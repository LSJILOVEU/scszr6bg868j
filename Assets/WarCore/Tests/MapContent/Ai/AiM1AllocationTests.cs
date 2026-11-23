// Managed direct counter tests are excluded from Unity discovery, as M0c2S2AllocationTests.
#if !UNITY_EDITOR && !UNITY_5_3_OR_NEWER
using System;
using NUnit.Framework;

namespace WarCore.Tests
{
    // AI-M1 acceptance 10: a Step allocates nothing with the pressure reading strikes and the melee units asking the engage part.
    public sealed class AiM1AllocationTests
    {
        // The nerve battle from tick 13 to its end (tick 1163): strikes, falls, pinned and broken units, units coming back.
        [Test]
        public void TheNerveBattleAllocatesNothing()
        {
            M6World warm = AiM1Field.World(AiM1Field.Nerve);
            warm.Step(1163);
            Assert.That(warm.Mode.Winner, Is.EqualTo(1));
            M6World w = AiM1Field.World(AiM1Field.Nerve);
            w.Step(12);
            AllocationMeter.Stabilize();
            long total = 0;
            for (int i = 0; i < 1151; i++)
            {
                long before = GC.GetAllocatedBytesForCurrentThread();
                w.Sim.Step(default);
                total += GC.GetAllocatedBytesForCurrentThread() - before;
            }
            Assert.That(w.Mode.Winner, Is.EqualTo(1), "the window covers the battle to its end");
            Assert.That(total, Is.EqualTo(0L));
            long start = GC.GetAllocatedBytesForCurrentThread();
            var control = new byte[64];
            long positive = GC.GetAllocatedBytesForCurrentThread() - start;
            GC.KeepAlive(control);
            Assert.That(positive, Is.GreaterThanOrEqualTo(64), "direct counter positive control");
        }
    }
}
#endif
