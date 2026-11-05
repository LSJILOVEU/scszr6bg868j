using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    [Category("S32")]
    public sealed class SchedulerDispatchRegressionTests
    {
        private static bool WalksQueue(MethodBase method) => HotPathAllocationRules.Read(method)
            .Any(instruction => instruction.Targets.Any(target => target < instruction.Offset));

        [Test]
        public void TypedDispatchAndCancellationDoNotWalkTheDueQueue()
        {
            Assert.That(WalksQueue(typeof(SchedulerDispatchRegressionTests).GetMethod(nameof(LinearControl), BindingFlags.Static | BindingFlags.NonPublic)), Is.True);
            MethodInfo dispatch = typeof(SimScheduler).GetMethod("TryDequeue", new[] { typeof(EventTypeId), typeof(ScheduledTask).MakeByRefType() });
            Assert.That(WalksQueue(dispatch), Is.False, "Typed dispatch must use an index.");
            Assert.That(WalksQueue(typeof(SimScheduler).GetMethod("Cancel", new[] { typeof(long) })), Is.False, "Due cancellation must use an index.");
            Assert.That(WalksQueue(typeof(SimScheduler).GetMethod("Cancel", new[] { typeof(ScheduledTaskHandle) })), Is.False, "Handle cancellation must not scan tasks.");
        }

        private static int LinearControl(int[] values)
        {
            int total = 0;
            for (int i = 0; i < values.Length; i++)
                total += values[i];
            return total;
        }

        [Test]
        public void TickEndReportsAnUnconsumedTypeOnlyAfterTheOneTickGrace()
        {
            var sim = new Simulation(1);
            sim.Scheduler.Schedule(1, new SimEvent(new EventTypeId(417)));
            sim.Step(default);
            sim.Step(default);
            var error = Assert.Throws<InvalidOperationException>(() => sim.Step(default));
            Assert.That(error.Message, Does.Contain("417"));
        }

        [Test]
        public void MixedTypesCancellationAndSlotReusePreserveFifoAndAllocateZero()
        {
            const int count = 4096;
            var scheduler = new SimScheduler(count);
            var ids = new long[count];
            var first = new EventTypeId(41);
            var second = new EventTypeId(int.MaxValue);
            for (int i = 0; i < count; i++)
                ids[i] = scheduler.Schedule(1, new SimEvent(i % 2 == 0 ? first : second, tag: i));
            scheduler.CompleteWarmup();
            scheduler.Advance(1);
            for (int i = 0; i < count; i += 4)
                Assert.That(scheduler.Cancel(ids[i]), Is.True);
            for (int i = 1; i < count; i += 2)
            {
                Assert.That(scheduler.TryDequeue(second, out ScheduledTask task), Is.True);
                Assert.That(task.Event.Tag, Is.EqualTo(i));
            }
            for (int i = 2; i < count; i += 4)
            {
                Assert.That(scheduler.TryDequeue(out ScheduledTask task), Is.True);
                Assert.That(task.Event.Tag, Is.EqualTo(i));
            }
            Assert.That(scheduler.DueCount, Is.Zero);
            var meter = new AllocationMeter();
            if (meter.Basis != AllocationBasis.Direct)
                Assert.Inconclusive("Strict zero requires the direct positive control.");
            Assert.That(meter.DirectControlDelta.Bytes, Is.GreaterThanOrEqualTo(32768));
            AllocationSample before = meter.Read();
            bool valid = true;
            for (int tick = 2; tick <= 32; tick++)
            {
                for (int i = 0; i < count; i++)
                    ids[i] = scheduler.Schedule(tick, new SimEvent(i % 2 == 0 ? first : second, tag: i));
                scheduler.Advance(tick);
                for (int i = 0; i < count; i += 2)
                {
                    valid &= scheduler.Cancel(ids[i]);
                    valid &= scheduler.TryDequeue(second, out ScheduledTask task) && task.Event.Tag == i + 1;
                }
            }
            AllocationSample sample = meter.Difference(before);
            Assert.That(valid, Is.True);
            AllocationAssertions.Zero(sample);
        }
        private static byte[] TaskBytes(ScheduledTask task)
        {
            var writer = new SimStateWriter();
            writer.Int64(task.Id);
            SnapshotValues.Event(writer, task.Event);
            return writer.ToArray();
        }

        private static int FindUnique(byte[] bytes, byte[] payload)
        {
            int found = -1;
            for (int i = 0; i <= bytes.Length - payload.Length; i++)
            {
                bool same = true;
                for (int j = 0; j < payload.Length; j++)
                    same &= bytes[i + j] == payload[j];
                if (!same)
                    continue;
                Assert.That(found, Is.EqualTo(-1));
                found = i;
            }
            Assert.That(found, Is.GreaterThanOrEqualTo(0));
            return found;
        }

        [Test]
        public void RestoreRejectsOutOfOrderDueTasksBeforeTheyCanHideAnOverdueType()
        {
            var sim = new Simulation(1);
            long a = sim.Scheduler.Schedule(1, new SimEvent(new EventTypeId(41), tag: 12345));
            long b = sim.Scheduler.Schedule(2, new SimEvent(new EventTypeId(42), tag: 23456));
            sim.Scheduler.TryGetPending(a, out ScheduledTask first);
            sim.Scheduler.TryGetPending(b, out ScheduledTask second);
            sim.Step(default);
            sim.Step(default);
            byte[] snapshot = sim.SaveSnapshot();
            byte[] left = TaskBytes(first);
            byte[] right = TaskBytes(second);
            int leftOffset = FindUnique(snapshot, left);
            int rightOffset = FindUnique(snapshot, right);
            Array.Copy(right, 0, snapshot, leftOffset, right.Length);
            Array.Copy(left, 0, snapshot, rightOffset, left.Length);
            Assert.Throws<SimSnapshotException>(() => sim.RestoreSnapshot(snapshot));
        }
    }
}
