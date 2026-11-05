using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    [TestFixture, Category("StableIdentity")]
    public sealed class StableStageTests
    {
        internal sealed class Probe : ISimEntitySystem, ISimStateSerializer
        {
            internal readonly List<ulong> Log = new List<ulong>();
            public int Order => 1;
            public string StateTypeId => "test.stable-probe";
            public int StateVersion => 1;
            public void StepEntity(SimContext context, EntityId entity) => Log.Add(context.Entities.StableOf(entity).Value);
            public void WriteState(StateHash hash) { }
            public void SaveState(SimStateWriter writer) { }
            public void LoadState(SimStateReader reader) { }
        }

        [Test]
        public void ReducedPhaseAndSharedSideEffectsFollowStableOrderAtEveryTick()
        {
            var sim = new Simulation(1, config: new SimConfig(entityCapacity:32), identityMode: IdentityMode.StableWorld);
            var tiers = sim.Entities.Register<SimTier>("tier", new SimTierSerializer());
            var keys = new List<ulong>();
            for (ulong i = 0; i < 24; i++)
            {
                EntityId entity = sim.Entities.Create(StableEntityKey.Placed(7, i));
                sim.Entities.Set(tiers, entity, SimTier.Reduced);
                keys.Add(sim.Entities.StableOf(entity).Value);
            }
            keys.Sort();
            var probe = new Probe();
            sim.RegisterTiered("probe", probe, tiers, 7);
            for (int tick = 1; tick <= 21; tick++)
            {
                probe.Log.Clear();
                sim.Step(default);
                var expected = keys.FindAll(key => key % 7 == (ulong)tick % 7);
                Assert.That(probe.Log, Is.EqualTo(expected), "tick=" + tick);
            }
        }

        [Test]
        public void ElapsedPhasePreservesIntegrationWhileChangingOnlyIdentity()
        {
            var sim = new Simulation(1, config:new SimConfig(entityCapacity:8), identityMode:IdentityMode.StableWorld);
            var positions = sim.Entities.Register<Position>("position", new PositionSerializer());
            var movements = sim.Entities.Register<Movement>("movement", new MovementSerializer());
            var tiers = sim.Entities.Register<SimTier>("tier", new SimTierSerializer());
            EntityId entity = sim.Entities.Create(StableEntityKey.Placed(3, 9));
            sim.Entities.Set(positions, entity, new Position(default));
            sim.Entities.Set(movements, entity, new Movement(MoveMode.Flying, SimVec3.Metres(30,0,0)));
            sim.Entities.Set(tiers, entity, SimTier.Reduced);
            sim.RegisterTiered("move", new MovementSystem(sim.Entities,positions,movements,new NavProviderRegistry(),1), tiers, 7);
            long last = 0;
            for (int tick = 1; tick <= 21; tick++)
            {
                sim.Step(default);
                if ((ulong)tick % 7 == sim.Entities.StableOf(entity).Value % 7 || tick % 7 == 0)
                    last = tick;
                Assert.That(sim.Entities.Read(positions,entity).Value.X.Raw, Is.EqualTo(last * SimFix.Scale), "tick=" + tick);
            }
        }

        [Test]
        public void FrozenStageExcludesNewEntitiesAndRejectsStaleGenerations()
        {
            var entities = new EntityStore(16, IdentityMode.StableWorld);
            for (ulong i = 0; i < 8; i++)
                entities.Create(StableEntityKey.Placed(1, i));
            var stage = new EntityStage(entities);
            var snapshot = stage.Capture();
            entities.Create(StableEntityKey.Placed(1, 100));
            var removed = entities.Resolve(new EntityLink(StableEntityKey.Placed(1,3).Id));
            entities.Destroy(removed);
            entities.RecycleDestroyed();
            var reused = entities.Create(StableEntityKey.Placed(1,101));
            Assert.That(reused.Slot,Is.EqualTo(removed.Id));
            var ids = new List<ulong>();
            foreach (EntityId entity in snapshot)
                ids.Add(entities.StableOf(entity).Value);
            Assert.That(ids.Count, Is.EqualTo(7));
            Assert.That(ids.Contains(StableEntityKey.Placed(1,101).Id.Value),Is.False);
            Assert.That(ids, Is.Ordered);
        }
    }
}
