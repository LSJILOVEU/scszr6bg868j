// Managed direct counter tests are excluded from Unity discovery, as M0c2S2AllocationTests.
#if !UNITY_EDITOR && !UNITY_5_3_OR_NEWER
using System;
using NUnit.Framework;
using WarCore.Sim;
using static WarCore.Tests.Ml5Plates;

namespace WarCore.Tests
{
    // ML-5 acceptance 6: once warm, plate-crossing queries allocate nothing.
    public sealed class Ml5AllocationTests
    {
        private static int Run(PlateField f, int count)
        {
            SimVec3 a = V(32768, 180224, 98304), b = V(491520, 150000, 200000);
            int blocked = 0;
            for (int n = 0; n < count; n++)
            {
                if (f.TryFirstThrough(a, b, out PlateHit hit) && hit.PlateId != 0) blocked++;
                if (f.Blocks(V(n * 500, 100000, 98304), V(491520 - n * 300, 190000, 32768))) blocked++;
            }
            return blocked;
        }

        [Test]
        public void QueriesDoNotAllocate()
        {
            PlateField f = Field(8, 4, 0, 0, Flat(1, 2, 0, 4, 4, holes: (1, 1)), AlongX(2, 0, 0, 2, 1, new long[] { 0, 65536, 131072 }));
            Run(f, 1000);
            long before = GC.GetAllocatedBytesForCurrentThread();
            int blocked = Run(f, 1000);
            long steady = GC.GetAllocatedBytesForCurrentThread() - before;
            before = GC.GetAllocatedBytesForCurrentThread();
            var control = new byte[64];
            long positive = GC.GetAllocatedBytesForCurrentThread() - before;
            GC.KeepAlive(control);
            Assert.That(blocked, Is.GreaterThan(1000));
            Assert.That(steady, Is.Zero, "1000 TryFirstThrough and 1000 Blocks");
            Assert.That(positive, Is.GreaterThanOrEqualTo(64), "direct counter positive control");
        }
    }
}
#endif
