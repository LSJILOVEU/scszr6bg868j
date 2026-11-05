using NUnit.Framework;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    [TestFixture, Category("Round4Retention")]
    public sealed class Round4RetentionTests
    {
        [Test]
        public void RetirementExpiresAtTheDeclaredBoundaryAndReleasesCapacity()
        {
            var sim = new Simulation(1, config:new SimConfig(entityCapacity:4),
                identityMode:IdentityMode.StableWorld, identityCapacity:4, retiredRetentionTicks:2);
            for (ulong i = 0; i < 4; i++)
                sim.Entities.Destroy(sim.Entities.Create(StableEntityKey.World(1,0,i)));
            sim.CompleteSpatialBoundary();
            sim.Step(default);
            Assert.That(sim.Entities.RegisteredIdentityCount, Is.EqualTo(4));
            sim.Step(default);
            Assert.That(sim.Entities.RegisteredIdentityCount, Is.Zero);
            Assert.DoesNotThrow(() => sim.Entities.Create(StableEntityKey.World(1,2,0)));
        }

        [Test]
        public void PeakIdentityCountTracksOccupancyAndRestoreWithoutChangingProtocol()
        {
            var config = new SimConfig(entityCapacity:4);
            var options = new RetainedWorldOptions(1,4);
            var sim = Simulation.CreateRetainedWorld(1,config,options);
            Assert.That(sim.Entities.PeakIdentityCount,Is.Zero);
            var entities = new EntityId[4];
            for (ulong i = 0; i < 4; i++)
            {
                entities[i] = sim.Entities.Create(StableEntityKey.Placed(1,i));
                Assert.That(sim.Entities.PeakIdentityCount,Is.EqualTo(i+1));
            }
            sim.Entities.MakeCold(entities[0]);
            Assert.That(sim.Entities.RegisteredIdentityCount,Is.EqualTo(4));
            Assert.That(sim.Entities.PeakIdentityCount,Is.EqualTo(4));
            entities[0] = sim.Entities.Reactivate(StableEntityKey.Placed(1,0).Id);
            Assert.Throws<SimBoundaryException>(() => sim.Entities.Create(StableEntityKey.Placed(1,5)));
            Assert.That(sim.Entities.PeakIdentityCount,Is.EqualTo(4));
            byte[] full = sim.SaveRetainedSnapshot();
            foreach (EntityId entity in entities)
                sim.Entities.Destroy(entity);
            sim.Step(default);
            Assert.That(sim.Entities.RegisteredIdentityCount,Is.Zero);
            Assert.That(sim.Entities.PeakIdentityCount,Is.EqualTo(4));
            byte[] empty = sim.SaveRetainedSnapshot();
            var restored = Simulation.CreateRetainedWorld(1,config,options);
            restored.RestoreRetainedSnapshot(empty);
            Assert.That(restored.Entities.PeakIdentityCount,Is.Zero);
            Assert.That(restored.StateHashRetained(),Is.EqualTo(sim.StateHashRetained()));
            Assert.That(restored.SaveRetainedSnapshot(),Is.EqualTo(empty));
            restored.RestoreRetainedSnapshot(full,true);
            Assert.That(restored.Entities.PeakIdentityCount,Is.EqualTo(4));
            restored.RestoreRetainedSnapshot(empty);
            Assert.That(restored.Entities.RegisteredIdentityCount,Is.Zero);
            Assert.That(restored.Entities.PeakIdentityCount,Is.EqualTo(4));
            Assert.That(restored.SaveRetainedSnapshot(),Is.EqualTo(empty));
        }

        [Test]
        public void ConstructorCapacityErrorsIncludeTheLifecycleBudgetRule()
        {
            var error = Assert.Throws<SimBoundaryException>(() => Simulation.CreateRetainedWorld(1,
                new SimConfig(entityCapacity:4),new RetainedWorldOptions(2,3)));
            Assert.That(error.Message,Does.Contain("identityCapacity >= maximumLiveCount + maximumDestroyedPerTick * (retentionTicks + 1)"));
            Assert.That(error.Message,Does.Contain("entityCapacity >= maximumLiveCount + maximumCreatedPerTick"));
            var invalid = Assert.Throws<SimBoundaryException>(() => new RetainedWorldOptions(2,-1));
            Assert.That(invalid.Message,Does.Contain("identityCapacity >= maximumLiveCount"));
        }

        [Test]
        public void DefaultIdentityCapacityAllowsLiveSlotsAndRetirementTogether()
        {
            var sim = new Simulation(1, config:new SimConfig(entityCapacity:4), identityMode:IdentityMode.StableWorld);
            for (ulong i = 0; i < 4; i++)
                sim.Entities.Destroy(sim.Entities.Create(StableEntityKey.World(1,0,i)));
            sim.CompleteSpatialBoundary();
            Assert.DoesNotThrow(() => sim.Entities.Create(StableEntityKey.World(1,1,0)));
        }

        [Test]
        public void EntityTaskCursorsDoNotConsumeTimerCapacityAfterOwnerRetirement()
        {
            var sim = new Simulation(1, config:new SimConfig(entityCapacity:4,timerCapacity:1,taskSystemCapacity:1),
                identityMode:IdentityMode.StableWorld, identityCapacity:16, retiredRetentionTicks:1);
            for (int tick = 0; tick < 8; tick++)
            {
                EntityId entity = sim.Entities.Create(StableEntityKey.World(1,tick,0));
                StableId id = sim.Entities.StableOf(entity);
                TaskKey key = sim.Scheduler.ScheduleFor(tick+1,0,id,1,new StableSimEvent(new EventTypeId(100)));
                Assert.That(key.Counter, Is.Zero);
                Assert.That(sim.Scheduler.CancelStable(key), Is.True);
                TaskKey next = sim.Scheduler.ScheduleFor(tick+1,0,id,1,new StableSimEvent(new EventTypeId(100)));
                Assert.That(next.Counter, Is.EqualTo(1), "Cancellation must not reuse the entity task counter.");
                Assert.That(sim.Scheduler.CancelStable(next), Is.True);
                sim.Entities.Destroy(entity);
                sim.Step(default);
            }
            Assert.That(sim.Entities.RegisteredIdentityCount, Is.Zero);
        }
    }
}
