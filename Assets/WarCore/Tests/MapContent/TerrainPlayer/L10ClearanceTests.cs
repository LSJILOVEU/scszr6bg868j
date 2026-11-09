using NUnit.Framework;
using WarCore.Sim;
using static WarCore.Tests.L10Fixture;

namespace WarCore.Tests
{
    // L10 (4): on terrain the player keeps the flat clearance rule (walls, blocked cells by the 3x3 nearest-point test, units).
    public sealed class L10ClearanceTests
    {
        private const long Radius = S / 4;

        // The squared distance from (x, z) to the nearest blocked cell among the 3x3 around it (map edges count as blocked).
        private static long NearestBlocked2(M6World w, SimVec3 p)
        {
            long best = long.MaxValue;
            int cx = (int)(p.X.Raw >> 16), cz = (int)(p.Z.Raw >> 16);
            for (int z = cz - 1; z <= cz + 1; z++)
                for (int x = cx - 1; x <= cx + 1; x++)
                {
                    if (!w.Session.BlockedCell(x, z) && x >= 0 && z >= 0 && x < w.Session.Map.Width && z < w.Session.Map.Depth) continue;
                    long left = x * S, bottom = z * S;
                    long dx = System.Math.Max(0, System.Math.Max(left - p.X.Raw, p.X.Raw - (left + S)));
                    long dz = System.Math.Max(0, System.Math.Max(bottom - p.Z.Raw, p.Z.Raw - (bottom + S)));
                    best = System.Math.Min(best, dx * dx + dz * dz);
                }
            return best;
        }

        [Test]
        public void L10Clearance_TheCliffBandStopsThePlayer()
        {
            var w = Demo(20500, 20500);
            SimVec3[] p = Walk(w, 0, 1000, 30);
            Assert.That(p[29].Z.Raw, Is.EqualTo(1422130));
            for (int t = 9; t < 30; t++) Assert.That(p[t], Is.EqualTo(p[29]), "still from tick " + (t + 1));
            foreach (SimVec3 q in p)
            {
                Assert.That(OnGround(w, q), Is.True);
                Assert.That(NearestBlocked2(w, q), Is.GreaterThanOrEqualTo((Radius - 1) * (Radius - 1)));
            }
        }

        [Test]
        public void L10Clearance_TheWallStopsThePlayer()
        {
            var w = Demo(4500, 10500);
            SimVec3[] p = Walk(w, 1000, 0, 20);
            Assert.That(p[19].X.Raw, Is.EqualTo(373554));
            for (int t = 9; t < 20; t++) Assert.That(p[t], Is.EqualTo(p[19]));
        }

        // An enemy and then an idle teammate 0.6 m ahead on the Z slope: neither lets the controlled unit through
        // (TeamIdle never applies to the controlled unit).
        [TestCase(2), TestCase(1)]
        public void L10Clearance_UnitsBlockOnTheSlope(int team)
        {
            var w = Demo(3500, 4500, map: ZSlope(), extra: new[] { U(3, 3500, 5100, team: team) });
            SimVec3[] p = Walk(w, 0, 1000, 20);
            SimVec3 other = At(w, 3);
            foreach (SimVec3 q in p)
            {
                long dx = q.X.Raw - other.X.Raw, dz = q.Z.Raw - other.Z.Raw;
                Assert.That(dx * dx + dz * dz, Is.GreaterThanOrEqualTo((2 * Radius) * (2 * Radius)));
                Assert.That(OnGround(w, q), Is.True);
            }
        }

        // Diagonal at a corner of the lone blocked cell (5, 10): the whole step touches the corner, X only and Z only both pass;
        // the X-only point is the one taken.
        [Test]
        public void L10Clearance_XOnlyComesBeforeZOnly()
        {
            var w = Demo(4700, 9700, map: ZSlope());
            SimVec3 before = At(w);
            SimVec3 after = Walk(w, 707, 707, 1)[0];
            Assert.That(after.Z.Raw, Is.EqualTo(before.Z.Raw), "Z unchanged");
            Assert.That(after.X.Raw, Is.GreaterThan(before.X.Raw), "X moved");
            Assert.That(OnGround(w, after), Is.True);
        }

        // K3 on terrain: a short step (0.2 m, under the 0.25 m radius) is judged by its end alone. This one passes the corner of
        // the single-cell wall at (5, 10) on the Z slope at about 0.235 m in the middle while both ends are about 0.255 m from
        // it (the flat ControlledSweepTests geometry moved 5 m in -X); a swept test would refuse it.
        [Test]
        public void L10Clearance_AShortStepGrazesACornerByItsEnd()
        {
            var w = Demo(4763, 9905, map: ZSlope());
            SimVec3 before = At(w);
            SimVec3[] p = Walk(w, 1000, -1000, 1);
            Assert.That(p[0].X.Raw, Is.GreaterThan(before.X.Raw), "moved right");
            Assert.That(p[0].Z.Raw, Is.LessThan(before.Z.Raw), "and down the slope");
            Assert.That(OnGround(w, p[0]), Is.True);
        }
    }
}
