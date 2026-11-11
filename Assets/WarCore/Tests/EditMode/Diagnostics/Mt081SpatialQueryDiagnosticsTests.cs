using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests.Diagnostics
{
    public sealed class Mt081SpatialQueryDiagnosticsTests
    {
        [Test]
        public void ResetClearsEmptyLookups()
        {
            var counters = new SpatialQueryDiagnostics { EmptyLookups = 7 };
            counters.Reset();
            Assert.That(counters.EmptyLookups, Is.Zero);
        }

        [Test]
        public void ResetClearsHashProbes()
        {
            var counters = new SpatialQueryDiagnostics { HashProbes = 7 };
            counters.Reset();
            Assert.That(counters.HashProbes, Is.Zero);
        }
    }
}
