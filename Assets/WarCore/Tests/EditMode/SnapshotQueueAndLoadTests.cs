using System;
using System.Diagnostics;
using NUnit.Framework;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    public sealed class SnapshotQueueAndLoadTests
    {
        private static Simulation WrappedQueues()
        {
            var sim = new Simulation(1, config: new SimConfig(3, 8, 8, 1, 8));
            // Exercise a retained queue independently of the optional missing-consumer diagnostic.
            sim.Scheduler.DiagnoseUnconsumedTasks = false;
            for (int i = 1; i <= 4; i++)
                sim.Scheduler.Schedule(1, SnapshotFixtures.Payload(i));
            sim.Step(default);
            for (int i = 1; i <= 3; i++)
            {
                Assert.That(sim.Scheduler.TryDequeue(out ScheduledTask task), Is.True);
                Assert.That(task.Id, Is.EqualTo(i));
            }

            for (int i = 5; i <= 8; i++)
                sim.Scheduler.Schedule(2, SnapshotFixtures.Payload(i));
            sim.Step(default);
            sim.Scheduler.Schedule(3, SnapshotFixtures.Payload(9));
            sim.Scheduler.Schedule(3, SnapshotFixtures.Payload(10));
            sim.Step(default);
            Assert.That(sim.Scheduler.Schedule(8, SnapshotFixtures.Payload(11)), Is.EqualTo(11));
            return sim;
        }

        [Test]
        public void WrappedDueQueueAndFullCapacityRestoreWithoutReschedulingOrRepublishing()
        {
            Simulation baseline = WrappedQueues();
            Simulation restored = WrappedQueues();
            byte[] snapshot = restored.SaveSnapshot();
            Assert.That(restored.Scheduler.DueCount, Is.EqualTo(7));
            Assert.That(restored.Scheduler.PendingCount, Is.EqualTo(1));
            restored.Scheduler.Cancel(11);
            restored.Scheduler.Schedule(4, SnapshotFixtures.Payload(99));
            restored.Step(default);
            restored.Scheduler.TryDequeue(out _);
            restored.RestoreSnapshot(snapshot);
            Assert.That(restored.SaveSnapshot(), Is.EqualTo(snapshot));
            foreach (Simulation sim in new[] { baseline, restored })
            {
                Assert.That(sim.Scheduler.Cancel(6), Is.True);
                Assert.That(sim.Scheduler.Schedule(4, SnapshotFixtures.Payload(12)), Is.EqualTo(12));
            }

            long[] expectedIds = { 4, 5, 7, 8, 9, 10, 12, 11 };
            int consumed = 0;
            for (int tick = 4; tick <= 12; tick++)
            {
                baseline.Step(default);
                restored.Step(default);
                bool found = baseline.Scheduler.TryDequeue(out ScheduledTask expected);
                Assert.That(restored.Scheduler.TryDequeue(out ScheduledTask actual), Is.EqualTo(found));
                if (found)
                {
                    Assert.That(actual.Id, Is.EqualTo(expectedIds[consumed++]));
                    Assert.That(actual.Id, Is.EqualTo(expected.Id));
                    Assert.That(actual.Event, Is.EqualTo(expected.Event));
                }

                Assert.That(restored.StateHash(), Is.EqualTo(baseline.StateHash()));
            }

            Assert.That(consumed, Is.EqualTo(expectedIds.Length));
        }

        private static Simulation Load(out LoadSystem system)
        {
            var sim = new Simulation(123, config: new SimConfig(8, 64, 64, 2000));
            system = new LoadSystem(sim, 2000);
            sim.Register("load", system);
            return sim;
        }

        [Test]
        public void ExistingTwoThousandEntityExampleRestoresGridAndContinuesEveryTickWithoutAllocating()
        {
            var windows = new AllocationWindow();
            do
            {
                Simulation baseline = Load(out _);
                Simulation restored = Load(out LoadSystem system);
                for (int tick = 0; tick < 16; tick++)
                {
                    baseline.Step(default);
                    restored.Step(default);
                }

                byte[] snapshot = restored.SaveSnapshot();
                for (int tick = 0; tick < 7; tick++)
                    restored.Step(default);
                restored.RestoreSnapshot(snapshot);
                for (int tick = 17; tick <= 48; tick++)
                {
                    baseline.Step(default);
                    restored.Step(default);
                    Assert.That(restored.StateHash(), Is.EqualTo(baseline.StateHash()), "Restored load tick " + tick);
                }

                restored.RestoreSnapshot(snapshot);
                var samples = new long[1000];
                var meter = new AllocationMeter();
                Stopwatch.GetTimestamp();
                windows.Begin(meter);
                for (int tick = 0; tick < samples.Length; tick++)
                {
                    long start = Stopwatch.GetTimestamp();
                    restored.Step(default);
                    samples[tick] = Stopwatch.GetTimestamp() - start;
                }

                AllocationSample bytes = windows.End();
                if (!windows.NeedsRetry)
                    AllocationAssertions.Zero(bytes);
                Assert.That(restored.Events.EventsDroppedTotal, Is.Zero);
                Assert.That(system.AppliedTasks, Is.EqualTo((1016 - 2) * 4));
                Assert.That(restored.Scheduler.PendingCount, Is.EqualTo(8));
                Assert.That(restored.Scheduler.DueCount, Is.Zero);
                Array.Sort(samples);
                double scale = 1000000.0 / Stopwatch.Frequency;
                TestContext.WriteLine("2000 original example after restore / 1000 ticks: {0}, EventsDropped=0; median={1:F3} us, p95={2:F3} us; 32 continuation hashes matched.",
                    bytes.Describe(), samples[500] * scale, samples[949] * scale);
            }
            while (windows.NeedsRetry);
        }
    }
}
