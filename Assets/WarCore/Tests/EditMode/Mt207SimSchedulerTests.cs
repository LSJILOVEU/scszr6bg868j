using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    [TestFixture, Category("StableIdentity")]
    public sealed class Mt207SimSchedulerTests
    {
        [Test]
        public void ReserveMakesEveryStableTaskSlotUsable()
        {
            var scheduler = new SimScheduler(1, IdentityMode.StableWorld);
            var item = new StableSimEvent(new EventTypeId(100));
            var first = new TaskKey(1, 0, new StableId(4), 0, 1);
            var second = new TaskKey(1, 0, new StableId(4), 1, 1);
            scheduler.ScheduleStable(first, item);
            scheduler.Reserve(2);

            Assert.That(scheduler.Capacity, Is.EqualTo(2));
            Assert.DoesNotThrow(() => scheduler.ScheduleStable(second, item));
            Assert.That(scheduler.PendingCount, Is.EqualTo(2));
            scheduler.Advance(1);
            Assert.That(scheduler.TryDequeueStable(out StableScheduledTask one), Is.True);
            Assert.That(one.Key, Is.EqualTo(first));
            Assert.That(scheduler.TryDequeueStable(out StableScheduledTask two), Is.True);
            Assert.That(two.Key, Is.EqualTo(second));
            Assert.That(scheduler.TryDequeueStable(out _), Is.False);
        }

        [Test]
        public void ReservePreservesEachPendingStableTaskLookup()
        {
            var scheduler = new SimScheduler(2, IdentityMode.StableWorld);
            var keys = new[]
            {
                new TaskKey(1, 0, new StableId(4), 0, 1),
                new TaskKey(1, 0, new StableId(4), 1, 1)
            };
            foreach (TaskKey key in keys)
                scheduler.ScheduleStable(key, new StableSimEvent(new EventTypeId(100), tag: (int)key.Counter));
            scheduler.Reserve(4);

            foreach (TaskKey key in keys)
            {
                Assert.That(scheduler.TryGetPendingStable(key, out StableScheduledTask task), Is.True);
                Assert.That(task.Key, Is.EqualTo(key));
                Assert.That(task.Event.Tag, Is.EqualTo((int)key.Counter));
            }
        }
    }
}
