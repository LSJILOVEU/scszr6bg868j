// Managed direct counter tests are excluded from Unity discovery, as M0c2S2AllocationTests.
#if !UNITY_EDITOR && !UNITY_5_3_OR_NEWER
using System;
using NUnit.Framework;
using WarCore.Sim;
using static WarCore.Tests.Ml6Nav;

namespace WarCore.Tests
{
    // ML-6 acceptance 8: once warm, layered searches (across layers too) allocate nothing.
    public sealed class Ml6AllocationTests
    {
        private static long Run(LayeredPathfinder f, int count)
        {
            long total = 0;
            for (int n = 0; n < count; n++)
            {
                if (f.Find(0, 5) == GridPathStatus.Found) total += f.TotalCostWide;
                if (f.FindWithin(5, 0, 4999) == GridPathStatus.Unreachable) total++;
            }
            return total;
        }

        [Test]
        public void SearchesDoNotAllocate()
        {
            LayeredNav nav = Nav(Open(6, 1, (2, 0), (3, 0)), Ml5Plates.Flat(1, 2, 0, 2, 1, 19660, 32768));
            var f = new LayeredPathfinder(nav);
            Run(f, 1000);
            long before = GC.GetAllocatedBytesForCurrentThread();
            long total = Run(f, 1000);
            long steady = GC.GetAllocatedBytesForCurrentThread() - before;
            before = GC.GetAllocatedBytesForCurrentThread();
            var control = new byte[64];
            long positive = GC.GetAllocatedBytesForCurrentThread() - before;
            GC.KeepAlive(control);
            Assert.That(total, Is.EqualTo(1000L * 5000 + 1000), "every search crosses the plate");
            Assert.That(steady, Is.Zero, "1000 Find and 1000 FindWithin");
            Assert.That(positive, Is.GreaterThanOrEqualTo(64), "direct counter positive control");
        }
    }
}
#endif
