using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;
using WarCore.Tests.Dash;
using static WarCore.Tests.DashT.DashTWorlds;

namespace WarCore.Tests.DashT
{
    // DASH-T acceptance 5: the ML-8 demo house (PlateDemoMap). The stair (layer 1) runs along x on row z = 9 over cells 10..13,
    // its top (x - 10 m) * 3/4 (0.75 m a cell); its foot faces the ground cell (9, 9), its top the first floor's cell (14, 9)
    // (layer 2, flat at 3 m). The house's east wall stands on x 15..16. The dasher starts on the ground at (9.5, 9.5).
    // A dash of 2 m a tick spans two cells a tick: in one piece TryEnter refuses it (more than one cell per axis) and the dash
    // would end on its first tick (mutation dash-t-no-split); in pieces of 0.25 m (m = 131072 / 16384 = 8) it walks on.
    public sealed class DashTPlateTests
    {
        private const long M = SimFix.Scale;

        private static long StairTop(long x) => (x - 10 * M) * 3 / 4;

        // 4 m in 2 ticks along +X: 131072 a tick. Tick 1 ends at x 11.5 on the stair (the seam at x = 10 is crossed by the third
        // piece), y = StairTop(753664) = 73728; tick 2 at x 13.5, y = 172032.
        [Test]
        public void UpTheStairInTwoTicks()
        {
            SimSession s = House(new[] { Dasher(1, 9500, 9500, distanceMm: 4000, ticks: 2, cooldown: 10) });
            Assert.That(Layer(s), Is.Zero);
            Assert.That(DashWorlds.Dash(s, DashWorlds.PlusX), Is.True);
            s.StepOnce();
            Assert.That(M3TestWorld.Position(s), Is.EqualTo(new SimVec3(SimFix.FromRaw(753664), SimFix.FromRaw(73728), SimFix.FromRaw(622592))));
            Assert.That(Layer(s), Is.EqualTo(1), "on the stair after the seam");
            s.StepOnce();
            Assert.That(M3TestWorld.Position(s), Is.EqualTo(new SimVec3(SimFix.FromRaw(884736), SimFix.FromRaw(StairTop(884736)), SimFix.FromRaw(622592))));
            Assert.That(StairTop(884736), Is.EqualTo(172032));
            Assert.That(Layer(s), Is.EqualTo(1));
            Assert.That(DashWorlds.State(s).Ticks, Is.EqualTo(2), "not ended early");
        }

        // 6 m in 3 ticks: ticks 1 and 2 as above; tick 3's pieces from x 13.5: 13.75 (stair, y 184320), 14.0 (the seam onto the
        // first floor, layer 2, y 3 m), 14.25, 14.5, 14.75; the next, x 15.0, would put the unit's centre on the east wall's face:
        // refused. The dash ends after step 2 with the unit at x 14.75 on the first floor.
        [Test]
        public void OntoTheFloorAndStopsAtTheWall()
        {
            SimSession s = House(new[] { Dasher(1, 9500, 9500, distanceMm: 6000, ticks: 3, cooldown: 10) });
            Assert.That(DashWorlds.Dash(s, DashWorlds.PlusX), Is.True);
            List<SimVec3> track = DashWorlds.Track(s, 4);
            Assert.That(track[3], Is.EqualTo(new SimVec3(SimFix.FromRaw(966656), SimFix.FromRaw(3 * M), SimFix.FromRaw(622592))));
            Assert.That(track[4], Is.EqualTo(track[3]));
            Assert.That(Layer(s), Is.EqualTo(2), "on the first floor after the stair's top seam");
            Assert.That(DashWorlds.State(s).Ticks, Is.EqualTo(2), "ended after step 2");
        }

        // Down again: from x 13.5 on the stair along -X, 4 m in 2 ticks: 11.5 on the stair, then 9.5 on the ground (layer 0).
        [Test]
        public void DownTheStairToTheGround()
        {
            SimSession s = House(new[] { Dasher(1, 9500, 9500, distanceMm: 4000, ticks: 2, cooldown: 2) });
            Assert.That(DashWorlds.Dash(s, DashWorlds.PlusX), Is.True);
            s.StepOnce(); s.StepOnce();
            Assert.That(DashWorlds.Dash(s, DashWorlds.MinusX), Is.True);
            s.StepOnce();
            Assert.That(M3TestWorld.Position(s), Is.EqualTo(new SimVec3(SimFix.FromRaw(753664), SimFix.FromRaw(73728), SimFix.FromRaw(622592))));
            Assert.That(Layer(s), Is.EqualTo(1));
            s.StepOnce();
            Assert.That(M3TestWorld.Position(s), Is.EqualTo(new SimVec3(SimFix.FromRaw(622592), default, SimFix.FromRaw(622592))));
            Assert.That(Layer(s), Is.Zero, "back on the ground");
        }
    }
}
