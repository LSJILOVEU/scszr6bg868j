using System;
using NUnit.Framework;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    [TestFixture, Category("StableIdentity")]
    public sealed class StableContinuationTests
    {
        [Test]
        public void CapturedControlAndAttachmentLinksRebindAfterColdReactivation()
        {
            var a = StableSnapshotTests.Scene(false);
            var b = StableSnapshotTests.Scene(true);
            var host = new EntityLink(StableEntityKey.Placed(1,0).Id);
            var rider = new EntityLink(StableEntityKey.Placed(1,1).Id);
            var controlA = new ControlTargetSystem(a.Entities,a.Entities.Resolve(host),new StableSystemTests.Handler(),new EventTypeId(600),2);
            var controlB = new ControlTargetSystem(b.Entities,b.Entities.Resolve(host),new StableSystemTests.Handler(),new EventTypeId(600),2);
            a.Register("control",controlA);
            b.Register("control",controlB);
            var key = new StableRequestKey(1,1,new StableId(1),1,0,1);
            a.Attach(rider,host,SimVec3.Metres(0,2,0),key);
            b.Attach(rider,host,SimVec3.Metres(0,2,0),key);
            a.Step(default);
            b.Step(default);
            a.Entities.MakeCold(a.Entities.Resolve(host));
            a.Entities.MakeCold(a.Entities.Resolve(rider));
            b.Entities.MakeCold(b.Entities.Resolve(host));
            b.Entities.MakeCold(b.Entities.Resolve(rider));
            a.Step(default);
            b.Step(default);
            b.RestoreStableSnapshot(a.SaveStableSnapshot(),true);
            a.Entities.Reactivate(host.Id);
            a.Entities.Reactivate(rider.Id);
            b.Entities.Reactivate(host.Id);
            b.Entities.Reactivate(rider.Id);
            Assert.DoesNotThrow(() => a.Step(default));
            Assert.DoesNotThrow(() => b.Step(default));
            Assert.That(controlA.Current,Is.EqualTo(a.Entities.Resolve(host)));
            Assert.That(controlB.Current,Is.EqualTo(b.Entities.Resolve(host)));
            Assert.That(a.StateHashStable(),Is.EqualTo(b.StateHashStable()));
        }
        [Test]
        public void MixedPendingDueTypeQueuesCancellationAndOriginCountersSurviveRestore()
        {
            var sim = new Simulation(1,identityMode:IdentityMode.StableWorld);
            var a = new EventTypeId(100);
            var b = new EventTypeId(101);
            var first = new TaskKey(1,0,new StableId(8),0,1);
            var second = new TaskKey(1,1,new StableId(1),0,1);
            var future = new TaskKey(3,1,new StableId(1),1,1);
            var cancelled = new TaskKey(4,1,new StableId(1),2,1);
            sim.Scheduler.ScheduleStable(future,new StableSimEvent(a,tag:3));
            sim.Scheduler.ScheduleStable(second,new StableSimEvent(b,tag:2));
            sim.Scheduler.ScheduleStable(first,new StableSimEvent(a,tag:1));
            sim.Scheduler.ScheduleStable(cancelled,new StableSimEvent(a,tag:9));
            Assert.That(sim.Scheduler.CancelStable(cancelled),Is.True);
            sim.Step(default);
            ulong hash = sim.StateHashStable();
            sim.RestoreStableSnapshot(sim.SaveStableSnapshot(),true);
            Assert.That(sim.StateHashStable(),Is.EqualTo(hash));
            Assert.That(sim.Scheduler.TryDequeueStable(b,out StableScheduledTask two),Is.True);
            Assert.That(two.Key,Is.EqualTo(second));
            Assert.That(sim.Scheduler.TryDequeueStable(out StableScheduledTask one),Is.True);
            Assert.That(one.Key,Is.EqualTo(first));
            TaskKey next = sim.Scheduler.ScheduleFor(4,1,new StableId(1),2,new StableSimEvent(b,tag:4));
            Assert.That(next.Counter,Is.EqualTo(3));
            sim.Step(default);
            sim.Step(default);
            Assert.That(sim.Scheduler.TryDequeueStable(a,out StableScheduledTask three),Is.True);
            Assert.That(three.Key,Is.EqualTo(future));
            sim.Step(default);
            Assert.That(sim.Scheduler.TryDequeueStable(out StableScheduledTask four),Is.True);
            Assert.That(four.Key,Is.EqualTo(next));
            Assert.That(sim.Scheduler.TryDequeueStable(out _),Is.False);
        }
        [Test]
        public void PendingMotionAttachmentBranchesAndStaticFlagsRestoreBeforeArbitration()
        {
            var a = StableSnapshotTests.Scene(false);
            var b = StableSnapshotTests.Scene(true);
            var host = new EntityLink(StableEntityKey.Placed(1,0).Id);
            var rider = new EntityLink(StableEntityKey.Placed(1,1).Id);
            var leaf = new EntityLink(StableEntityKey.Placed(1,2).Id);
            var low = new StableRequestKey(1,1,new StableId(1),1,0,1);
            var high = new StableRequestKey(2,1,new StableId(1),1,0,1);
            a.Detach(rider,low);
            a.Attach(rider,host,SimVec3.Metres(0,2,0),high);
            b.Attach(rider,host,SimVec3.Metres(0,2,0),high);
            b.Detach(rider,low);
            a.Attach(leaf,rider,SimVec3.Metres(0,3,0),low);
            b.Attach(leaf,rider,SimVec3.Metres(0,3,0),low);
            a.RequestStableColliderStatic(host,true,high);
            b.RequestStableColliderStatic(host,true,high);
            a.RequestPosition(host,SimVec3.Metres(7,0,0),high);
            b.RequestPosition(host,SimVec3.Metres(7,0,0),high);
            Assert.That(a.StateHashStable(),Is.EqualTo(b.StateHashStable()));
            b.RestoreStableSnapshot(a.SaveStableSnapshot(),true);
            var hitsA = new SpatialHit[32];
            var hitsB = new SpatialHit[32];
            for (int tick = 0; tick < 300; tick++)
            {
                a.Step(default);
                b.Step(default);
                Assert.That(a.StateHashStable(),Is.EqualTo(b.StateHashStable()));
                var request = SpatialQueries.Sphere(default,SimFix.FromInt(1000));
                var x = a.SpatialQuery.AllCapped(request,512,hitsA);
                var y = b.SpatialQuery.AllCapped(request,512,hitsB);
                Assert.That(x.TotalHits,Is.EqualTo(y.TotalHits));
                for (int i = 0; i < x.Written; i++)
                {
                    Assert.That(a.Entities.StableOf(hitsA[i].Entity),Is.EqualTo(b.Entities.StableOf(hitsB[i].Entity)));
                    Assert.That(hitsA[i].Distance,Is.EqualTo(hitsB[i].Distance));
                }
                SimVec3 root = a.Entities.Read(a.Entities.Resolve<Position>(),a.Entities.Resolve(host)).Value;
                SimVec3 end = b.Entities.Read(b.Entities.Resolve<Position>(),b.Entities.Resolve(leaf)).Value;
                Assert.That(end,Is.EqualTo(root+SimVec3.Metres(0,5,0)));
            }
        }
        [Test]
        public void StableAttachmentCycleAndUnresolvedStrongLinkFailExplicitly()
        {
            var sim = StableSnapshotTests.Scene(false);
            var first = new EntityLink(StableEntityKey.Placed(1,0).Id);
            var second = new EntityLink(StableEntityKey.Placed(1,1).Id);
            var key = new StableRequestKey(1,1,new StableId(1),1,0,1);
            sim.Attach(first,second,default,key);
            sim.Attach(second,first,default,key);
            Assert.Throws<SimBoundaryException>(() => sim.Step(default));
            var other = StableSnapshotTests.Scene(false);
            var seats = other.Entities.Register<Seat>("seat",new SeatSerializer());
            EntityId carrier = other.Entities.Resolve(first);
            EntityId occupant = other.Entities.Resolve(second);
            other.Entities.Set(seats,carrier,new Seat(first,0,second));
            other.Entities.Destroy(occupant);
            Assert.That(other.Entities.Read(seats,carrier).OccupantLink.HasValue,Is.False);
            Assert.DoesNotThrow(() => other.StateHashStable());
            Assert.Throws<SimBoundaryException>(() => other.Entities.Set(seats,carrier,new Seat(first,0,second)));
        }
    }
}
