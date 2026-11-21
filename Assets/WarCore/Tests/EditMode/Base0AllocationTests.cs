// Managed direct counter tests are deliberately excluded from Unity discovery.
#if !UNITY_EDITOR && !UNITY_5_3_OR_NEWER
using System;
using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    // BASE-0 V13: StatLine.Scale and StatLine.Apply allocate nothing.
    public sealed class Base0AllocationTests
    {
        [Test]
        public void V13_ScaleAndApplyAllocateZero()
        {
            int[] factors = { 800, 700, 1250, 999, 4000, 1, 3999 };
            int[] gears = { 250, 1500, 999 };
            int[] drags = { 800, 700, 850, 700 };
            long sum = StatLine.Scale(6553, factors) + StatLine.Apply(10000, gears, drags, 400, 1500);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10000; i++)
                sum += StatLine.Scale(6553 + i, factors);
            long scale = GC.GetAllocatedBytesForCurrentThread() - before;
            before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10000; i++)
                sum += StatLine.Apply(10000 + i, gears, drags, 400 + (i & 1) * 600, 0);
            long apply = GC.GetAllocatedBytesForCurrentThread() - before;
            before = GC.GetAllocatedBytesForCurrentThread();
            var control = new byte[64];
            long positive = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(scale, Is.Zero, "10000 Scale");
            Assert.That(apply, Is.Zero, "10000 Apply");
            Assert.That(positive, Is.GreaterThanOrEqualTo(64), "direct counter positive control");
            GC.KeepAlive(control);
            Assert.That(sum, Is.Not.Zero);
        }
    }
}
#endif
