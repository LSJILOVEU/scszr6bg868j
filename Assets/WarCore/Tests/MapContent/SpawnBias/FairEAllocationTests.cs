#if !UNITY_EDITOR && !UNITY_5_3_OR_NEWER
using System;
using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class FairEAllocationTests
    {
        [Test]
        public void FairE_OnAllocatesZero()
        {
            M6World w = FairE.World("control", 41, true);
            byte[] initial = w.Sim.SaveRetainedSnapshot();
            FairE.Finish(w);
            Assert.That((w.Match.Winner, w.Match.EndTick), Is.EqualTo((1, 2068L)));
            ulong warmHash = w.Hash;
            w.Sim.RestoreRetainedSnapshot(initial);

            long controlStart = GC.GetAllocatedBytesForCurrentThread();
            var control = new byte[32768];
            long controlBytes = GC.GetAllocatedBytesForCurrentThread() - controlStart;
            GC.KeepAlive(control);
            Assert.That(controlBytes, Is.GreaterThanOrEqualTo(32768));
            AllocationMeter.Stabilize();
            long before = GC.GetAllocatedBytesForCurrentThread();
            FairE.Finish(w);
            long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That((w.Match.Winner, w.Match.EndTick), Is.EqualTo((1, 2068L)));
            Assert.That(w.Hash, Is.EqualTo(warmHash));
            Assert.That(bytes, Is.EqualTo(0L));
        }
    }
}
#endif
