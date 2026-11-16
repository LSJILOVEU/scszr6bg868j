using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    [TestFixture, Category("StableIdentity")]
    public sealed class Mt101StableTasksTests
    {
        [Test]
        public void TaskKeyRejectsZeroDueTick()
        {
            Assert.Throws<SimBoundaryException>(() => new TaskKey(0, 0, new StableId(4), 0, 1));
        }

        [Test]
        public void StableTaskAtCurrentTickThrowsBoundaryException()
        {
            var scheduler = new SimScheduler(4, IdentityMode.StableWorld);
            scheduler.Advance(1);
            var key = new TaskKey(1, 0, new StableId(4), 0, 1);
            var item = new StableSimEvent(new EventTypeId(100));
            Assert.Throws<SimBoundaryException>(() => scheduler.ScheduleStable(key,item));
        }

        [Test]
        public void FirstSlotDuplicateDoesNotConsumeSlots()
        {
            var scheduler = new SimScheduler(4, IdentityMode.StableWorld);
            var item = new StableSimEvent(new EventTypeId(100));
            var key = new TaskKey(1, 0, new StableId(4), 0, 1);
            var next = new TaskKey(1, 0, new StableId(4), 1, 1);
            Assert.That(scheduler.ScheduleStable(key,item).Slot, Is.EqualTo(0));
            Assert.Throws<SimBoundaryException>(() => scheduler.ScheduleStable(key,item));
            Assert.That(scheduler.PendingCount, Is.EqualTo(1));
            Assert.That(scheduler.ScheduleStable(next,item).Slot, Is.EqualTo(1));
            Assert.That(scheduler.PendingCount, Is.EqualTo(2));
        }
    }
}
