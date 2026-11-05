using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    [TestFixture, Category("StableIdentity")]
    public sealed class StableSchedulerTests
    {
        [Test]
        public void StableTasksUseLogicalKeysInGlobalAndTypeQueues()
        {
            var scheduler = new SimScheduler(32, IdentityMode.StableWorld);
            var a = new EventTypeId(100);
            var b = new EventTypeId(101);
            var one = new TaskKey(1, 0, new StableId(2), 0, 7);
            var two = new TaskKey(1, 0, new StableId(0x8000000000000000), 0, 7);
            var three = new TaskKey(1, 1, new StableId(1), 0, 7);
            scheduler.ScheduleStable(three, new StableSimEvent(b, tag:3));
            scheduler.ScheduleStable(two, new StableSimEvent(a, tag:2));
            scheduler.ScheduleStable(one, new StableSimEvent(a, tag:1));
            Assert.Throws<SimBoundaryException>(() => scheduler.Schedule(2, new SimEvent(a)));
            scheduler.Advance(1);
            Assert.That(scheduler.TryDequeueStable(a, out StableScheduledTask first), Is.True);
            Assert.That(first.Key, Is.EqualTo(one));
            Assert.That(scheduler.TryDequeueStable(out StableScheduledTask second), Is.True);
            Assert.That(second.Key, Is.EqualTo(two));
            Assert.That(scheduler.TryDequeueStable(out StableScheduledTask third), Is.True);
            Assert.That(third.Key, Is.EqualTo(three));
            int nonCommutative = (first.Event.Tag*10+second.Event.Tag)*10+third.Event.Tag;
            Assert.That(nonCommutative,Is.EqualTo(123));
        }

        [Test]
        public void DuplicateKeysAndCancellationDoNotChangeRemainingOrder()
        {
            var scheduler = new SimScheduler(8, IdentityMode.StableWorld);
            var item = new StableSimEvent(new EventTypeId(100));
            var key = new TaskKey(1, 0, new StableId(4), 0, 1);
            var later = new TaskKey(1, 0, new StableId(4), 1, 1);
            scheduler.ScheduleStable(later,item);
            scheduler.ScheduleStable(key,item);
            Assert.Throws<SimBoundaryException>(() => scheduler.ScheduleStable(key,item));
            Assert.That(scheduler.CancelStable(key), Is.True);
            scheduler.Advance(1);
            Assert.That(scheduler.TryDequeueStable(out StableScheduledTask task), Is.True);
            Assert.That(task.Key, Is.EqualTo(later));
        }
    }
}
