using System;
using System.Reflection;
using System.IO;
using System.Security.Cryptography;
using NUnit.Framework;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    [TestFixture, Category("StableQueryOptimization"), Category("StableIdentity")]
    public sealed class StableQueryOptimizationTests
    {
        private static Simulation Scene(bool legacy = false)
        {
            var sim = new Simulation(913,config:new SimConfig(entityCapacity:96,spatialMaxCellsPerBody:1),
                identityMode:legacy ? IdentityMode.Legacy : IdentityMode.StableWorld,identityCapacity:128);
            var positions = sim.Entities.Register<Position>("p",new PositionSerializer());
            var colliders = sim.Entities.Register<Collider>("c",new ColliderSerializer());
            for (int i = 0; i < 80; i++)
            {
                EntityId entity = legacy ? sim.Entities.Create() : (EntityId)sim.Entities.Create(StableEntityKey.Placed(3,(ulong)((i*37)%80)));
                sim.Entities.Set(positions,entity,new Position(default));
                sim.Entities.Set(colliders,entity,new Collider(SimFix.FromInt(2),SimFix.FromInt(4),1,isStatic:i%2==0));
            }
            sim.ConfigureSpatial(positions,colliders,SimFix.FromInt(1));
            sim.CompleteWarmup();
            return sim;
        }

        private static SpatialGridState State(Simulation sim)
            => (SpatialGridState)typeof(GridSpatialIndex).GetField("active",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(sim.SpatialIndex);

        private static int[] Ordered(SpatialPartitionState partition)
        {
            FieldInfo field = typeof(SpatialPartitionState).GetField("OrderedLargeIds",BindingFlags.Instance|BindingFlags.NonPublic);
            Assert.That(field,Is.Not.Null,"AllCapped needs its own derived order; Any must retain the published scan order.");
            return (int[])field.GetValue(partition);
        }

        private static void Audit(Simulation sim)
        {
            MethodInfo method = typeof(GridSpatialIndex).GetMethod("ValidateStableQueryOrder",BindingFlags.Instance|BindingFlags.NonPublic,null,Type.EmptyTypes,null);
            Assert.That(method,Is.Not.Null,"A derived order needs a table-consistency guard.");
            try { method.Invoke(sim.SpatialIndex,null); }
            catch (TargetInvocationException error) { throw error.InnerException; }
        }

        [Test]
        public void StableOrderIsSeparateAndLegacyReservesNoPermutation()
        {
            var stable = Scene();
            var legacy = Scene(true);
            var state = State(stable);
            Audit(stable);
            foreach (SpatialPartitionState partition in new[] { state.Static, state.Unit })
            {
                int[] sorted = Ordered(partition);
                Assert.That(sorted,Is.Not.SameAs(partition.LargeIds));
                bool changed = false;
                for (int i = 0; i < partition.LargeCount; i++)
                {
                    if (i > 0)
                    {
                        Assert.That(partition.LargeIds[i],Is.GreaterThan(partition.LargeIds[i-1]));
                        Assert.That(stable.Entities.StableAt(sorted[i]).Value,Is.GreaterThan(stable.Entities.StableAt(sorted[i-1]).Value));
                    }
                    changed |= sorted[i] != partition.LargeIds[i];
                }
                Assert.That(changed,Is.True,"The keys must not accidentally match slot order.");
            }
            Assert.That(Ordered(State(legacy).Static),Is.Null);
            Assert.That(Ordered(State(legacy).Unit),Is.Null);
        }

        [Test]
        public void DerivedOrderGuardRejectsPermutationOrIdentityTableCorruption()
        {
            var sim = Scene();
            int[] order = Ordered(State(sim).Static);
            int first = order[0];
            order[0] = order[1];
            Assert.Throws<SimBoundaryException>(() => Audit(sim));
            order[0] = first;
            Audit(sim);
            var table = (StableId[])typeof(EntityStore).GetField("stableIds",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(sim.Entities);
            StableId saved = table[first];
            table[first] = new StableId(ulong.MaxValue);
            Assert.Throws<SimBoundaryException>(() => Audit(sim),"An order that no longer agrees with the identity table must fail.");
            table[first] = saved;
            Audit(sim);
        }

        [Test]
        public void RebuildRestoreAndSlotReuseRegenerateOrderWithoutSerializingIt()
        {
            var sim = Scene();
            var request = SpatialQueries.Sphere(default,SimFix.FromInt(8));
            var hits = new SpatialHit[96];
            sim.SpatialQuery.AllCapped(request,96,hits);
            var expected = new ulong[80];
            for (int i = 0; i < expected.Length; i++)
                expected[i] = sim.Entities.StableOf(hits[i].Entity).Value;
            ulong hash = sim.StateHashStable();
            byte[] snapshot = sim.SaveStableSnapshot();
            int[] order = Ordered(State(sim).Static);
            int first = order[0];
            order[0] = order[1];
            Assert.That(sim.StateHashStable(),Is.EqualTo(hash));
            Assert.That(sim.SaveStableSnapshot(),Is.EqualTo(snapshot),"Only the derived permutation was corrupted.");
            order[0] = first;
            EntityId removed = hits[0].Entity;
            sim.Entities.Destroy(removed);
            sim.CompleteSpatialBoundary();
            Audit(sim);
            EntityId replacement = sim.Entities.Create(StableEntityKey.Placed(99,99));
            Assert.That(replacement.Id,Is.EqualTo(removed.Id));
            sim.Entities.Set(sim.Entities.Resolve<Position>(),replacement,new Position(default));
            sim.Entities.Set(sim.Entities.Resolve<Collider>(),replacement,new Collider(SimFix.FromInt(2),SimFix.FromInt(4),1,isStatic:true));
            sim.CompleteSpatialBoundary();
            Audit(sim);
            sim.RestoreStableSnapshot(snapshot,reverseAllocation:true);
            Audit(sim);
            Assert.That(sim.StateHashStable(),Is.EqualTo(hash));
            var result = sim.SpatialQuery.AllCapped(request,96,hits);
            Assert.That(result.TotalHits,Is.EqualTo(80));
            for (int i = 0; i < expected.Length; i++)
                Assert.That(sim.Entities.StableOf(hits[i].Entity).Value,Is.EqualTo(expected[i]));
        }

        [Test]
        public void StableOrderReplayFingerprintRejectsStaleProductCopies()
        {
            string path = Path.Combine(ArchitectureTests.AssetRoot(),"WarCore/Sim/GridSpatialIndex.StableOrder.cs");
            using var sha = SHA256.Create();
            string digest = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-","").ToLowerInvariant();
            Assert.That(digest,Is.EqualTo(Diagnostics.DiagnosticSourceStamp.GridSpatialIndex_StableOrder));
            Assert.That(digest,Is.Not.EqualTo(new string('0',64)),"The source stamp must not accept a known-wrong digest.");
        }

        [Test]
        public void QueryBudgetControlRejectsSixPercentAndInvalidClocks()
        {
            Assert.That(StableIdentityBench.WithinQueryBudget(StableIdentityBench.QueryIncrease(1000,1049)),Is.True);
            Assert.That(StableIdentityBench.WithinQueryBudget(StableIdentityBench.QueryIncrease(1000,1050)),Is.True);
            Assert.That(StableIdentityBench.WithinQueryBudget(StableIdentityBench.QueryIncrease(1000,1060)),Is.False);
            Assert.That(StableIdentityBench.WithinQueryBudget(double.NaN),Is.False);
            Assert.Throws<InvalidOperationException>(() => StableIdentityBench.QueryIncrease(0,1));
        }
    }
}
