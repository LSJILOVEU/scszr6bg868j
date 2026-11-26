// Managed direct counter tests are excluded from Unity discovery, as M0c2S2AllocationTests.
#if !UNITY_EDITOR && !UNITY_5_3_OR_NEWER
using System;
using NUnit.Framework;

namespace WarCore.Tests
{
    // RC2-3 acceptance 7: once settled (from tick 300 on) a tick of a stage that takes the commander allocates nothing, to
    // the end of the match. A first run of the same stage warms the code paths; the second is measured.
    public sealed class CommanderAllocationTests
    {
        [TestCaseSource(typeof(CommanderStageTests), nameof(CommanderStageTests.Stages))]
        public void ASettledTickAllocatesNothing(string id)
        {
            M6World warm = CommanderStageTests.World(id);
            while (CommanderStageTests.Playing(warm)) warm.Session.StepOnce();
            M6World w = CommanderStageTests.World(id);
            for (int i = 0; i < 300; i++) w.Session.StepOnce();
            AllocationMeter.Stabilize();
            long total = 0;
            int ticks = 0;
            while (w.Mode.Winner == 0 && w.Sim.Clock.Tick < warm.Session.Tick)
            {
                long before = GC.GetAllocatedBytesForCurrentThread();
                w.Sim.Step(default);
                total += GC.GetAllocatedBytesForCurrentThread() - before;
                ticks++;
            }
            Assert.That(w.Sim.Clock.Tick, Is.EqualTo(warm.Session.Tick), "the window covers the match to its end");
            Assert.That(ticks, Is.GreaterThan(1000));
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
