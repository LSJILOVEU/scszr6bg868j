using NUnit.Framework;
using WarCore.Interim;
using WarCore.Sim;
using static WarCore.Tests.L10Fixture;

namespace WarCore.Tests
{
    // L10 (2): the controlled unit stands on the height field after every step, whichever candidate the step took.
    public sealed class L10GroundTests
    {
        private static void AllOnGround(M6World w, SimVec3[] points, int minMoves, string what)
        {
            SimVec3 previous = default;
            int moves = 0;
            for (int t = 0; t < points.Length; t++)
            {
                Assert.That(OnGround(w, points[t]), Is.True, what + " tick " + (t + 1));
                if (t > 0 && (points[t].X.Raw != previous.X.Raw || points[t].Z.Raw != previous.Z.Raw)) moves++;
                previous = points[t];
            }
            Assert.That(moves, Is.GreaterThanOrEqualTo(minMoves), what + " moved");
        }

        [Test]
        public void L10Ground_UpTheSlopeOnGrass()
        {
            var w = Demo(12500, 6500);
            SimVec3 start = At(w);
            SimVec3[] p = Walk(w, 1000, 0, 10);
            AllOnGround(w, p, 9, "up");
            SimVec3 before = start;
            foreach (SimVec3 now in p)
            {
                Assert.That(now.X.Raw - before.X.Raw, Is.EqualTo(Grass6000));
                Assert.That(now.Y.Raw - before.Y.Raw, Is.InRange(6553, 6554));
                before = now;
            }
            Assert.That(new[] { p[0].Y.Raw, p[1].Y.Raw, p[2].Y.Raw }, Is.EqualTo(new long[] { 88473, 95027, 101580 }));
        }

        [Test]
        public void L10Ground_DownTheSlope()
        {
            var w = Demo(21500, 6500);
            SimVec3[] p = Walk(w, -1000, 0, 10);
            AllOnGround(w, p, 9, "down");
            Assert.That(p[9].Y.Raw, Is.LessThan(p[0].Y.Raw));
        }

        [Test]
        public void L10Ground_Diagonal()
        {
            var w = Demo(12500, 6500);
            SimVec3 before = At(w);
            SimVec3[] p = Walk(w, 707, 707, 10);
            AllOnGround(w, p, 9, "diagonal");
            foreach (SimVec3 now in p)
            {
                Assert.That((now.X.Raw - before.X.Raw, now.Z.Raw - before.Z.Raw), Is.EqualTo((9266L, 9266L)));
                before = now;
            }
        }

        [TestCase(1000), TestCase(-1000)]
        public void L10Ground_AlongAZSlope(int z)
        {
            var w = Demo(3500, 7500, map: ZSlope());
            SimVec3[] p = Walk(w, 0, z, 10);
            AllOnGround(w, p, 9, "z slope " + z);
            Assert.That(p[9].Y.Raw, z > 0 ? Is.GreaterThan(p[0].Y.Raw) : Is.LessThan(p[0].Y.Raw));
        }

        // Diagonal under the cliff band (cells z = 22 are too steep): Z is refused, the X-only candidate moves up the X slope.
        [Test]
        public void L10Ground_SlideAlongTheCliffTakesXOnly()
        {
            var w = Demo(15500, 21500);
            SimVec3[] p = Walk(w, 707, 707, 20);
            AllOnGround(w, p, 15, "cliff slide");
            int xOnly = 0;
            for (int t = 1; t < p.Length; t++)
                if (p[t].Z.Raw == p[t - 1].Z.Raw && p[t].X.Raw != p[t - 1].X.Raw && p[t].Y.Raw != p[t - 1].Y.Raw) xOnly++;
            Assert.That(xOnly, Is.GreaterThanOrEqualTo(5), "X-only steps that changed height");
        }

        // Diagonal into the wall strip at x = 10 on the Z slope: X is refused, the Z-only candidate climbs.
        [Test]
        public void L10Ground_SlideAlongAWallTakesZOnly()
        {
            var w = Demo(9300, 5500, map: ZSlope());
            SimVec3[] p = Walk(w, 707, 707, 20);
            AllOnGround(w, p, 15, "wall slide");
            int zOnly = 0;
            for (int t = 1; t < p.Length; t++)
                if (p[t].X.Raw == p[t - 1].X.Raw && p[t].Z.Raw != p[t - 1].Z.Raw && p[t].Y.Raw != p[t - 1].Y.Raw) zOnly++;
            Assert.That(zOnly, Is.GreaterThanOrEqualTo(5), "Z-only steps that changed height");
            Assert.That(p[19].X.Raw, Is.LessThanOrEqualTo(10 * S - S / 4 + 1), "never into the wall");
        }

        [Test]
        public void L10Ground_SurfacesOnlyKeepsY_HeightsOnlyFollowsTheGround()
        {
            var flat = Demo(12500, 6500, map: SurfacesOnly());
            long y0 = At(flat).Y.Raw;
            SimVec3[] a = Walk(flat, 1000, 0, 10);
            Assert.That(a[9].X.Raw - a[0].X.Raw, Is.EqualTo(9 * Grass6000));
            foreach (SimVec3 p in a) Assert.That(p.Y.Raw, Is.EqualTo(y0));

            var heights = Demo(12500, 16500, map: HeightsOnly());
            SimVec3 before = At(heights);
            SimVec3[] b = Walk(heights, 1000, 0, 10);
            AllOnGround(heights, b, 9, "heights only");
            Assert.That(b[0].X.Raw - before.X.Raw, Is.EqualTo(Grass6000), "no surface table: multiplier 1000 even on the road row");
        }

        [Test]
        public void L10Ground_WithFacingTurnsAlongTheStep()
        {
            var w = Demo(12500, 6500, facing: true);
            SimVec3[] p = Walk(w, 1000, 0, 5);
            AllOnGround(w, p, 4, "facing");
            UnitFacing f = w.Read<UnitFacing>(1);
            Assert.That(f.X.Raw, Is.GreaterThan(0));
            Assert.That(f.Z.Raw, Is.EqualTo(0));
        }
    }
}
