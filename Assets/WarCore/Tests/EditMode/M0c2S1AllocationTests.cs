// Strict direct-byte proof runs through Tools/run-managed-checks.sh (SGen/CoreCLR).
// Unity's flat counter cannot prove zero; its 20-case inconclusive allowlist is frozen.
#if !UNITY_EDITOR && !UNITY_5_3_OR_NEWER
using System;
using NUnit.Framework;

namespace WarCore.Tests
{
    [TestFixture, Category("M0c2S1"), Category("M0c2S1Allocation")]
    public sealed class M0c2S1AllocationTests
    {
        [Test]
        public void FirstHashAfterRestoreHasNoLazyRandomViewAllocation()
        {
            var source = M0c2S1Fixture.Create();
            for (int tick = 0; tick < 40; tick++) M0c2S1Fixture.Step(source);
            var restored = M0c2S1Fixture.Create(false, true);
            restored.RestoreRetainedSnapshot(source.SaveRetainedSnapshot(), true);
            long beforeControl = GC.GetAllocatedBytesForCurrentThread();
            var control = new byte[32768];
            long controlBytes = GC.GetAllocatedBytesForCurrentThread() - beforeControl;
            long before = GC.GetAllocatedBytesForCurrentThread();
            ulong digest = restored.StateHashRetained();
            long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            GC.KeepAlive(control);
            Assert.That(controlBytes, Is.GreaterThanOrEqualTo(32768), "Direct byte counter positive control.");
            Assert.That(bytes, Is.Zero, "The first restored hash must not create SortedList Keys/Values views.");
            Assert.That(digest, Is.EqualTo(source.StateHashRetained()));
            TestContext.WriteLine("S1 first restored hash: {0} B; positive control: {1} B", bytes, controlBytes);
        }
    }
}
#endif
