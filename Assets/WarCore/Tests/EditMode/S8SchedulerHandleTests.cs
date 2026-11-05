using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    [Category("S8Layout")]
    public sealed class S8SchedulerHandleTests
    {
        private static byte[] Save(SimScheduler scheduler)
        {
            var writer = new SimStateWriter();
            scheduler.SaveScheduler(writer);
            return writer.ToArray();
        }

        [Test]
        public void RestoreInvalidatesLocalHandlesButPreservesStableIdsAndSnapshotEncoding()
        {
            var scheduler = new SimScheduler(4);
            ScheduledTaskHandle pending = scheduler.ScheduleWithHandle(3, new SimEvent(new EventTypeId(41), tag: 1));
            ScheduledTaskHandle due = scheduler.ScheduleWithHandle(1, new SimEvent(new EventTypeId(42), tag: 2));
            scheduler.Advance(1);
            byte[] snapshot = Save(scheduler);
            ulong hash = Fixtures.Hash(scheduler);
            scheduler.LoadScheduler(new SimStateReader(snapshot), 1);
            Assert.That(Save(scheduler), Is.EqualTo(snapshot));
            Assert.That(Fixtures.Hash(scheduler), Is.EqualTo(hash));
            Assert.That(scheduler.Cancel(pending), Is.False);
            Assert.That(scheduler.Cancel(due), Is.False);
            Assert.That(scheduler.TryGetHandle(1, out ScheduledTaskHandle restored), Is.True);
            Assert.That(scheduler.Cancel(restored), Is.True);
            Assert.That(scheduler.Cancel(2), Is.True);
            Assert.That(scheduler.PendingCount + scheduler.DueCount, Is.Zero);
        }

        [Test]
        public void ShrinkingThenGrowingAfterRestoreCannotResurrectAnOldHandle()
        {
            var scheduler = new SimScheduler(1);
            byte[] small = Save(scheduler);
            scheduler.Reserve(4);
            var old = new ScheduledTaskHandle[4];
            for (int i = 0; i < old.Length; i++)
                old[i] = scheduler.ScheduleWithHandle(1, new SimEvent(new EventTypeId(41)));
            scheduler.LoadScheduler(new SimStateReader(small), 0);
            scheduler.Reserve(4);
            for (int i = 0; i < old.Length; i++)
                scheduler.ScheduleWithHandle(1, new SimEvent(new EventTypeId(41)));
            foreach (ScheduledTaskHandle handle in old)
                Assert.That(scheduler.Cancel(handle), Is.False);
            Assert.That(scheduler.PendingCount, Is.EqualTo(4));
        }

        [Test]
        public void GenerationAndCapacityBoundariesFailExplicitlyBeforeMutation()
        {
            var scheduler = new SimScheduler(1);
            var field = typeof(SimScheduler).GetField("taskGenerations", BindingFlags.NonPublic | BindingFlags.Instance);
            ((ulong[])field.GetValue(scheduler))[0] = ulong.MaxValue;
            byte[] before = Save(scheduler);
            Assert.Throws<SimBoundaryException>(() => scheduler.ScheduleWithHandle(1, new SimEvent(new EventTypeId(41))));
            Assert.That(Save(scheduler), Is.EqualTo(before));
            Assert.Throws<SimBoundaryException>(() => scheduler.Reserve((1 << 28) + 1));
            Assert.That(Save(scheduler), Is.EqualTo(before));
            Assert.Throws<SimBoundaryException>(() => new SimScheduler((1 << 28) + 1));
            Assert.That(scheduler.Cancel(default(ScheduledTaskHandle)), Is.False);
            Assert.That(scheduler.Cancel(new ScheduledTaskHandle(-1, 1)), Is.False);
            Assert.That(scheduler.Cancel(new ScheduledTaskHandle(int.MaxValue, 1)), Is.False);
        }

        [Test]
        public void InvalidTemplateDoesNotConsumeCapacityOrTaskIdentity()
        {
            var scheduler = new SimScheduler(1);
            byte[] before = Save(scheduler);
            Assert.Throws<ArgumentOutOfRangeException>(() => scheduler.ScheduleWithHandle(1, default));
            Assert.That(Save(scheduler), Is.EqualTo(before));
            Assert.That(scheduler.Schedule(1, new SimEvent(new EventTypeId(41))), Is.EqualTo(1));
        }

        [Test]
        public void GenerationCheckRejectsAForgedGenerationForALiveSlot()
        {
            var scheduler = new SimScheduler(1);
            ScheduledTaskHandle live = scheduler.ScheduleWithHandle(1, new SimEvent(new EventTypeId(41)));
            var wrong = new ScheduledTaskHandle(live.Slot, live.Generation + 1);
            Assert.That(scheduler.Cancel(wrong), Is.False);
            Assert.That(scheduler.Cancel(live), Is.True, "The same live slot with its issued generation is the positive control.");
        }

        [Test]
        public void LegacyIndexSurvivesCollisionDeletionAndUnboundedSlotReuse()
        {
            const int capacity = 47;
            var scheduler = new SimScheduler(capacity);
            var expected = new SortedDictionary<long, int>();
            var random = new Random(0x583008);
            for (int step = 0; step < 20000; step++)
            {
                if (expected.Count == 0 || (expected.Count < capacity && random.Next(2) == 0))
                {
                    long id = scheduler.Schedule(1, new SimEvent(new EventTypeId(41), tag: step));
                    expected.Add(id, step);
                }
                else
                {
                    long id = expected.Keys.ElementAt(random.Next(expected.Count));
                    Assert.That(scheduler.TryGetHandle(id, out ScheduledTaskHandle handle), Is.True);
                    Assert.That(step % 2 == 0 ? scheduler.Cancel(id) : scheduler.Cancel(handle), Is.True);
                    Assert.That(scheduler.TryGetHandle(id, out _), Is.False);
                    expected.Remove(id);
                }
                foreach (KeyValuePair<long, int> pair in expected)
                {
                    Assert.That(scheduler.TryGetPending(pair.Key, out ScheduledTask task), Is.True);
                    Assert.That(task.Event.Tag, Is.EqualTo(pair.Value));
                }
            }
            scheduler.Advance(1);
            foreach (KeyValuePair<long, int> pair in expected)
            {
                Assert.That(scheduler.TryDequeue(out ScheduledTask task), Is.True);
                Assert.That(task.Id, Is.EqualTo(pair.Key));
                Assert.That(task.Event.Tag, Is.EqualTo(pair.Value));
            }
            Assert.That(scheduler.TryDequeue(out _), Is.False);
        }

        [Test]
        public void HandleLifecycleAndTypedQueuesAreStrictlyZeroAfterWarmup()
        {
            var scheduler = new SimScheduler(1000);
            scheduler.CompleteWarmup();
            var handles = new ScheduledTaskHandle[1000];
            for (int tick = 1; tick <= 32; tick++)
                Cycle(scheduler, handles, tick);
            var meter = new AllocationMeter();
            if (meter.Basis != AllocationBasis.Direct)
                Assert.Inconclusive("Strict zero needs a working direct positive control.");
            Assert.That(meter.DirectControlDelta.Bytes, Is.GreaterThanOrEqualTo(32768));
            AllocationSample before = meter.Read();
            bool valid = true;
            for (int tick = 33; tick <= 332; tick++)
                valid &= Cycle(scheduler, handles, tick);
            AllocationSample sample = meter.Difference(before);
            Assert.That(valid, Is.True);
            AllocationAssertions.Zero(sample);
        }

        private static bool Cycle(SimScheduler scheduler, ScheduledTaskHandle[] handles, int tick)
        {
            for (int i = 0; i < handles.Length; i++)
                handles[i] = scheduler.ScheduleWithHandle(tick, new SimEvent(new EventTypeId(i % 2 + 41), tag: i));
            bool valid = true;
            for (int i = 0; i < handles.Length; i += 4)
                valid &= scheduler.Cancel(handles[i]);
            scheduler.Advance(tick);
            for (int i = 2; i < handles.Length; i += 4)
                valid &= scheduler.Cancel(handles[i]);
            for (int i = 1; i < handles.Length; i += 2)
                valid &= scheduler.TryDequeue(new EventTypeId(42), out ScheduledTask task) && task.Event.Tag == i;
            return valid && scheduler.PendingCount == 0 && scheduler.DueCount == 0;
        }
    }
}
