#if !UNITY_EDITOR && !UNITY_5_3_OR_NEWER
using System;
using NUnit.Framework;

namespace WarCore.Tests
{
    public sealed class FairEAllocationTests
    {
        [Test]
        public void FairE_OnSteadyStateAllocatesZeroBytes()
        {
            M6World w = FairEFixture.Create("control", 41, true);
            byte[] start = w.Sim.SaveRetainedSnapshot();
            Window(w);
            long end = w.Match.EndTick; ulong hash = w.Hash;
            w.Sim.RestoreRetainedSnapshot(start);

            long beforeControl = GC.GetAllocatedBytesForCurrentThread();
            var control = new byte[32768];
            long controlBytes = GC.GetAllocatedBytesForCurrentThread() - beforeControl;
            GC.KeepAlive(control);
            Assert.That(controlBytes, Is.GreaterThanOrEqualTo(32768));
            AllocationMeter.Stabilize();

            long bytes = Window(w);
            Assert.That(bytes, Is.EqualTo(0L));
            Assert.That(w.Match.EndTick, Is.EqualTo(end));
            Assert.That(w.Hash, Is.EqualTo(hash));
        }

        // Keep assertions outside this method so their boxed arguments cannot enter the measured interval.
        private static long Window(M6World w)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            while (!w.Sim.IsFinished) w.Step();
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }
    }
}
#endif
