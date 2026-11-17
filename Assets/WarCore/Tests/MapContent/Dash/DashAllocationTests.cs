// Strict direct-byte proof needs the headless CoreCLR run (as the M0c2 allocation tests); Unity's counter cannot prove zero.
#if !UNITY_EDITOR && !UNITY_5_3_OR_NEWER
using System;
using NUnit.Framework;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests.Dash
{
    // M7a acceptance 5: Sim.Step does not allocate in a dash world. Unit 1 is ordered along the 80 m map and dashes back and forth
    // (+Z then -Z, 20 m each) whenever its cooldown allows; every dash ends in a path rejoin. 300 ticks warm up the world, then
    // one measured window of 1200 ticks (20 dashes) after a positive control of the counter. The same world without the requests
    // is the control case.
    public sealed class DashAllocationTests
    {
        [Test]
        public void SimStep_DoesNotAllocateWhileDashing([Values(true, false)] bool dash)
        {
            SimSession s = DashWorlds.World(new[] { DashWorlds.Dasher(1, 2500, 1500), DashWorlds.Plain(2, 40500, 12500) }, DashWorlds.Map());
            Simulation sim = M3TestWorld.Sim(s);
            InterimLocomotion locomotion = DashWorlds.Loco(s);
            EntityId unit = M3TestWorld.Unit(s);
            locomotion.Order(unit, new SimVec3(SimFix.Ratio(155, 2), default, SimFix.Ratio(3, 2)));
            SimVec3 up = SimVec3.Metres(0, 0, 1), down = SimVec3.Metres(0, 0, -1);
            int dashes = 0;
            for (int i = 0; i < 300; i++)
            {
                if (dash && locomotion.TryDash(unit, (dashes & 1) == 0 ? up : down)) dashes++;
                sim.Step(default);
            }
            int warmed = dashes;
            long walked = M3TestWorld.Position(s).X.Raw;

            long c0 = GC.GetAllocatedBytesForCurrentThread();
            var control = new byte[32768];
            long controlBytes = GC.GetAllocatedBytesForCurrentThread() - c0;
            GC.KeepAlive(control);
            Assert.That(controlBytes, Is.GreaterThanOrEqualTo(32768), "the counter works");

            AllocationMeter.Stabilize();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1200; i++)
            {
                if (dash && locomotion.TryDash(unit, (dashes & 1) == 0 ? up : down)) dashes++;
                sim.Step(default);
            }
            long used = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(warmed, Is.EqualTo(dash ? 5 : 0), "warm-up dashes");
            Assert.That(walked, Is.GreaterThan(SimFix.FromInt(20).Raw), "the order walks");
            Assert.That(dashes, Is.EqualTo(dash ? 25 : 0), "window dashes");
            Assert.That(used, Is.Zero, "bytes over 1200 ticks with dashes");
        }
    }
}
#endif
