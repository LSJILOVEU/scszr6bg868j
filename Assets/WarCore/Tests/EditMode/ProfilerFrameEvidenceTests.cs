using System;
using NUnit.Framework;

namespace WarCore.Tests
{
    public sealed class ProfilerFrameEvidenceTests
    {
        private static ProfilerFrameReading Reading(int frame, long bytes = 0, long count = 0)
            => ProfilerFrameReading.Completed(frame, 1, 1, bytes, count);

        private static ProfilerFrameEvidence Controlled(ProfilerFrameReading sample)
        {
            var evidence = new ProfilerFrameEvidence();
            evidence.Control(Reading(1, 32768, 1), false);
            evidence.Observe(sample);
            evidence.Control(Reading(3, 32768, 1), true);
            return evidence;
        }

        [TestCase(0)]
        [TestCase(32767)]
        public void FlatAndUndersizedControlsNeverGainDirectLabel(long bytes)
        {
            var evidence = new ProfilerFrameEvidence();
            evidence.Control(Reading(1, bytes, bytes == 0 ? 0 : 1), false);
            evidence.Control(Reading(2, 32768, 1), true);
            Assert.That(evidence.Label, Is.EqualTo("unavailable"));
            Assert.That(evidence.ByteColumn(Reading(3)), Is.Empty);
            Assert.That(evidence.CountColumn(Reading(3)), Is.Empty);
            Assert.That(evidence.Distribution(new[] { Reading(3) }, 0, 1), Does.Not.Contain("gc_bytes"));
            Assert.Throws<InconclusiveException>(() => AllocationAssertions.Check(evidence.Sample(Reading(3))));
        }

        [Test]
        public void ClosingControlIsRequiredEvenAfterSuccessfulOpeningControl()
        {
            var evidence = new ProfilerFrameEvidence();
            evidence.Control(Reading(1, 32768, 1), false);
            Assert.That(evidence.IsDirect, Is.False);
            Assert.That(evidence.Describe(Reading(2)), Does.Contain("allocation=unavailable"));
        }

        [TestCase(0, 1)]
        [TestCase(1, 0)]
        [TestCase(2, 1)]
        [TestCase(1, 2)]
        public void StaleOrMultipleSamplesCannotMasqueradeAsOneTick(int byteSamples, int countSamples)
        {
            var sample = ProfilerFrameReading.Completed(2, byteSamples, countSamples, 0, 0);
            Assert.That(sample.Available, Is.False);
            Assert.That(Controlled(sample).IsDirect, Is.False);
        }

        [TestCase(-1, 1)]
        [TestCase(1, -1)]
        [TestCase(0, 1)]
        [TestCase(1, 0)]
        public void CounterUnitsMustAgreeOnWhetherAnAllocationOccurred(long bytes, long count)
        {
            Assert.That(Controlled(Reading(2, bytes, count)).IsDirect, Is.False);
        }

        [TestCase(1)]
        [TestCase(4)]
        public void RepeatedOrMissingFrameInvalidatesTheWholeRun(int frame)
        {
            Assert.That(Controlled(Reading(frame)).IsDirect, Is.False);
        }

        [Test]
        public void MissingOrDefaultReadingsCannotBecomeZeroEvidence()
        {
            Assert.That(Controlled(default).IsDirect, Is.False);
            Assert.That(Controlled(ProfilerFrameReading.Unavailable(2, "No Editor frame flush.")).IsDirect, Is.False);
        }

        [Test]
        public void TrailingControlDetectsAByteCounterThatStoppedResponding()
        {
            var evidence = new ProfilerFrameEvidence();
            evidence.Control(Reading(1, 32768, 1), false);
            evidence.Observe(Reading(2));
            evidence.Control(Reading(3), true);
            Assert.That(evidence.IsDirect, Is.False);
            Assert.That(evidence.ByteColumn(Reading(2)), Is.Empty);
        }

        [Test]
        public void DirectProfilerUsesExistingStrictAssertionsIncludingOneByteFailure()
        {
            ProfilerFrameReading zero = Reading(2);
            var evidence = Controlled(zero);
            Assert.That(evidence.Label, Is.EqualTo("direct-profiler(ProfilerRecorder)"));
            AllocationAssertions.Check(evidence.Sample(zero));
            ProfilerFrameReading one = Reading(2, 1, 1);
            Assert.Throws<InvalidOperationException>(() => AllocationAssertions.Check(evidence.Sample(one)));
            Assert.Throws<InvalidOperationException>(() => AllocationAssertions.CheckDirect(evidence.Sample(one)));
            Assert.Throws<InvalidOperationException>(() => AllocationAssertions.CheckObserved(evidence.Sample(one)));
            Assert.That(evidence.Describe(one), Does.Contain("allocated_bytes=1"));
            Assert.That(evidence.Sample(one).CsvValues(), Does.StartWith("direct-profiler(ProfilerRecorder),1,"));
        }

        [Test]
        public void WarmupAndSteadyDistributionsUseSeparateRanges()
        {
            var evidence = new ProfilerFrameEvidence();
            var samples = new[] { Reading(2, 64, 2), Reading(3), Reading(4, 1, 1), Reading(5) };
            evidence.Control(Reading(1, 32768, 1), false);
            foreach (ProfilerFrameReading sample in samples)
                evidence.Observe(sample);
            evidence.Control(Reading(6, 32768, 1), true);
            string warmup = evidence.Distribution(samples, 0, 1);
            string steady = evidence.Distribution(samples, 1, 3);
            Assert.That(warmup, Does.Contain("gc_bytes_sum=64; gc_bytes_max=64; nonzero_ticks=1"));
            Assert.That(steady, Does.Contain("gc_bytes_sum=1; gc_bytes_max=1; nonzero_ticks=1"));
            Assert.That(steady, Does.Contain("strict_zero=fail"));
            Assert.That(evidence.Distribution(samples, 3, 1), Does.Contain("strict_zero=pass"));
            Assert.That(evidence.Distribution(samples, 0, 0), Does.Contain("strict_zero=unproven"));
            Assert.That(AllocationSample.Summary(new[] { evidence.Sample(samples[1]) }),
                Does.Contain("allocation=direct-profiler(ProfilerRecorder)"));
        }

        [Test]
        public void MixedEvidenceIsUnavailableInUnifiedSummary()
        {
            var evidence = Controlled(Reading(2));
            Assert.That(AllocationSample.Summary(new[] { evidence.Sample(Reading(2)), new AllocationSample(AllocationBasis.Direct) }),
                Does.Contain("allocation=unavailable"));
        }

        [Test]
        public void FramePolicyDoesNotAllocateInsideTheMeasuredLoop()
        {
            var evidence = new ProfilerFrameEvidence();
            evidence.Control(Reading(1, 32768, 1), false);
            for (int i = 2; i < 34; i++)
                evidence.Observe(Reading(i));
            var meter = new AllocationMeter();
            AllocationSample before = meter.Read();
            for (int i = 34; i < 1034; i++)
                evidence.Observe(Reading(i));
            AllocationAssertions.Zero(meter.Difference(before));
            evidence.Control(Reading(1034, 32768, 1), true);
            Assert.That(evidence.IsDirect, Is.True);
        }
    }
}
