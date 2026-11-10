using System.Reflection;
using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    [Category("S8Layout")]
    public sealed class Mt059SchedulerSlotsTests
    {
        [TestCase(1, 2)]
        [TestCase(4, 8)]
        [TestCase(3, 8)]
        public void ConstructorAllocatesTheSmallestHalfFullIdTable(int capacity, int expectedLength)
        {
            var scheduler = new SimScheduler(capacity);
            var field = typeof(SimScheduler).GetField("idSlots", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(((int[])field.GetValue(scheduler)).Length, Is.EqualTo(expectedLength));
        }

        [Test]
        public void NewlyScheduledTaskRemainsPendingAfterPartialRestoreFailure()
        {
            var item = new SimEvent(new EventTypeId(41));
            var saved = new SimScheduler(4);
            for (int i = 0; i < 3; i++)
                saved.Cancel(saved.Schedule(1, item));
            for (int i = 0; i < 4; i++)
                saved.Schedule(1, item);
            saved.Advance(1);
            Assert.That(saved.TryDequeue(out ScheduledTask first), Is.True);
            Assert.That(first.Id, Is.EqualTo(4));
            var writer = new SimStateWriter();
            saved.SaveScheduler(writer);

            var scheduler = new SimScheduler(4);
            for (int i = 0; i < 4; i++)
                scheduler.Cancel(scheduler.Schedule(1, item));
            var field = typeof(SimScheduler).GetField("taskGenerations", BindingFlags.NonPublic | BindingFlags.Instance);
            ((ulong[])field.GetValue(scheduler))[3] = ulong.MaxValue;
            Assert.Throws<SimBoundaryException>(() => scheduler.LoadScheduler(new SimStateReader(writer.ToArray()), 1));
            Assert.That(scheduler.DueCount, Is.EqualTo(2));
            Assert.That(scheduler.Schedule(1, item), Is.EqualTo(5));
            Assert.That(scheduler.Schedule(1, new SimEvent(new EventTypeId(41), tag: 61)), Is.EqualTo(6));
            Assert.That(scheduler.TryGetPending(6, out ScheduledTask pending), Is.True);
            Assert.That(pending.Id, Is.EqualTo(6));
            Assert.That(pending.Event.Tag, Is.EqualTo(61));
        }

        [Test]
        public void FailedRestoreDoesNotExposeTheUncommittedNextId()
        {
            var saved = new SimScheduler(2);
            saved.Schedule(1, new SimEvent(new EventTypeId(41)));
            saved.Schedule(1, new SimEvent(new EventTypeId(42)));
            saved.Advance(1);
            var writer = new SimStateWriter();
            saved.SaveScheduler(writer);

            var scheduler = new SimScheduler(2);
            var field = typeof(SimScheduler).GetField("taskGenerations", BindingFlags.NonPublic | BindingFlags.Instance);
            ((ulong[])field.GetValue(scheduler))[1] = ulong.MaxValue;
            Assert.Throws<SimBoundaryException>(() => scheduler.LoadScheduler(new SimStateReader(writer.ToArray()), 1));
            Assert.That(scheduler.DueCount, Is.EqualTo(1));
            Assert.That(scheduler.TryGetHandle(1, out _), Is.False);
        }
    }
}
