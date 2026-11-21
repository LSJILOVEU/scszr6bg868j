// Managed direct counter tests are excluded from Unity discovery, as M0c2S2AllocationTests.
#if !UNITY_EDITOR && !UNITY_5_3_OR_NEWER
using System;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Presentation;
using static WarCore.Tests.Ml11aWorld;

namespace WarCore.Tests
{
    // ML-11a acceptance 18: the layered AI allocates nothing in a Step: the walking-cost tables are built with the world.
    public sealed class Ml11aAllocationTests
    {
        private static M6World Assault() =>
            World(new[] { Walker(1, 30500, 31500), Walker(2, 30500, 32500), Walker(3, 29500, 31500), Walker(4, 29500, 32500) },
                new[] { PointUp(1, 41500, 32500, 3), PointDown(2, 51500, 32500) }, capture: 400);

        // Four soldiers think, are ordered up the stair, climb, arrive and stop, with a spread dispatch that reads the tables on
        // every think.
        [Test]
        public void TheClimbAllocatesNothing()
        {
            M6World warm = Assault();
            Install(warm, "dispatch", new NearestDispatch(300, 2));
            warm.Step(200);
            M6World w = Assault();
            Install(w, "dispatch", new NearestDispatch(300, 2));
            w.Step(12);
            AllocationMeter.Stabilize();
            long total = 0;
            for (int i = 0; i < 188; i++)
            {
                long before = GC.GetAllocatedBytesForCurrentThread();
                w.Sim.Step(default);
                total += GC.GetAllocatedBytesForCurrentThread() - before;
            }
            long start = GC.GetAllocatedBytesForCurrentThread();
            var control = new byte[64];
            long positive = GC.GetAllocatedBytesForCurrentThread() - start;
            GC.KeepAlive(control);
            int up = 0;
            for (ulong key = 1; key <= 4; key++) up += Layer(w, key) == 1 ? 1 : 0;
            Assert.That(up, Is.GreaterThanOrEqualTo(1), "the window covers a climb");
            Assert.That(total, Is.EqualTo(0L));
            Assert.That(positive, Is.GreaterThanOrEqualTo(64), "direct counter positive control");
        }
    }
}
#endif
