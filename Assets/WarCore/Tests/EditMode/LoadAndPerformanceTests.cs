using System;
using System.Diagnostics;
using NUnit.Framework;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    public sealed class LoadAndPerformanceTests
    {
        private static Simulation Create(int count, int capacity, out LoadSystem system)
        {
            var sim = new Simulation(123, eventBuffer: new SimEventBuffer(8, capacity, capacity));
            system = new LoadSystem(sim, count);
            sim.Register("load", system);
            return sim;
        }

        [Test]
        public void SaturationChangesOnlyOutputAndEachCompleteHashReplaysIndependently()
        {
            Simulation fullA = Create(64, 2, out LoadSystem fullSystem);
            Simulation fullB = Create(64, 2, out _);
            Simulation clearA = Create(64, 64, out LoadSystem clearSystem);
            Simulation clearB = Create(64, 64, out _);
            for (int tick = 1; tick <= 100; tick++)
            {
                fullA.Step(default);
                fullB.Step(default);
                clearA.Step(default);
                clearB.Step(default);
                Assert.That(Fixtures.Hash(fullA.Entities), Is.EqualTo(Fixtures.Hash(clearA.Entities)), "Components at " + tick);
                Assert.That(Fixtures.Hash(fullA.Rng), Is.EqualTo(Fixtures.Hash(clearA.Rng)), "RNG at " + tick);
                Assert.That(Fixtures.Hash(fullA.Scheduler), Is.EqualTo(Fixtures.Hash(clearA.Scheduler)), "Scheduler at " + tick);
                Assert.That(Fixtures.Authority(fullA), Is.EqualTo(Fixtures.Authority(clearA)), "All authoritative fields at " + tick);
                Assert.That(fullA.StateHash(), Is.EqualTo(fullB.StateHash()), "Overflow full hash replay at " + tick);
                Assert.That(clearA.StateHash(), Is.EqualTo(clearB.StateHash()), "Non-overflow full hash replay at " + tick);
            }

            Assert.That(fullA.Events.EventsDroppedTotal, Is.GreaterThan(0));
            Assert.That(clearA.Events.EventsDroppedTotal, Is.Zero);
            Assert.That(fullSystem.AppliedTasks, Is.EqualTo(392));
            Assert.That(clearSystem.AppliedTasks, Is.EqualTo(392));
            Assert.That(fullA.StateHash(), Is.Not.EqualTo(clearA.StateHash()));
        }

        [Test]
        public void TwoThousandEntitiesThousandTicksAllocateZeroAndDropNoEvents()
        {
            var windows = new AllocationWindow();
            do
            {
                Simulation sim = Create(2000, 64, out LoadSystem system);
                for (int i = 0; i < 16; i++)
                    sim.Step(default);
                var samples = new long[1000];
                var meter = new AllocationMeter();
                Stopwatch.GetTimestamp();
                windows.Begin(meter);
                for (int tick = 0; tick < samples.Length; tick++)
                {
                    long start = Stopwatch.GetTimestamp();
                    sim.Step(default);
                    samples[tick] = Stopwatch.GetTimestamp() - start;
                }

                AllocationSample allocated = windows.End();
                if (!windows.NeedsRetry)
                    AllocationAssertions.Zero(allocated);
                Assert.That(sim.Events.EventsDroppedTotal, Is.Zero);
                Assert.That(system.AppliedTasks, Is.EqualTo((1016 - 2) * 4));
                Assert.That(sim.Scheduler.PendingCount, Is.EqualTo(8));
                Assert.That(sim.Scheduler.DueCount, Is.Zero);
                TestContext.WriteLine("2000 entities / 1000 measured ticks: " + allocated.Describe() + ", EventsDropped=0");
                Report("2000 entities tick", samples);
            }
            while (windows.NeedsRetry);
        }

        [Test]
        public void ThousandAndHundredThousandTimersReportEmptyAndActiveTickCosts()
        {
            BenchmarkTimers(1000, false);
            BenchmarkTimers(100000, false);
            BenchmarkTimers(1000, true);
            BenchmarkTimers(100000, true);
        }

        private static void BenchmarkTimers(int pending, bool active)
        {
            var windows = new AllocationWindow();
            do
            {
                var scheduler = new SimScheduler(pending + 16);
                var template = new SimEvent(new EventTypeId(10), tag: 7);
                for (int i = 0; i < pending; i++)
                    scheduler.Schedule(1000000000 + i, template);
                scheduler.CompleteWarmup();
                for (int tick = 1; tick <= 100; tick++)
                {
                    if (active)
                        scheduler.Schedule(tick, template);
                    scheduler.Advance(tick);
                    if (active)
                        scheduler.TryDequeue(out _);
                }

                var samples = new long[20000];
                int batch = active ? 1 : 64;
                var meter = new AllocationMeter();
                windows.Begin(meter);
                int consumed = 0;
                for (int i = 0; i < samples.Length; i++)
                {
                    long start = Stopwatch.GetTimestamp();
                    for (int offset = 0; offset < batch; offset++)
                    {
                        long tick = 101L + i * batch + offset;
                        if (active)
                            scheduler.Schedule(tick, template);
                        scheduler.Advance(tick);
                        if (active && scheduler.TryDequeue(out _))
                            consumed++;
                    }

                    samples[i] = Stopwatch.GetTimestamp() - start;
                }

                AllocationSample allocated = windows.End();
                if (!windows.NeedsRetry)
                    AllocationAssertions.Zero(allocated);
                Assert.That(scheduler.PendingCount, Is.EqualTo(pending));
                Assert.That(consumed, Is.EqualTo(active ? samples.Length : 0));
                TestContext.WriteLine("Timers pending=" + pending + ", active=" + active + ", " + allocated.Describe());
                Report("Timers " + pending + (active ? " schedule+due+dequeue" : " empty tick (64-tick batch average)"), samples, batch);
            }
            while (windows.NeedsRetry);
        }

        private static void Report(string label, long[] samples, int batch = 1)
        {
            Array.Sort(samples);
            double scale = 1000000.0 / Stopwatch.Frequency / batch;
            TestContext.WriteLine(label + ": median=" + (samples[samples.Length / 2] * scale).ToString("F3", System.Globalization.CultureInfo.InvariantCulture) + " us, p95=" + (samples[(samples.Length * 95 - 1) / 100] * scale).ToString("F3", System.Globalization.CultureInfo.InvariantCulture) + " us");
        }

        [Test]
        public void FixedScriptHasKnownM0b1HashVector()
        {
            Simulation sim = Fixtures.Create(123);
            for (int tick = 1; tick <= 1000; tick++)
                sim.Step(Fixtures.Input(tick));
            // Empty scheduler queues make this endpoint insensitive to pending-task ordering.
            Assert.That(sim.Scheduler.PendingCount, Is.Zero);
            Assert.That(sim.Scheduler.DueCount, Is.Zero);
            TestContext.WriteLine("M0b1 known hash seed=123 ticks=1000: 0x" + sim.StateHash().ToString("X16"));
            TestContext.WriteLine("M0b1 known hash scheduler queues: pending=0; due=0.");
            Assert.That(sim.StateHash(), Is.EqualTo(0x8166D6848CDEB66CUL));
        }
    }
}
