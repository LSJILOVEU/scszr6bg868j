using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class Mt055QueueSnapshotsTests
    {
        private static SimStateWriter SchedulerHeader(int capacity, int pending)
        {
            var writer = new SimStateWriter();
            writer.Int32(capacity);
            writer.Bool(false);
            writer.Int64(2);
            writer.Int64(1);
            writer.Int32(pending);
            return writer;
        }

        private static byte[] EventWindow(long nextSequence, params long[] sequences)
        {
            var writer = new SimStateWriter();
            writer.Int32(1); // Retained ticks.
            writer.Int32(2); // Hard limit.
            writer.Int64(1); // Saved tick.
            writer.Int64(nextSequence);
            writer.Int64(0);
            writer.Int64(0);
            writer.Bool(false);
            writer.Int32(3); // Slot capacity includes the overflow event.
            writer.Int64(1); // Slot tick.
            writer.Int32(sequences.Length);
            for (int i = 0; i < sequences.Length; i++)
                SnapshotValues.Event(writer, SnapshotFixtures.Payload(i + 1).At(1, sequences[i]));
            return writer.ToArray();
        }

        [Test]
        public void RejectsTaskIdSharedByPendingAndDueTasks()
        {
            var scheduler = new SimScheduler(2);
            SimStateWriter writer = SchedulerHeader(2, 1);
            writer.Int64(1);
            SnapshotValues.Event(writer, SnapshotFixtures.Payload(1).At(2));
            writer.Int32(0);
            writer.Int32(1);
            writer.Int64(1);
            SnapshotValues.Event(writer, SnapshotFixtures.Payload(2).At(1));
            Assert.Throws<SimSnapshotException>(() => scheduler.LoadScheduler(new SimStateReader(writer.ToArray()), 1));
        }

        [Test]
        public void RejectsZeroSchedulerCapacityAsInvalidHeader()
        {
            var scheduler = new SimScheduler(1);
            SimStateWriter writer = SchedulerHeader(0, 0);
            writer.Int32(0);
            writer.Int32(0);
            var error = Assert.Throws<SimSnapshotException>(() => scheduler.LoadScheduler(new SimStateReader(writer.ToArray()), 1));
            Assert.That(error.Message, Is.EqualTo("Invalid scheduler snapshot header."));
        }

        [Test]
        public void RejectsPendingTaskAtSavedTick()
        {
            var scheduler = new SimScheduler(1);
            SimStateWriter writer = SchedulerHeader(1, 1);
            writer.Int64(1);
            SnapshotValues.Event(writer, SnapshotFixtures.Payload(1).At(1));
            writer.Int32(0);
            writer.Int32(0);
            Assert.Throws<SimSnapshotException>(() => scheduler.LoadScheduler(new SimStateReader(writer.ToArray()), 1));
        }

        [Test]
        public void RejectsEmptyDueHeadEqualToCapacity()
        {
            var scheduler = new SimScheduler(1);
            SimStateWriter writer = SchedulerHeader(1, 0);
            writer.Int32(1);
            writer.Int32(0);
            Assert.Throws<SimSnapshotException>(() => scheduler.LoadScheduler(new SimStateReader(writer.ToArray()), 1));
        }

        [Test]
        public void RejectsZeroNextSequenceInEmptyEventWindow()
        {
            var events = new SimEventBuffer(1, 2, 2);
            byte[] snapshot = EventWindow(0);
            Assert.Throws<SimSnapshotException>(() => events.LoadEvents(new SimStateReader(snapshot), 1));
        }

        [TestCase(0, 1)]
        [TestCase(1, 1)]
        public void RejectsNonIncreasingRetainedSequences(long first, long second)
        {
            var events = new SimEventBuffer(1, 2, 2);
            byte[] snapshot = EventWindow(3, first, second);
            Assert.Throws<SimSnapshotException>(() => events.LoadEvents(new SimStateReader(snapshot), 1));
        }
    }
}
