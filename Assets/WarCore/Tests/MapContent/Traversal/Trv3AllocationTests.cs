// Managed direct counter tests are excluded from Unity discovery, as Ml6AllocationTests.
#if !UNITY_EDITOR && !UNITY_5_3_OR_NEWER
using System;
using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    // TRV-3 T3-15: once warm, searches on the densest link graph allocate nothing (the heap was sized for the links).
    public sealed class Trv3AllocationTests
    {
        private static long Run(LayeredPathfinder f, int far, int near, int count)
        {
            long total = 0;
            for (int n = 0; n < count; n++)
            {
                if (f.Find(0, far) == GridPathStatus.Found) total += f.TotalCostWide;
                if (f.FindWithin(far, near, long.MaxValue, 3) == GridPathStatus.Found) total += f.TotalCostWide;
            }
            return total;
        }

        [Test]
        public void T3_15_DenseSearchesDoNotAllocate()
        {
            LayeredNav nav = Trv3ReachTests.Dense(24);
            var f = new LayeredPathfinder(nav);
            int far = nav.Count - 1, near = 30;
            long first = Run(f, far, near, 20);
            long before = GC.GetAllocatedBytesForCurrentThread();
            long total = Run(f, far, near, 20);
            long steady = GC.GetAllocatedBytesForCurrentThread() - before;
            before = GC.GetAllocatedBytesForCurrentThread();
            var control = new byte[64];
            long positive = GC.GetAllocatedBytesForCurrentThread() - before;
            GC.KeepAlive(control);
            Assert.That(total, Is.EqualTo(first).And.GreaterThan(0));
            Assert.That(steady, Is.Zero, "40 searches on the dense graph");
            Assert.That(positive, Is.GreaterThanOrEqualTo(64), "direct counter positive control");
        }
    }
}
#endif
