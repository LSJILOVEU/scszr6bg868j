using System;
using NUnit.Framework;

namespace WarCore.Tests
{
    public sealed class AllocationMeterTests
    {
        [Test]
        public void ProcessWideCollectionEvidenceIsInconclusive()
        {
            var sample = new AllocationSample(AllocationBasis.Indirect, gen0: 1, gen1: 1, gen2: 1, totalMemory: -6029312);
            var error = Assert.Throws<InconclusiveException>(() => AllocationAssertions.Check(sample));
            Assert.That(error.Message, Does.Contain("process-wide counter"));
            Assert.That(error.Message, Does.Contain("cannot attribute to the simulation"));
        }

        [TestCase(-1)]
        [TestCase(1)]
        public void UnchangedCollectionsDoNotHideMemoryChanges(long delta)
        {
            var sample = new AllocationSample(AllocationBasis.Indirect, totalMemory: delta);
            Assert.That(sample.MeetsZeroContract, Is.False);
            Assert.Throws<InconclusiveException>(() => AllocationAssertions.Check(sample));
        }

        [Test]
        public void RuntimeProbeReports32KiBControlAndBasis()
        {
            var meter = new AllocationMeter();
#if UNITY_EDITOR
            TestContext.WriteLine("Allocation runtime=Unity Editor Mono; Unity={0}; optimization={1}",
                UnityEngine.Application.unityVersion, UnityEditor.Compilation.CompilationPipeline.codeOptimization);
#else
            TestContext.WriteLine("Allocation runtime={0}; framework={1}; not Unity Editor",
                Type.GetType("Mono.Runtime") == null ? "CoreCLR" : "standalone Mono", Environment.Version);
#endif
            TestContext.WriteLine(meter.DescribeProbe());
            AllocationSample before = meter.Read();
            AllocationSample idle = meter.Difference(before);
            AllocationAssertions.Zero(idle);
            if (meter.Basis == AllocationBasis.Direct)
                Assert.That(meter.DirectControlDelta.Bytes, Is.GreaterThanOrEqualTo(32768));
            else
                Assert.That(meter.DirectControlDelta.Basis != AllocationBasis.Direct || meter.DirectControlDelta.Bytes < 32768, Is.True);
        }

        [TestCase(0)]
        [TestCase(32767)]
        public void FlatOrUndersizedDirectCounterUsesControlledIndirectEvidence(long increment)
        {
            long direct = 0;
            long memory = 0;
            var meter = new AllocationMeter(() => direct += increment, _ => 0, () => memory += 8 * 1024 * 1024);
            Assert.That(meter.Basis, Is.EqualTo(AllocationBasis.Indirect));
            Assert.That(meter.DirectControlDelta.Bytes, Is.EqualTo(increment));
            TestContext.WriteLine(meter.DescribeProbe());
            AllocationSample before = meter.Read();
            Assert.Throws<InconclusiveException>(() => AllocationAssertions.Check(meter.Difference(before)));
        }

        [TestCase(32768)]
        [TestCase(65536)]
        public void DirectPositiveControlPreservesStrictEqualityIncludingOneByte(long increment)
        {
            long direct = 0;
            var meter = new AllocationMeter(() => direct += increment,
                _ => throw new Exception("Indirect counter used despite passing direct control."),
                () => throw new Exception("Indirect counter used despite passing direct control."));
            Assert.That(meter.Basis, Is.EqualTo(AllocationBasis.Direct));
            increment = 0;
            AllocationSample before = meter.Read();
            AllocationAssertions.Zero(meter.Difference(before));
            increment = 1;
            AllocationSample allocated = meter.Difference(before);
            Assert.That(allocated.Bytes, Is.EqualTo(1));
            Assert.Throws<InvalidOperationException>(() => AllocationAssertions.Check(allocated));
            Assert.Throws<InvalidOperationException>(() => AllocationAssertions.CheckObserved(allocated));
            TestContext.WriteLine("allocation=direct-bytes; a one-byte delta fails strict equality.");
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void EveryCollectionGenerationCanEnableFallbackAndFailTheAssertion(int generation)
        {
            int count = 0;
            var meter = new AllocationMeter(() => 0, g => g == generation ? ++count : 0, () => 0);
            Assert.That(meter.Basis, Is.EqualTo(AllocationBasis.Indirect));
            AllocationSample before = meter.Read();
            AllocationSample changed = meter.Difference(before);
            Assert.That(changed.CollectionsChanged, Is.True);
            Assert.Throws<InconclusiveException>(() => AllocationAssertions.Check(changed));
            TestContext.WriteLine(changed.Describe());
        }

        [TestCase(-1024)]
        [TestCase(0)]
        [TestCase(2048)]
        public void IndirectMemoryDeltaIsReportedWithoutAByteClaim(long memory)
        {
            var sample = new AllocationSample(AllocationBasis.Indirect, totalMemory: memory);
            if (memory == 0)
                AllocationAssertions.Zero(sample);
            else
                Assert.Throws<InconclusiveException>(() => AllocationAssertions.Check(sample));
            AllocationAssertions.CheckObserved(sample);
            Assert.That(sample.Describe(), Does.Contain("TotalMemory delta=" + memory));
            Assert.That(sample.Describe(), Does.Contain("cannot prove strict zero allocation"));
            Assert.That(sample.CsvValues().Split(',')[1], Is.Empty);
        }

        [Test]
        public void FlatCountersAreInconclusiveInsteadOfPassing()
        {
            var meter = new AllocationMeter(() => 0, _ => 0, () => 0);
            Assert.That(meter.Basis, Is.EqualTo(AllocationBasis.Unavailable));
            AllocationSample sample = meter.Difference(meter.Read());
            InconclusiveException error = Assert.Throws<InconclusiveException>(() => AllocationAssertions.Check(sample));
            Assert.That(error.Message, Does.Contain("allocation=unavailable"));
            Assert.That(error.Message, Does.Contain("reason="));
            TestContext.WriteLine(meter.DescribeProbe());
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void UnsupportedDirectApisUseTheSameControlledFallback(int kind)
        {
            long memory = 0;
            var meter = new AllocationMeter(() => throw Unsupported(kind), _ => 0, () => ++memory);
            Assert.That(meter.Basis, Is.EqualTo(AllocationBasis.Indirect));
            TestContext.WriteLine(meter.DescribeProbe());
        }

        [TestCase(true)]
        [TestCase(false)]
        public void UnsupportedIndirectApiIsInconclusive(bool collections)
        {
            var meter = new AllocationMeter(() => 0,
                _ => collections ? throw new NotSupportedException() : 0,
                () => collections ? 0 : throw new NotImplementedException());
            AllocationSample sample = meter.Read();
            Assert.Throws<InconclusiveException>(() => AllocationAssertions.Check(sample));
            TestContext.WriteLine(sample.Describe());
        }

        [TestCase(true)]
        [TestCase(false)]
        public void LateApiFailureDoesNotBecomeZeroEvidence(bool direct)
        {
            long value = 0;
            bool broken = false;
            var meter = new AllocationMeter(() => broken ? throw new NotSupportedException() : direct ? value += 32768 : 0,
                _ => 0, () => broken ? throw new NotSupportedException() : ++value);
            AllocationSample before = meter.Read();
            broken = true;
            AllocationSample sample = meter.Difference(before);
            Assert.That(sample.Basis, Is.EqualTo(AllocationBasis.Unavailable));
            Assert.Throws<InconclusiveException>(() => AllocationAssertions.Check(sample));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void DecreasingCountersAreInconclusive(bool direct)
        {
            long bytes = 0;
            int collections = 0;
            var meter = new AllocationMeter(() => direct ? bytes += 32768 : 0, _ => ++collections, () => 0);
            AllocationSample before = meter.Read();
            bytes = -1000000;
            collections = -1000;
            AllocationSample sample = meter.Difference(before);
            Assert.That(sample.Basis, Is.EqualTo(AllocationBasis.Unavailable));
            Assert.Throws<InconclusiveException>(() => AllocationAssertions.Check(sample));
        }

        [Test]
        public void MissingDelegatesAreInconclusive()
        {
            var meter = new AllocationMeter(null, null, null);
            AllocationSample sample = meter.Read();
            Assert.Throws<InconclusiveException>(() => AllocationAssertions.Check(sample));
        }

        [Test]
        public void SharedMeasurementAndSuccessGuardDoNotAllocateAfterWarmup()
        {
            var meter = new AllocationMeter();
            for (int warm = 0; warm < 32; warm++)
                AllocationAssertions.Check(meter.Difference(meter.Read()));
            AllocationSample before = meter.Read();
            for (int i = 0; i < 1000; i++)
                AllocationAssertions.Check(meter.Difference(meter.Read()));
            AllocationAssertions.Zero(meter.Difference(before));
        }

        private static Exception Unsupported(int kind)
        {
            if (kind == 0)
                return new NotSupportedException();
            if (kind == 1)
                return new NotImplementedException();
            return new EntryPointNotFoundException();
        }
    }
}
