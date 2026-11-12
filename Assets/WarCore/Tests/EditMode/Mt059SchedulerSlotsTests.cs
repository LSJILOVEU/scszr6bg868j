using System;
using System.Reflection;
using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    [Category("S8Layout")]
    public sealed class Mt059SchedulerSlotsTests
    {
        [Test]
        public void PowerOfTwoConstructorKeepsAllocationGrowthLinear()
        {
            var meter = new AllocationMeter();
            if (meter.Basis != AllocationBasis.Direct)
                Assert.Inconclusive("Constructor allocation requires the direct positive control.");

            // Equal capacity steps share the same dictionary tier and preserve array alignment.
            ConstructorBytes(meter, 448);
            ConstructorBytes(meter, 480);
            ConstructorBytes(meter, 512);
            long first = ConstructorBytes(meter, 448);
            long middle = ConstructorBytes(meter, 480);
            long last = ConstructorBytes(meter, 512);
            Assert.That(last - 2 * middle + first, Is.Zero);
        }

        private static long ConstructorBytes(AllocationMeter meter, int capacity)
        {
            AllocationSample before = meter.Read();
            var scheduler = new SimScheduler(capacity);
            long bytes = meter.Difference(before).Bytes;
            GC.KeepAlive(scheduler);
            return bytes;
        }

        [Test]
        public void FailedRestoreDoesNotExposeUncommittedTaskId()
        {
            var source = new SimScheduler(2);
            long first = source.Schedule(1, new SimEvent(new EventTypeId(41), tag: 11));
            source.Schedule(1, new SimEvent(new EventTypeId(42), tag: 22));
            source.Advance(1);
            var writer = new SimStateWriter();
            source.SaveScheduler(writer);
            byte[] snapshot = writer.ToArray();

            var scheduler = new SimScheduler(2);
            // Exhaust the second slot so restore inserts the first task before failing.
            var field = typeof(SimScheduler).GetField("taskGenerations", BindingFlags.NonPublic | BindingFlags.Instance);
            ((ulong[])field.GetValue(scheduler))[1] = ulong.MaxValue;
            Assert.Throws<SimBoundaryException>(() => scheduler.LoadScheduler(new SimStateReader(snapshot), 1));
            Assert.That(scheduler.DueCount, Is.EqualTo(1));
            Assert.That(scheduler.TryGetHandle(first, out ScheduledTaskHandle handle), Is.False);
            Assert.That(handle, Is.EqualTo(default(ScheduledTaskHandle)));
            Assert.That(scheduler.Cancel(first), Is.False);
        }
    }
}
