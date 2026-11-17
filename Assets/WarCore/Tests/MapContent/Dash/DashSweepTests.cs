using NUnit.Framework;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests.Dash
{
    // M7a acceptance 1: a dash at ten times the unit's own step (2 m per tick against 0.2 m) goes through neither a 1 m wall
    // nor a standing unit, though in both fixtures a whole dash step would jump the obstacle with both end points clear.
    public sealed class DashSweepTests
    {
        // Wall cells x = 20 (one metre thick, z 0..23, so no slide can get round it). Unit 1 at (15.5, 12.5) dashes +X:
        // 17.5 and 19.5 are clear (19.75 m is the body's front, the wall face is at 20 m); the step to 21.5 would jump the wall
        // (its end is 1.25 m past the far face) and every axis fallback is blocked, so the dash ends there after two steps.
        [Test]
        public void TenTimesSpeed_DoesNotPassAThinWall()
        {
            SimSession s = DashWorlds.World(new[] { DashWorlds.Dasher(1, 15500, 12500) }, DashWorlds.Map((20, 0, 1, 24)));
            Assert.That(DashWorlds.Dash(s, DashWorlds.PlusX), Is.True);
            var track = DashWorlds.Track(s, DashWorlds.Ticks);
            long[] expectedX = { 15 * DashWorlds.S + DashWorlds.S / 2, 17 * DashWorlds.S + DashWorlds.S / 2, 19 * DashWorlds.S + DashWorlds.S / 2 };
            for (int k = 0; k < track.Count; k++)
            {
                long x = track[k].X.Raw;
                Assert.That(x + DashWorlds.S / 4, Is.LessThanOrEqualTo(20 * DashWorlds.S), "body front stays before the wall, step " + k);
                Assert.That(x, Is.EqualTo(expectedX[System.Math.Min(k, 2)]), "x after step " + k);
                Assert.That(track[k].Z, Is.EqualTo(SimFix.FromInt(12) + SimFix.Ratio(1, 2)), "z after step " + k);
            }
            Assert.That(DashWorlds.State(s).Ticks, Is.EqualTo(2), "the blocked third step ended the dash after two steps");
            Assert.That(DashWorlds.Loco(s).IsDashing(M3TestWorld.Unit(s)), Is.False);
        }

        // Unit 2 (other team, idle, no pass rule) stands at (10.5, 12.5). Unit 1 at (5.5, 12.5) dashes +X: 7.5 and 9.5 are clear;
        // the step 9.5 -> 11.5 has both end points 1.0 m from unit 2 (clear of the 0.5 m sum of radii) but passes through its
        // centre, so only the sweep along the step stops it.
        [Test]
        public void TenTimesSpeed_DoesNotPassAUnit()
        {
            SimSession s = DashWorlds.World(new[] { DashWorlds.Dasher(1, 5500, 12500), DashWorlds.Plain(2, 10500, 12500) }, DashWorlds.Map());
            SimVec3 other = M3TestWorld.Position(s, 2);
            Assert.That(DashWorlds.Dash(s, DashWorlds.PlusX), Is.True);
            var track = DashWorlds.Track(s, DashWorlds.Ticks);
            for (int k = 1; k < track.Count; k++)
            {
                Assert.That(DashWorlds.SegmentDistance(track[k - 1], track[k], other), Is.GreaterThanOrEqualTo(0.5),
                    "the step " + k + " keeps the summed radii clear along its whole path");
                Assert.That(track[k].X.Raw, Is.LessThan(other.X.Raw), "unit 1 stays on its side, step " + k);
            }
            Assert.That(track[DashWorlds.Ticks].X.Raw, Is.EqualTo(9 * DashWorlds.S + DashWorlds.S / 2));
            Assert.That(M3TestWorld.Position(s, 2), Is.EqualTo(other), "the standing unit is not pushed");
            Assert.That(DashWorlds.State(s).Ticks, Is.EqualTo(2));
        }

        // Control: with nothing in the way the same dash covers its whole 20 m in ten steps of exactly 2 m.
        [Test]
        public void OpenGround_CoversTheWholeDistance()
        {
            SimSession s = DashWorlds.World(new[] { DashWorlds.Dasher(1, 5500, 12500) }, DashWorlds.Map());
            Assert.That(DashWorlds.Dash(s, DashWorlds.PlusX), Is.True);
            var track = DashWorlds.Track(s, DashWorlds.Ticks + 2);
            for (int k = 1; k <= DashWorlds.Ticks; k++)
                Assert.That(track[k].X.Raw - track[k - 1].X.Raw, Is.EqualTo(2 * DashWorlds.S), "step " + k);
            Assert.That(track[DashWorlds.Ticks + 2], Is.EqualTo(track[DashWorlds.Ticks]), "no order: the unit stands after the dash");
            Assert.That(track[DashWorlds.Ticks].X.Raw, Is.EqualTo(25 * DashWorlds.S + DashWorlds.S / 2));
        }
    }
}
