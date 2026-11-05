using System;
using NUnit.Framework;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    [TestFixture, Category("StableIdentity")]
    public sealed class StableBoundaryTests
    {
        [Test]
        public void ColdTimelineContinuesFromItsLastSimulatedTickAfterRestore()
        {
            var sim = StableSnapshotTests.Scene(false);
            var link = new EntityLink(StableEntityKey.Placed(1,1).Id);
            sim.Step(default);
            EntityId entity = sim.Entities.Resolve(link);
            sim.Entities.MakeCold(entity);
            for (int i = 0; i < 10; i++)
                sim.Step(default);
            sim.RestoreStableSnapshot(sim.SaveStableSnapshot(),true);
            EntityId active = sim.Entities.Reactivate(link.Id);
            sim.Step(default);
            long delta = SimFix.FromInt(3).Raw/30;
            Assert.That(sim.Entities.Read(sim.Entities.Resolve<Position>(),active).Value.X.Raw,
                Is.EqualTo(SimFix.Scale+12*delta));
        }
        [Test]
        public void NullAndIdentityZeroAreDistinctAndFullKeysResolveThroughBucketCollisions()
        {
            var store = new EntityStore(64,IdentityMode.StableWorld);
            var source = StableEntityKey.Placed(1,1);
            EntityId zero = store.CreateIdentity(source,new StableId(0));
            EntityLink link = store.Link(zero);
            Assert.That(link.HasValue,Is.True);
            Assert.That(link.Id.Value,Is.Zero);
            Assert.That(default(EntityLink).HasValue,Is.False);
            Assert.That(store.Resolve(link),Is.EqualTo(zero));
            for (ulong i = 2; i < 60; i++)
            {
                var key = StableEntityKey.Placed(1,i);
                EntityRef entity = store.Create(key);
                Assert.That(store.TryResolve(key.Id,out EntityRef found),Is.True);
                Assert.That(found,Is.EqualTo(entity));
            }
        }
        [Test]
        public void ExhaustedSpawnCounterRejectsTheWholeBatchWithoutConsumingAnything()
        {
            var sim = new Simulation(1,identityMode:IdentityMode.StableWorld);
            EntityId entity = sim.Entities.Create(StableEntityKey.Placed(1,1));
            var link = sim.Entities.Link(entity);
            sim.Entities.SetSpawnSequence(entity,ulong.MaxValue);
            sim.Spawns.Enqueue(link,new StableRequestKey(1,0,link.Id,1,0,1));
            ulong hash = sim.StateHashStable();
            Assert.Throws<SimBoundaryException>(() => sim.Spawns.Commit());
            Assert.That(sim.StateHashStable(),Is.EqualTo(hash));
            Assert.That(sim.Entities.RegisteredIdentityCount,Is.EqualTo(1));
            Assert.Throws<SimBoundaryException>(() => sim.Scheduler.ScheduleStable(
                new TaskKey(1,0,link.Id,ulong.MaxValue,1),new StableSimEvent(new EventTypeId(100))));
            Assert.That(sim.StateHashStable(),Is.EqualTo(hash));
        }
        [Test]
        public void StrongSeatReferenceFailsBeforeComponentWrite()
        {
            var sim = new Simulation(1,identityMode:IdentityMode.StableWorld);
            var seats = sim.Entities.Register<Seat>("seat",new SeatSerializer());
            EntityId entity = sim.Entities.Create(StableEntityKey.Placed(1,1));
            Assert.Throws<SimBoundaryException>(() => sim.Entities.Set(seats,entity,new Seat(new EntityLink(new StableId(7)),0)));
            Assert.That(sim.Entities.TryGet(seats,entity,out _),Is.False);
        }
        [Test]
        public void WorldSpawnsResumeOrdinalsAfterPendingSnapshotAndInterleavedSystems()
        {
            var sim = new Simulation(1,config:new SimConfig(entityCapacity:16),identityMode:IdentityMode.StableWorld);
            for (ulong i = 0; i < 4; i++)
            {
                sim.Spawns.EnqueueWorld(0,new StableRequestKey(2,1,new StableId(2),1,3-i,1));
                sim.Spawns.EnqueueWorld(0,new StableRequestKey(1,1,new StableId(1),1,i,1));
            }
            sim.RestoreStableSnapshot(sim.SaveStableSnapshot(),true);
            sim.Spawns.Commit();
            for (ulong system = 1; system <= 2; system++)
                for (ulong i = 0; i < 4; i++)
                    Assert.That(sim.Entities.TryResolve(StableEntityKey.World(system,0,i).Id,out _),Is.True);
            sim.Spawns.EnqueueWorld(0,new StableRequestKey(1,1,new StableId(1),1,9,1));
            sim.Spawns.Commit();
            Assert.That(sim.Entities.TryResolve(StableEntityKey.World(1,0,4).Id,out _),Is.True);
        }
    }
}
