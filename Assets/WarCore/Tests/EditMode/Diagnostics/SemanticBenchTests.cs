using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;

namespace WarCore.Tests.Diagnostics
{
    internal static class BenchmarkTestMode
    {
        internal static int Ticks => SelectTicks(Environment.GetCommandLineArgs());

        // NUnit categories alone do not exclude long cases from an unfiltered Unity run.
        // Select duration instead of skipping cases: both durations execute identical assertions.
        internal static int SelectTicks(string[] args)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--perf")
                    return 300;
                if (args[i] == "-testCategory" && i + 1 < args.Length)
                    foreach (string category in args[i + 1].Split(';'))
                        if (category == "Perf")
                            return 300;
            }
            return 30;
        }
    }

    public sealed class SemanticBenchTests
    {
        [TestCase(false, false, false)]
        [TestCase(false, true, false)]
        [TestCase(true, false, false)]
        [TestCase(true, true, false)]
        [TestCase(true, true, true)]
        [Category("Perf")]
        [Timeout(3600000)]
        public void SemanticProfilesKeepBlockersAndActualRetainedCoverWork(bool dense, bool peak, bool overflow)
        {
            int ticks = BenchmarkTestMode.Ticks;
            string directory = Path.Combine(BenchEnvironment.OutputDirectory(), "semantic", ticks == 30 ? "smoke" : "full");
            BenchEnvironment.Report(TestContext.Out);
            var scene = new SemanticBenchScene(dense, peak, overflow);
            SemanticBenchMeasurement.Run(scene, ticks, new SemanticDirectMeter(), TestContext.Out, directory);
            Assert.That(scene.LastCoverRays, Is.EqualTo(scene.Affected));
            if (overflow)
            {
                Assert.That(scene.ExplosionTotal, Is.GreaterThan(512));
                Assert.That(scene.Affected, Is.EqualTo(512));
                Assert.That(scene.LastTruncatedQueries, Is.GreaterThan(0));
                Assert.That(scene.LastTruncatedHits, Is.GreaterThanOrEqualTo(scene.ExplosionTotal - 512));
            }
        }

        [Test]
        public void SelectedPerfMethodsAndCasesAreNonzero()
        {
            int methods = 0;
            int cases = 0;
            foreach (Type type in typeof(SemanticBenchTests).Assembly.GetTypes())
                foreach (MethodInfo method in type.GetMethods())
                    foreach (CategoryAttribute category in method.GetCustomAttributes(typeof(CategoryAttribute), false))
                        if (category.Name == "Perf")
                        {
                            methods++;
                            cases += method.GetCustomAttributes(typeof(TestCaseAttribute), false).Length;
                        }
            Assert.That(methods, Is.GreaterThan(0));
            Assert.That(cases, Is.EqualTo(9), "Four frozen legacy, four semantic and one overflow case must be selectable.");
            TestContext.WriteLine("Perf inventory: methods={0}; cases={1}; selected duration={2}; no cases skipped; default smoke and category-selected full share every assertion.", methods, cases, BenchmarkTestMode.Ticks);
        }

        [Test]
        public void DurationSelectionRequiresAnExplicitPerfRequest()
        {
            Assert.That(BenchmarkTestMode.SelectTicks(new[] { "-runTests" }), Is.EqualTo(30));
            Assert.That(BenchmarkTestMode.SelectTicks(new[] { "-runTests", "-testCategory", "Perf" }), Is.EqualTo(300));
            Assert.That(BenchmarkTestMode.SelectTicks(new[] { "-testCategory", "Other;Perf" }), Is.EqualTo(300));
            Assert.That(BenchmarkTestMode.SelectTicks(new[] { "-testCategory", "!Perf" }), Is.EqualTo(30));
            Assert.That(BenchmarkTestMode.SelectTicks(new[] { "--perf" }), Is.EqualTo(300));
        }

        [Test]
        public void AllocationEvidenceNeverTurnsIndirectOrUnavailableIntoZeroBytes()
        {
            Assert.That(new AllocationSample(AllocationBasis.Direct, 0).CsvValues(), Is.EqualTo("direct-bytes,0,,,,"));
            Assert.That(new AllocationSample(AllocationBasis.Indirect, 0).CsvValues(), Is.EqualTo("indirect(CollectionCount+TotalMemory),,0,0,0,0"));
            Assert.That(default(AllocationSample).CsvValues(), Is.EqualTo("unavailable,,,,,"));
            Assert.Throws<InvalidOperationException>(() => AllocationAssertions.Check(new AllocationSample(AllocationBasis.Direct, 1)));
        }
    }
}
