using NUnit.Framework;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    [TestFixture, Category("Round4Capacity")]
    public sealed class Round4CapacityTests
    {
        [Test]
        public void SpawnAndActivationDefaultsDoNotBorrowTimerCapacity()
        {
            var sim = Simulation.CreateRetainedWorld(1,new SimConfig(entityCapacity:8,timerCapacity:1));
            sim.Spawns.EnqueueWorld(0,new StableRequestKey(1,1,new StableId(1),0,0,1));
            sim.Spawns.EnqueueWorld(0,new StableRequestKey(1,1,new StableId(1),0,1,1));
            sim.Spawns.Commit();
            Assert.That(sim.Entities.ActiveEntityCount,Is.Zero,"Publication is deferred.");
            Assert.That(sim.Entities.RegisteredIdentityCount,Is.EqualTo(2));
            sim.Activations.Plan(1,1);
            sim.Activations.Plan(2,2);
        }

        [Test]
        public void EveryIndependentCapacityRejectsBeforeConsumingAuthority()
        {
            var config = new SimConfig(entityCapacity:8,timerCapacity:1,spawnRequestCapacity:2,worldCursorCapacity:1,
                motionRequestCapacity:2,activationCapacity:1,taskSystemCapacity:1);
            var sim = Simulation.CreateRetainedWorld(1,config);
            sim.Spawns.EnqueueWorld(0,new StableRequestKey(1,1,new StableId(1),0,0,1));
            sim.Spawns.EnqueueWorld(0,new StableRequestKey(2,1,new StableId(2),0,0,1));
            ulong before = sim.StateHashRetained();
            Assert.That(Assert.Throws<SimBoundaryException>(() => sim.Spawns.EnqueueWorld(0,
                new StableRequestKey(3,1,new StableId(3),0,0,1))).Message,Does.Contain("request capacity"));
            Assert.That(Assert.Throws<SimBoundaryException>(() => sim.Spawns.Commit()).Message,Does.Contain("cursor capacity"));
            Assert.That(sim.StateHashRetained(),Is.EqualTo(before));
            Assert.That(sim.Entities.RegisteredIdentityCount,Is.Zero);
            sim.Activations.Plan(1,1);
            Assert.Throws<SimBoundaryException>(() => sim.Activations.Plan(2,2));
            TaskKey task = sim.Scheduler.ScheduleFor(1,1,new StableId(1),1,new StableSimEvent(new EventTypeId(100)));
            sim.Scheduler.CancelStable(task);
            Assert.Throws<SimBoundaryException>(() => sim.Scheduler.ScheduleFor(1,1,new StableId(2),1,new StableSimEvent(new EventTypeId(100))));
        }

        [Test]
        public void MotionCapacityIsIndependentAndDuplicateKeysRemainImmediateErrors()
        {
            var sim = new Simulation(1,config:new SimConfig(entityCapacity:8,timerCapacity:1,motionRequestCapacity:2),
                identityMode:IdentityMode.StableWorld);
            var positions = sim.Entities.Register<Position>("position",new PositionSerializer());
            var colliders = sim.Entities.Register<Collider>("collider",new ColliderSerializer());
            EntityId entity = sim.Entities.Create(StableEntityKey.Placed(1,1));
            EntityLink link = sim.Entities.Link(entity);
            sim.Entities.Set(positions,entity,new Position(default));
            sim.Entities.Set(colliders,entity,new Collider(SimFix.FromInt(1),SimFix.FromInt(2)));
            sim.ConfigureSpatial(positions,colliders,SimFix.FromInt(4));
            sim.RequestPosition(link,SimVec3.Metres(1,0,0),Round4DeathTests.Key(0));
            Assert.Throws<SimBoundaryException>(() => sim.RequestPosition(link,default,Round4DeathTests.Key(0)));
            sim.RequestPosition(link,SimVec3.Metres(2,0,0),Round4DeathTests.Key(1));
            Assert.Throws<SimBoundaryException>(() => sim.RequestPosition(link,default,Round4DeathTests.Key(2)));
            sim.Step(default);
            Assert.That(sim.Entities.Read(positions,entity).Value,Is.EqualTo(SimVec3.Metres(2,0,0)));
        }

        [Test]
        public void RetiredRecordsStillAffectTheHashUntilTheirExpiry()
        {
            var a = Round4ProtocolTests.World(4);
            var b = Round4ProtocolTests.World(4);
            a.Entities.Destroy(a.Entities.Create(StableEntityKey.World(1,0,0)));
            b.Entities.Destroy(b.Entities.Create(StableEntityKey.World(2,0,0)));
            a.CompleteSpatialBoundary();
            b.CompleteSpatialBoundary();
            Assert.That(a.StateHashRetained(),Is.Not.EqualTo(b.StateHashRetained()));
            for (int i = 0; i < 4; i++)
            {
                a.Step(default);
                b.Step(default);
            }
            Assert.That(a.StateHashRetained(),Is.EqualTo(b.StateHashRetained()));
        }

        [Test]
        public void SimultaneousPassengerAndHostDeathsHaveOrderIndependentDiagnostics()
        {
            var a = Round4DeathTests.Scene();
            var b = Round4DeathTests.Scene();
            EntityLink passenger = Round4DeathTests.Link(0);
            EntityLink host = Round4DeathTests.Link(1);
            a.Attach(passenger,host,default,Round4DeathTests.Key());
            b.Attach(passenger,host,default,Round4DeathTests.Key());
            a.Step(default);
            b.Step(default);
            a.Entities.Destroy(a.Entities.Resolve(passenger));
            a.Entities.Destroy(a.Entities.Resolve(host));
            b.Entities.Destroy(b.Entities.Resolve(host));
            b.Entities.Destroy(b.Entities.Resolve(passenger));
            a.Step(default);
            b.Step(default);
            Assert.That(a.RequestDiagnostics.Total,Is.Zero);
            Assert.That(b.RequestDiagnostics.Total,Is.Zero);
            Assert.That(a.StateHashStable(),Is.EqualTo(b.StateHashStable()));
        }

        [Test]
        public void BoundedLookupBackshiftKeepsClusterMembersAndRejectsOverflow()
        {
            var lookup = new BoundedLookup<int>(4);
            lookup.Add(1,0);
            lookup.Add(9,1);
            lookup.Add(17,2);
            lookup.Add(25,3);
            Assert.Throws<SimBoundaryException>(() => lookup.Add(33,4));
            lookup.Remove(1);
            Assert.That(lookup.Find(9),Is.EqualTo(1));
            Assert.That(lookup.Find(17),Is.EqualTo(2));
            Assert.That(lookup.Find(25),Is.EqualTo(3));
            lookup.Add(33,4);
            lookup.Remove(17);
            Assert.That(lookup.Find(33),Is.EqualTo(4));
            Assert.That(lookup.Find(17),Is.EqualTo(-1));
            Assert.Throws<SimBoundaryException>(() => lookup.Add(9,5));
        }

        [Test]
        public void CleanupNoticesRemainOrderIndependentWithTinyRequestDiagnosticBuffer()
        {
            var a = Round4DeathTests.Scene(1);
            var b = Round4DeathTests.Scene(1);
            for (ulong i = 0; i < 2; i++)
            {
                a.Attach(Round4DeathTests.Link(i),Round4DeathTests.Link(i+2),default,Round4DeathTests.Key(i));
                b.Attach(Round4DeathTests.Link(i),Round4DeathTests.Link(i+2),default,Round4DeathTests.Key(i));
            }
            a.Step(default);
            b.Step(default);
            for (ulong i = 0; i < 4; i++)
            {
                a.Entities.Destroy(a.Entities.Resolve(Round4DeathTests.Link(i)));
                b.Entities.Destroy(b.Entities.Resolve(Round4DeathTests.Link((i+2)%4)));
            }
            a.Step(default);
            b.Step(default);
            Assert.That(a.RequestDiagnostics.Total,Is.Zero);
            Assert.That(b.RequestDiagnostics.Total,Is.Zero);
            Assert.That(b.RequestDiagnostics.SuppressedTotal,Is.Zero);
            Assert.That(a.StateHashStable(),Is.EqualTo(b.StateHashStable()));
        }
    }
}
