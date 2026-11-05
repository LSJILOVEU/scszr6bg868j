using System;
using System.IO;
using NUnit.Framework;

namespace WarCore.Tests
{
    [TestFixture]
    public sealed class StableIdentityPerformanceTests
    {
        [Test, Category("StableIdentityPerformance"), Timeout(600000)]
        public void TenThousandIdentitySmokeReportsWorkAndPositiveControls()
        {
            var output = new StringWriter();
            StableIdentityBench.Run("smoke",output);
            string text = output.ToString();
            TestContext.Write(text);
            Assert.That(text,Does.Contain("entities=10000"));
            Assert.That(text,Does.Contain("samples=30"));
            Assert.That(text,Does.Contain("identity-benchmark=complete"));
        }
        [Test, Category("Perf"), Timeout(1800000)]
        public void TenThousandIdentityFullUsesThreeHundredSamplesPerGroup()
        {
            StableIdentityBench.Run("full",TestContext.Out);
        }
        [Test, Category("StableIdentityPerformance")]
        public void BenchmarkComparisonRejectsAnInjectedWrongStableKey()
        {
            Assert.Throws<InvalidOperationException>(() => StableIdentityBench.CheckKey(7,8));
            StableIdentityBench.CheckKey(7,7);
        }
    }
}
