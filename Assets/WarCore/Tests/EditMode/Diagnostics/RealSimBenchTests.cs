using NUnit.Framework;

namespace WarCore.Tests.Diagnostics
{
    public sealed class RealSimBenchTests
    {
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        // The dense real-load groups run 300 ticks at hundreds of ms each in the editor; the default 180 s limit would abort them.
        [Category("Perf")]
        [Timeout(3600000)]
        public void RealWorkloadsKeepAllPeakWorkFor300ConsecutiveTicks(bool dense, bool peak)
        {
            RealSimBench.Run(dense, peak, TestContext.Out, BenchmarkTestMode.Ticks);
        }

        [Test]
        public void ReportActualEnvironmentAndObserverCalibration()
        {
            BenchEnvironment.Report(TestContext.Out);
            QueryProfiling.Calibration(TestContext.Out);
        }
    }
}
