using NUnit.Framework;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    [TestFixture, Category("StableIdentity")]
    public sealed class Snap1CriticFix1Tests
    {
        [TestCase(false,false,false), TestCase(false,false,true)]
        [TestCase(false,true,false), TestCase(false,true,true)]
        [TestCase(true,false,false), TestCase(true,false,true)]
        [TestCase(true,true,false), TestCase(true,true,true)]
        public void RetiredBatchKeepsSortedCountersAcrossTwoRestores(bool retained, bool reverseEnqueue, bool reverseAllocation)
        {
            var uninterrupted = World(retained);
            var restored = World(retained);
            var source = new EntityLink(StableEntityKey.Placed(7,0).Id);
            var keys = new[] {
                new StableRequestKey(1,0,source.Id,0,0,11),
                new StableRequestKey(1,0,source.Id,0,0,12),
                new StableRequestKey(1,0,source.Id,0,1,10),
                new StableRequestKey(1,0,source.Id,1,0,9),
                new StableRequestKey(2,0,source.Id,0,0,8)
            };
            var values = new SpawnValues(SimVec3.Metres(4,5,6),SimVec3.Metres(0,0,1),17);
            foreach (Simulation sim in new[] { uninterrupted,restored })
            {
                EntityId parent = sim.Entities.Create(StableEntityKey.Placed(7,0));
                for (ulong i = 0; i < 2; i++)
                    sim.Spawns.Enqueue(source,new StableRequestKey(1,0,source.Id,0,i,9));
                sim.Step(default);
                Assert.That(sim.Spawns.Counter(source),Is.EqualTo(2UL));
                for (int i = 0; i < keys.Length; i++)
                    sim.Spawns.Enqueue(source,keys[reverseEnqueue ? keys.Length-1-i : i],values);
                sim.Entities.Destroy(parent);
                Assert.That(sim.Entities.LifeOf(source.Id),Is.EqualTo(IdentityLife.Retired));
                Assert.That(sim.Spawns.PendingCount,Is.EqualTo(5));
            }
            RestoreTwice(uninterrupted,restored,reverseAllocation);
            Continue(uninterrupted,restored);
            foreach (Simulation sim in new[] { uninterrupted,restored })
            {
                Assert.That(sim.Entities.RegisteredIdentityCount,Is.EqualTo(8));
                for (int i = 0; i < keys.Length; i++)
                {
                    EntityId child = Child(sim,source,(ulong)i+2,keys[i].Kind,7);
                    if (retained)
                    {
                        SpawnCapture capture = sim.Entities.Read(sim.Entities.Resolve<SpawnCapture>(),child);
                        Assert.That(capture.Spawner,Is.EqualTo(source));
                        Assert.That(capture.Kind,Is.EqualTo(keys[i].Kind));
                        Assert.That(capture.Chunk,Is.EqualTo(7));
                        Assert.That(capture.Values.Position,Is.EqualTo(values.Position));
                        Assert.That(capture.Values.Facing,Is.EqualTo(values.Facing));
                        Assert.That(capture.Values.Ownership,Is.EqualTo(values.Ownership));
                    }
                }
            }
        }

        [TestCase(false,false), TestCase(false,true)]
        [TestCase(true,false), TestCase(true,true)]
        public void InterleavedSourcesKeepCountersAfterSlotReuseAndRestore(bool retained, bool reverseAllocation)
        {
            var uninterrupted = World(retained);
            var restored = World(retained);
            var retired = new EntityLink(StableEntityKey.Placed(7,0).Id);
            var alive = new EntityLink(StableEntityKey.Placed(8,0).Id);
            var replacement = new EntityLink(StableEntityKey.Placed(9,0).Id);
            foreach (Simulation sim in new[] { uninterrupted,restored })
            {
                EntityId parent = sim.Entities.Create(StableEntityKey.Placed(7,0));
                sim.Entities.Create(StableEntityKey.Placed(8,0));
                sim.Spawns.Enqueue(retired,new StableRequestKey(1,0,retired.Id,0,0,9));
                for (ulong i = 0; i < 2; i++)
                    sim.Spawns.Enqueue(alive,new StableRequestKey(1,0,alive.Id,0,i,9));
                sim.Step(default);
                Assert.That(sim.Spawns.Counter(retired),Is.EqualTo(1UL));
                Assert.That(sim.Spawns.Counter(alive),Is.EqualTo(2UL));
                sim.Spawns.Enqueue(retired,new StableRequestKey(4,0,retired.Id,0,0,14));
                sim.Spawns.Enqueue(alive,new StableRequestKey(1,0,alive.Id,0,0,11));
                sim.Spawns.Enqueue(retired,new StableRequestKey(1,0,retired.Id,0,0,10));
                sim.Spawns.Enqueue(alive,new StableRequestKey(4,0,alive.Id,0,0,15));
                sim.Entities.Destroy(parent);
                sim.CompleteSpatialBoundary();
                EntityId reused = sim.Entities.Create(StableEntityKey.Placed(9,0));
                Assert.That(reused.Id,Is.EqualTo(parent.Id));
                Assert.That(reused.Generation,Is.Not.EqualTo(parent.Generation));
                Assert.That(sim.Entities.TryResolve(retired.Id,out _),Is.False);
                Assert.That(sim.Spawns.Counter(replacement),Is.Zero);
                sim.Spawns.Enqueue(replacement,new StableRequestKey(3,0,replacement.Id,0,0,13));
                sim.Spawns.Enqueue(replacement,new StableRequestKey(2,0,replacement.Id,0,0,12));
                Assert.That(sim.Spawns.PendingCount,Is.EqualTo(6));
            }
            RestoreTwice(uninterrupted,restored,reverseAllocation);
            Assert.That(restored.Entities.TryResolve(retired.Id,out _),Is.False);
            Assert.That(restored.Spawns.Counter(alive),Is.EqualTo(2UL));
            Assert.That(restored.Spawns.Counter(replacement),Is.Zero);
            Continue(uninterrupted,restored);
            foreach (Simulation sim in new[] { uninterrupted,restored })
            {
                Assert.That(sim.Entities.RegisteredIdentityCount,Is.EqualTo(12));
                Child(sim,retired,1,10,7);
                Child(sim,retired,2,14,7);
                Child(sim,alive,2,11,8);
                Child(sim,alive,3,15,8);
                Child(sim,replacement,0,12,9);
                Child(sim,replacement,1,13,9);
                Assert.That(sim.Spawns.Counter(alive),Is.EqualTo(4UL));
                Assert.That(sim.Spawns.Counter(replacement),Is.EqualTo(2UL));
            }
        }

        private static Simulation World(bool retained)
        {
            var config = new SimConfig(entityCapacity:12);
            return retained ? Simulation.CreateRetainedWorld(1,config)
                : new Simulation(1,config:config,identityMode:IdentityMode.StableWorld);
        }

        private static ulong Hash(Simulation sim) => sim.Entities.UsesRetainedProtocol
            ? sim.StateHashRetained() : sim.StateHashStable();

        private static byte[] Snapshot(Simulation sim) => sim.Entities.UsesRetainedProtocol
            ? sim.SaveRetainedSnapshot() : sim.SaveStableSnapshot();

        private static void RestoreTwice(Simulation uninterrupted, Simulation restored, bool reverseAllocation)
        {
            Assert.That(Hash(restored),Is.EqualTo(Hash(uninterrupted)),"pending tick=1");
            int pending = restored.Spawns.PendingCount;
            byte[] snapshot = Snapshot(restored);
            for (int round = 0; round < 2; round++)
            {
                bool reverse = round == 0 ? reverseAllocation : !reverseAllocation;
                if (restored.Entities.UsesRetainedProtocol)
                    restored.RestoreRetainedSnapshot(snapshot,reverse);
                else
                    restored.RestoreStableSnapshot(snapshot,reverse);
                Assert.That(Hash(restored),Is.EqualTo(Hash(uninterrupted)),"restored tick=1 round="+round);
                Assert.That(restored.Spawns.PendingCount,Is.EqualTo(pending));
                byte[] next = Snapshot(restored);
                Assert.That(next,Is.EqualTo(snapshot),"snapshot round="+round);
                snapshot = next;
            }
        }

        private static void Continue(Simulation uninterrupted, Simulation restored)
        {
            for (int tick = 2; tick <= 4; tick++)
            {
                uninterrupted.Step(default);
                restored.Step(default);
                Assert.That(Hash(restored),Is.EqualTo(Hash(uninterrupted)),"continued tick="+tick);
            }
            Assert.That(uninterrupted.Spawns.PendingCount,Is.Zero);
            Assert.That(restored.Spawns.PendingCount,Is.Zero);
        }

        private static EntityId Child(Simulation sim, EntityLink source, ulong counter, ulong kind, ulong chunk)
        {
            StableId id = StableEntityKey.Spawned(source.Id,counter,kind).Id;
            Assert.That(sim.Entities.TryResolve(id,out EntityRef child),Is.True);
            Assert.That(sim.Entities.ChunkOf(id),Is.EqualTo(chunk));
            return child;
        }
    }
}
