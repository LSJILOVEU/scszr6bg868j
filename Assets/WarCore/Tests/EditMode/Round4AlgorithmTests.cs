using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    [TestFixture, Category("Round4Algorithms")]
    public sealed class Round4AlgorithmTests
    {
        [Test]
        public void CorrelatedTaskAndRequestFieldsDoNotCollapseTheLookupHash()
        {
            var tasks = new HashSet<int>();
            var requests = new HashSet<int>();
            for (ulong i = 0; i < 1024; i++)
            {
                tasks.Add(new TaskKey(1,0,new StableId(i),i,9).GetHashCode());
                requests.Add(new StableRequestKey(1,0,new StableId(i),0,i,9).GetHashCode());
            }
            Assert.That(tasks.Count,Is.GreaterThan(1000),"Correlated origin/counter values must not form one probe cluster.");
            Assert.That(requests.Count,Is.GreaterThan(1000));
        }

        [Test]
        public void ThousandReversedSpawnRequestsUseCanonicalOrdinalsAndCapturedPosition()
        {
            var sim = Simulation.CreateRetainedWorld(1,new SimConfig(entityCapacity:1024));
            var positions = sim.Entities.Register<Position>("position",new PositionSerializer());
            EntityId source = sim.Entities.Create(StableEntityKey.Placed(1,1));
            EntityLink link = sim.Entities.Link(source);
            sim.Entities.Set(positions,source,new Position(SimVec3.Metres(3,4,5)));
            for (int i = 999; i >= 0; i--)
                sim.Spawns.Enqueue(link,new StableRequestKey(1,0,link.Id,0,(ulong)i,(ulong)i));
            sim.Entities.Set(positions,source,new Position(SimVec3.Metres(8,9,10)));
            sim.Entities.Destroy(source);
            sim.Spawns.Commit();
            for (ulong i = 0; i < 1000; i++)
            {
                EntityId child = sim.Entities.Resolve(new EntityLink(StableEntityKey.Spawned(link.Id,i,i).Id));
                Assert.That(sim.Entities.Read(sim.Entities.Resolve<SpawnCapture>(),child).Values.Position,Is.EqualTo(SimVec3.Metres(3,4,5)));
            }
            Assert.That(sim.Spawns.PendingCount,Is.Zero);
        }

        [Test]
        public void ThousandTasksKeepLookupAndQueueOrderAfterCancelRestoreAndDequeue()
        {
            var sim = Simulation.CreateRetainedWorld(1,new SimConfig(entityCapacity:1024,timerCapacity:1024));
            var keys = new TaskKey[1000];
            for (int i = 999; i >= 0; i--)
            {
                EntityId entity = sim.Entities.Create(StableEntityKey.Placed(0,(ulong)i));
                keys[i] = sim.Scheduler.ScheduleFor(1,0,sim.Entities.StableOf(entity),7,new StableSimEvent(new EventTypeId(100)));
            }
            for (int i = 0; i < 1000; i += 2)
                Assert.That(sim.Scheduler.CancelStable(keys[i]),Is.True);
            sim.RestoreRetainedSnapshot(sim.SaveRetainedSnapshot(),true);
            sim.Step(default);
            ulong previous = 0;
            int count = 0;
            while (sim.Scheduler.TryDequeueStable(out StableScheduledTask task))
            {
                Assert.That(task.Key.Origin.Value,Is.GreaterThan(previous));
                previous = task.Key.Origin.Value;
                count++;
                Assert.That(sim.Scheduler.CancelStable(task.Key),Is.False);
            }
            Assert.That(count,Is.EqualTo(500));
        }
    }
}
