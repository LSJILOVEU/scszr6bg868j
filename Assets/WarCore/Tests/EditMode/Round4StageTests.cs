using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    [TestFixture, Category("Round4Stage")]
    public sealed class Round4StageTests
    {
        [Test]
        public void ColdReactivationBeforeTheSameBoundaryDoesNotDuplicateAnActiveMember()
        {
            const int capacity = 4;
            var store = new EntityStore(capacity,IdentityMode.StableWorld);
            EntityId entity = store.Create(StableEntityKey.Placed(1,1));
            store.SynchronizeStableEntities();
            for (int i = 0; i < capacity+1; i++)
            {
                store.MakeCold(entity);
                entity = store.Reactivate(StableEntityKey.Placed(1,1).Id);
            }
            store.SynchronizeStableEntities();
            Assert.That(Keys(store,new EntityStage(store).Capture()),Is.EqualTo(new[] { store.StableOf(entity).Value }));
            Assert.That(store.ActiveEntityCount,Is.EqualTo(1));
        }

        [Test]
        public void ConsumersShareThePublishedSetUntilTheNextSynchronization()
        {
            var sim = new Simulation(1,config:new SimConfig(entityCapacity:16),identityMode:IdentityMode.StableWorld);
            for (ulong i = 0; i < 8; i++)
                sim.Entities.Create(StableEntityKey.Placed(1,i));
            sim.CompleteSpatialBoundary();
            var first = new EntityStage(sim.Entities);
            var second = new EntityStage(sim.Entities);
            ulong[] original = Keys(sim.Entities,first.Capture());
            sim.Entities.Create(StableEntityKey.Placed(1,100));
            Assert.That(Keys(sim.Entities,second.Capture()),Is.EqualTo(original));
            sim.CompleteSpatialBoundary();
            ulong[] after = Keys(sim.Entities,first.Capture());
            Assert.That(after.Length,Is.EqualTo(9));
            Assert.That(after,Is.Ordered);
            Assert.That(Keys(sim.Entities,second.Capture()),Is.EqualTo(after));
        }

        [Test]
        public void StandaloneCapturePublishesOnceAndCountRemainsTheLastPublication()
        {
            var store = new EntityStore(4,IdentityMode.StableWorld);
            EntityId first = store.Create(StableEntityKey.Placed(1,1));
            var stage = new EntityStage(store);
            Assert.That(store.ActiveEntityCount,Is.Zero);
            Assert.That(Keys(store,stage.Capture()),Is.EqualTo(new[] { store.StableOf(first).Value }));
            Assert.That(store.ActiveEntityCount,Is.EqualTo(1));
            EntityId second = store.Create(StableEntityKey.Placed(1,2));
            store.Destroy(first);
            Assert.That(Keys(store,stage.Capture()),Is.Empty,"Dead members are filtered; the new member is not yet published.");
            Assert.That(store.ActiveEntityCount,Is.EqualTo(1),"The published count still includes the dead first member.");
            store.SynchronizeStableEntities();
            Assert.That(Keys(store,stage.Capture()),Is.EqualTo(new[] { store.StableOf(second).Value }));
            Assert.That(store.ActiveEntityCount,Is.EqualTo(1));
        }

        [Test]
        public void BatchedReuseAndColdActivationMatchAnIndependentSortedList()
        {
            var sim = new Simulation(1,config:new SimConfig(entityCapacity:100),identityMode:IdentityMode.StableWorld,identityCapacity:1000);
            var stage = new EntityStage(sim.Entities);
            for (int tick = 0; tick < 40; tick++)
            {
                var previous = new List<EntityId>();
                foreach (EntityId entity in sim.Entities)
                    previous.Add(entity);
                for (int i = 0; i < previous.Count; i++)
                    if (i % 5 == 0)
                        sim.Entities.Destroy(previous[i]);
                sim.CompleteSpatialBoundary();
                // A dead addition has no replacement for dedupe to overwrite.
                sim.Entities.Destroy(sim.Entities.Create(StableEntityKey.World(2,tick,0)));
                for (ulong i = 0; i < 10; i++)
                    sim.Entities.Create(StableEntityKey.World(1,tick,i));
                sim.CompleteSpatialBoundary();
                var expected = new List<ulong>();
                foreach (EntityId entity in sim.Entities)
                    expected.Add(sim.Entities.StableOf(entity).Value);
                expected.Sort();
                Assert.That(Keys(sim.Entities,stage.Capture()),Is.EqualTo(expected));
                Assert.That(sim.Entities.ActiveEntityCount,Is.EqualTo(expected.Count));
            }
        }

        private static ulong[] Keys(EntityStore store, EntityStage.View view)
        {
            var result = new List<ulong>();
            foreach (EntityId entity in view)
                result.Add(store.StableOf(entity).Value);
            return result.ToArray();
        }
    }
}
