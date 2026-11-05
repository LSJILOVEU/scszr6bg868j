using System;
using NUnit.Framework;

namespace WarCore.Tests
{
    public sealed class AllocationWindowTests
    {
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void CleanWindowRetriesWholeWorkAndReportsEveryAttempt(int clean)
        {
            var samples = new AllocationSample[clean];
            for (int i = 0; i < clean - 1; i++)
                samples[i] = new AllocationSample(AllocationBasis.Indirect, gen0: 1, gen1: 1, gen2: 1,
                    totalMemory: i == 0 ? -6029312 : 0);
            samples[clean - 1] = new AllocationSample(AllocationBasis.Indirect);
            var meter = new ScriptedMeter(samples);
            int stabilized = 0;
            var windows = new AllocationWindow(() => stabilized++);
            int ticks = 0;
            do
            {
                windows.Begin(meter);
                Assert.That(stabilized, Is.EqualTo(windows.Count + 1));
                for (int tick = 0; tick < 300; tick++)
                    ticks++;
                windows.End();
            }
            while (windows.NeedsRetry);
            AllocationAssertions.Zero(windows.Result);
            Assert.That(ticks, Is.EqualTo(300 * clean));
            Assert.That(stabilized, Is.EqualTo(clean));
            Assert.That(windows.Result.Label, Is.EqualTo("indirect(CollectionCount+TotalMemory; clean window " + clean + "/3)"));
            for (int i = 1; i <= clean; i++)
                Assert.That(windows.Result.Describe(), Does.Contain("window " + i + "/3: CollectionCount delta"));
            Assert.That(windows.Result.CsvValues().Split(',')[1], Is.Empty);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void AllContaminatedWindowsAreInconclusiveAndBounded(int generation)
        {
            var dirty = new AllocationSample(AllocationBasis.Indirect,
                gen0: generation == 0 ? 1 : 0, gen1: generation == 1 ? 1 : 0,
                gen2: generation == 2 ? 1 : 0, totalMemory: -6029312);
            var meter = new ScriptedMeter(dirty, dirty, dirty);
            int stabilized = 0;
            var windows = new AllocationWindow(() => stabilized++);
            do
            {
                windows.Begin(meter);
                windows.End();
            }
            while (windows.NeedsRetry);
            Assert.That(windows.Count, Is.EqualTo(3));
            Assert.That(stabilized, Is.EqualTo(3));
            var error = Assert.Throws<InconclusiveException>(() => AllocationAssertions.Zero(windows.Result));
            Assert.That(error.Message, Does.Contain("process-wide counters (CollectionCount and TotalMemory); unclean evidence after 3 windows; cannot attribute to the simulation; strict zero is established only in direct-bytes environments (SGen mono / CoreCLR)"));
            for (int i = 1; i <= 3; i++)
                Assert.That(error.Message, Does.Contain("window " + i + "/3: CollectionCount delta"));
            Assert.Throws<InvalidOperationException>(() => windows.Begin(meter));
        }

        [TestCase(-4096, 2)]
        [TestCase(4096, 2)]
        [TestCase(4096, 3)]
        [TestCase(4096, 4)]
        public void StableCollectionMemoryChangeRetriesUntilCleanOrInconclusive(long memory, int clean)
        {
            var samples = new AllocationSample[3];
            for (int i = 0; i < samples.Length; i++)
                samples[i] = new AllocationSample(AllocationBasis.Indirect, totalMemory: i + 1 == clean ? 0 : memory);
            var meter = new ScriptedMeter(samples);
            int stabilized = 0;
            var windows = new AllocationWindow(() => stabilized++);
            do
            {
                windows.Begin(meter);
                windows.End();
            }
            while (windows.NeedsRetry);
            Assert.That(windows.Count, Is.EqualTo(Math.Min(clean, 3)));
            Assert.That(stabilized, Is.EqualTo(windows.Count));
            if (clean <= 3)
            {
                AllocationAssertions.Zero(windows.Result);
                Assert.That(windows.Result.Label, Does.Contain("clean window " + clean + "/3"));
            }
            else
            {
                var error = Assert.Throws<InconclusiveException>(() => AllocationAssertions.Zero(windows.Result));
                Assert.That(error.Message, Does.Contain("cannot attribute to the simulation"));
                for (int i = 1; i <= 3; i++)
                    Assert.That(error.Message, Does.Contain("window " + i + "/3: CollectionCount delta 0/1/2=0/0/0; TotalMemory delta=" + memory));
                Assert.That(error.Message, Does.Not.Contain("\u7591\u4f3c\u771f\u5b9e\u5206\u914d"));
            }
        }

        [TestCase(4096, 300, 0, false)]
        [TestCase(18900, 300, 0, false)]
        [TestCase(19200, 300, 0, true)]
        [TestCase(8192, 128, 0, true)]
        [TestCase(19200, 300, 4096, false)]
        [TestCase(-19200, 300, 0, false)]
        [TestCase(19200, 0, 0, false)]
        public void ConsistentGrowthWarningUsesActualTicks(long memory, int ticks, long variation, bool suspected)
        {
            var meter = new ScriptedMeter(
                new AllocationSample(AllocationBasis.Indirect, totalMemory: memory),
                new AllocationSample(AllocationBasis.Indirect, totalMemory: memory + variation),
                new AllocationSample(AllocationBasis.Indirect, totalMemory: memory));
            var windows = new AllocationWindow(() => { });
            do
            {
                windows.Begin(meter);
                windows.End(ticks);
            }
            while (windows.NeedsRetry);
            Assert.That(windows.Count, Is.EqualTo(3));
            var error = Assert.Throws<InconclusiveException>(() => AllocationAssertions.Zero(windows.Result));
            Assert.That(error.Message.Contains("\u7591\u4f3c\u771f\u5b9e\u5206\u914d"), Is.EqualTo(suspected));
        }

        [TestCase(0)]
        [TestCase(1)]
        public void DirectByteEqualityNeverRetriesOrCollects(long bytes)
        {
            var meter = new ScriptedMeter(new AllocationSample(AllocationBasis.Direct, bytes));
            var windows = new AllocationWindow(() => throw new Exception("Direct measurement must not collect."));
            windows.Begin(meter);
            windows.End();
            Assert.That(windows.NeedsRetry, Is.False);
            Assert.That(windows.Count, Is.EqualTo(1));
            if (bytes == 0)
                AllocationAssertions.Zero(windows.Result);
            else
                Assert.Throws<InvalidOperationException>(() => AllocationAssertions.Zero(windows.Result));
        }

        [Test]
        public void LostCounterDuringRetryRemainsInconclusive()
        {
            var meter = new ScriptedMeter(new AllocationSample(AllocationBasis.Indirect, gen0: 1),
                new AllocationSample(AllocationBasis.Unavailable, reason: "Counter lost on retry."));
            var windows = new AllocationWindow();
            do
            {
                windows.Begin(meter);
                windows.End();
            }
            while (windows.NeedsRetry);
            Assert.That(windows.Count, Is.EqualTo(2));
            Assert.Throws<InconclusiveException>(() => AllocationAssertions.Zero(windows.Result));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Retained64KiBPerTickNeverPasses(bool forceIndirect)
        {
            const int ticks = 128;
            var body = new AllocatingBody(ticks * AllocationWindow.MaximumWindows + 1);
            body.Tick();
            var meter = new AllocationMeter(forceIndirect);
            if (forceIndirect)
                Assert.That(meter.Basis, Is.EqualTo(AllocationBasis.Indirect));
            var windows = new AllocationWindow();
            var measured = new AllocationSample[AllocationWindow.MaximumWindows];
            do
            {
                windows.Begin(meter);
                for (int tick = 0; tick < ticks; tick++)
                    body.Tick();
                measured[windows.Count] = windows.End(ticks);
            }
            while (windows.NeedsRetry);
            AllocationSample sample = windows.Result;
            Assert.That(sample.Basis, Is.Not.EqualTo(AllocationBasis.Unavailable), meter.DescribeProbe());
            Assert.That(sample.MeetsZeroContract, Is.False);
            if (sample.Basis == AllocationBasis.Direct)
            {
                Assert.That(sample.Bytes, Is.GreaterThanOrEqualTo(ticks * 65536L));
                Assert.Throws<InvalidOperationException>(() => AllocationAssertions.Zero(sample));
            }
            else
            {
                Assert.That(windows.Count, Is.EqualTo(AllocationWindow.MaximumWindows));
                bool identical = true;
                foreach (AllocationSample attempt in measured)
                {
                    Assert.That(attempt.MeetsZeroContract, Is.False);
                    Assert.That(attempt.TotalMemory, Is.GreaterThan(4096L * 64));
                    Assert.That(attempt.TotalMemory / ticks, Is.GreaterThanOrEqualTo(64));
                    identical &= attempt.TotalMemory == measured[0].TotalMemory;
                }
                var error = Assert.Throws<InconclusiveException>(() => AllocationAssertions.Zero(sample));
                if (identical)
                    Assert.That(error.Message, Does.Contain("\u7591\u4f3c\u771f\u5b9e\u5206\u914d"));
            }
            Assert.That(body.Count, Is.EqualTo(1 + ticks * windows.Count));
            GC.KeepAlive(body);
        }

        private sealed class AllocatingBody
        {
            private readonly byte[][] retained;
            internal int Count;
            internal AllocatingBody(int capacity) => retained = new byte[capacity][];
            internal void Tick()
            {
                var bytes = new byte[65536];
                bytes[0] = 17;
                bytes[bytes.Length - 1] = 29;
                retained[Count++] = bytes;
            }
        }

        private sealed class ScriptedMeter : IAllocationMeter
        {
            private readonly AllocationSample[] samples;
            private int index;
            internal ScriptedMeter(params AllocationSample[] samples) => this.samples = samples;
            public AllocationSample Read() => new AllocationSample(samples[index].Basis);
            public AllocationSample Difference(AllocationSample before) => samples[index++];
        }
    }
}
