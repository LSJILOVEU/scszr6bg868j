using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class Wp22HeightFieldTests
    {
        private const long S = SimFix.Scale;

        private static HeightField Field(int w, int d, int[] pts, long ox = 0, long oz = 0)
            => new HeightField(w, d, SimFix.FromInt(ox), SimFix.FromInt(oz), pts);

        private static int[] RandomPoints(Random rng, int count, int limit)
        {
            var p = new int[count];
            for (int i = 0; i < count; i++) p[i] = rng.Next(-limit, limit);
            return p;
        }

        [Test]
        public void HandWorkedExample_DiagonalAndBothSharedEdges()
        {
            // Two cells side by side; left cell (h00,h10,h01,h11) = (-2,7,-5,3) raw, right cell shares x=1 and adds h20, h21.
            HeightField f = Field(2, 1, new[] { -2, 7, 11, -5, 3, 13 });
            long t = S / 4;
            Assert.That(f.TryHeightAt(SimFix.FromRaw(t), SimFix.FromRaw(t), out SimFix diagonal), Is.True);
            Assert.That(diagonal.Raw, Is.EqualTo(-1));                       // floor(-3/4), not the truncated 0
            // x edge x = 1 m, z = S/4: the right cell at u = 0 (branch B) gives floor(7 + (3-7)/4) = 6.
            Assert.That(f.TryHeightAt(SimFix.FromRaw(S), SimFix.FromRaw(t), out SimFix xEdge), Is.True);
            Assert.That(xEdge.Raw, Is.EqualTo(6));
            // z edge z = 1 m needs a second row: a 1 x 2 field with the same corner heights bottom-up.
            HeightField g = Field(1, 2, new[] { -2, 7, -5, 3, 4, 20 });
            Assert.That(g.TryHeightAt(SimFix.FromRaw(t), SimFix.FromRaw(S), out SimFix zEdge), Is.True);
            Assert.That(zEdge.Raw, Is.EqualTo(-3));                          // floor(-5 + (3-(-5))/4) = -3
        }

        [Test]
        public void VertexRaw_ReadsEveryGridPointIncludingTheOuterRing()
        {
            var rng = new Random(7);
            int[] pts = RandomPoints(rng, 6 * 4, 1 << 28);
            HeightField f = Field(5, 3, pts);
            for (int j = 0; j <= 3; j++)
                for (int i = 0; i <= 5; i++) Assert.That(f.VertexRaw(i, j), Is.EqualTo(pts[j * 6 + i]));
            Assert.Throws<ArgumentOutOfRangeException>(() => f.VertexRaw(6, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => f.VertexRaw(0, -1));
            Assert.That(f.PointsX, Is.EqualTo(6)); Assert.That(f.PointsZ, Is.EqualTo(4));
        }

        [Test]
        public void HeightAt_MatchesIndependentOracle_OnTenThousandRandomPoints()
        {
            var rng = new Random(20261104);
            const int w = 9, d = 7;
            int[] pts = RandomPoints(rng, (w + 1) * (d + 1), (1 << 29) - 1);
            HeightField f = Field(w, d, pts, -4, -3);
            for (int n = 0; n < 10000; n++)
            {
                long u = n % 7 == 0 ? 0 : rng.Next(0, (int)S), v = n % 11 == 0 ? 0 : n % 13 == 0 ? u : rng.Next(0, (int)S);
                int cx = rng.Next(0, w), cz = rng.Next(0, d);
                long x = (-4 + cx) * S + u, z = (-3 + cz) * S + v;
                int r = cz * (w + 1) + cx;
                long expected = Wp22Oracle.HeightAt(pts[r], pts[r + 1], pts[r + w + 1], pts[r + w + 2], u, v);
                Assert.That(f.TryHeightAt(SimFix.FromRaw(x), SimFix.FromRaw(z), out SimFix h), Is.True);
                Assert.That(h.Raw, Is.EqualTo(expected), "cell " + cx + "," + cz + " u=" + u + " v=" + v);
            }
        }

        [Test]
        public void HeightAt_SharedEdges_AgreeFromBothNeighbours()
        {
            var rng = new Random(99);
            const int w = 6, d = 6;
            int[] pts = RandomPoints(rng, (w + 1) * (d + 1), 1 << 28);
            HeightField f = Field(w, d, pts);
            for (int n = 0; n < 1000; n++)
            {
                bool xEdge = (n & 1) == 0;
                int cx = rng.Next(1, w), cz = rng.Next(1, d);
                long along = rng.Next(0, (int)S);
                // The edge point belongs to the upper / right cell for the production sample (half-open);
                // the oracle evaluates the lower / left neighbour at its closed far edge.
                long x = xEdge ? cx * S : cx * S + along, z = xEdge ? cz * S + along : cz * S;
                f.TryHeightAt(SimFix.FromRaw(x), SimFix.FromRaw(z), out SimFix produced);
                int lx = xEdge ? cx - 1 : cx, lz = xEdge ? cz : cz - 1;
                int r = lz * (w + 1) + lx;
                long other = Wp22Oracle.HeightAt(pts[r], pts[r + 1], pts[r + w + 1], pts[r + w + 2], xEdge ? S : along, xEdge ? along : S);
                Assert.That(produced.Raw, Is.EqualTo(other), (xEdge ? "x" : "z") + " edge cell " + cx + "," + cz);
            }
        }

        [Test]
        public void HeightAt_Domain_IsHalfOpen()
        {
            HeightField f = Field(4, 3, new int[20], -2, -1);
            Assert.That(f.TryHeightAt(SimFix.FromInt(-2), SimFix.FromInt(-1), out _), Is.True);
            Assert.That(f.TryHeightAt(SimFix.FromRaw(2 * S - 1), SimFix.FromRaw(2 * S - 1), out _), Is.True);
            Assert.That(f.TryHeightAt(SimFix.FromInt(2), SimFix.FromInt(0), out _), Is.False);
            Assert.That(f.TryHeightAt(SimFix.FromInt(0), SimFix.FromInt(2), out _), Is.False);
            Assert.That(f.TryHeightAt(SimFix.FromRaw(-2 * S - 1), SimFix.FromInt(0), out _), Is.False);
            Assert.That(f.TryHeightAt(SimFix.FromInt(0), SimFix.FromRaw(-S - 1), out _), Is.False);
        }

        [Test]
        public void Constructor_RejectsBadInput()
        {
            Assert.Throws<ArgumentException>(() => Field(2, 2, new int[8]));
            Assert.Throws<ArgumentOutOfRangeException>(() => Field(1, 1, new[] { 0, 0, 0, 1 << 29 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => Field(1, 1, new[] { 0, 0, 0, -(1 << 29) }));
            Assert.DoesNotThrow(() => Field(1, 1, new[] { 0, 0, 0, (1 << 29) - 1 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => Field(0, 1, new int[2]));
        }

        [Test]
        public void Digest_ChangesWithAnyPoint()
        {
            int[] a = new int[12];
            ulong baseline = Field(3, 2, a).Digest;
            for (int i = 0; i < a.Length; i++)
            {
                int[] b = (int[])a.Clone(); b[i] = 1;
                Assert.That(Field(3, 2, b).Digest, Is.Not.EqualTo(baseline), "point " + i);
            }
        }

        // ---- slope ----

        private static bool Within(int h00, int h10, int h01, int h11, long num, long den)
            => Field(1, 1, new[] { h00, h10, h01, h11 }).CellWithinSlope(0, 0, num, den);

        [Test]
        public void Slope_ExactlyAtLimitPasses_OneRawOverFails()
        {
            Assert.That(Within(0, (int)(2 * S), 0, (int)(2 * S), 2, 1), Is.True);
            Assert.That(Within(0, (int)(2 * S) + 1, 0, (int)(2 * S), 2, 1), Is.False);
            Assert.That(Within(0, (int)(2 * S), 0, (int)(2 * S) + 1, 2, 1), Is.False);
            // Non-unit denominators: 3/2 and 1500/1000 are the same limit.
            Assert.That(Within(0, (int)(3 * S / 2), 0, (int)(3 * S / 2), 3, 2), Is.True);
            Assert.That(Within(0, (int)(3 * S / 2) + 1, 0, (int)(3 * S / 2), 3, 2), Is.False);
            Assert.That(Within(0, (int)(3 * S / 2), 0, (int)(3 * S / 2), 1500, 1000), Is.True);
            Assert.That(Within(0, (int)(3 * S / 2) + 1, 0, (int)(3 * S / 2), 1500, 1000), Is.False);
        }

        [Test]
        public void Slope_UsesTheWholePlane_NotEachAxialEdge()
        {
            // Both axial differences are exactly 2 m per metre, so each edge passes, but the plane is sqrt(8) > 2.
            int two = (int)(2 * S);
            Assert.That(Within(0, two, two, 2 * two, 2, 1), Is.False);
        }

        [Test]
        public void Slope_EitherTriangleCanFailTheCell()
        {
            int s = (int)S;
            // Only triangle B (u < v) is steep: its b = h01 - h00 is 3 m, while triangle A is flat.
            Assert.That(Within(0, 0, 3 * s, 0, 2, 1), Is.False);
            // Only triangle A is steep: a = h10 - h00 is 3 m.
            Assert.That(Within(0, 3 * s, 0, 3 * s, 2, 1), Is.False);
            Assert.That(Within(0, 0, 0, 0, 0, 1), Is.True);                 // flat passes even with a zero limit
        }

        [Test]
        public void Slope_MatchesCrossProductOracle_OnRandomCellsAndRatios()
        {
            var rng = new Random(31337);
            for (int n = 0; n < 4000; n++)
            {
                int limit = n % 3 == 0 ? 1 << 20 : 3 * (int)S;
                int[] p = RandomPoints(rng, 4, limit);
                long den = rng.Next(1, 4097), num = rng.Next(0, (int)(2 * den) + 1);
                bool expected = Wp22Oracle.CellWithin(p[0], p[1], p[2], p[3], num, den);
                Assert.That(Within(p[0], p[1], p[2], p[3], num, den), Is.EqualTo(expected), string.Join(",", p) + " " + num + "/" + den);
            }
        }

        [Test]
        public void Slope_NearTheWidthLimits_DoesNotOverflow()
        {
            int top = (1 << 29) - 1;
            Assert.That(Within(-top, top, -top, top, 2, 1), Is.False);
            Assert.That(Within(-top, top, top, -top, 2, 4096 / 2), Is.False);
            Assert.That(Within(top, -top, -top, top, 8192, 4096), Is.False);
            Assert.That(Within(top, top, top, top, 8192, 4096), Is.True);
            Assert.Throws<ArgumentOutOfRangeException>(() => Within(0, 0, 0, 0, 3, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => Within(0, 0, 0, 0, 1, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => Within(0, 0, 0, 0, 1, 4097));
            Assert.Throws<ArgumentOutOfRangeException>(() => Within(0, 0, 0, 0, -1, 1));
        }

        [Test]
        public void HeightAt_DoesNotAllocate()
        {
            HeightField f = Field(8, 8, RandomPoints(new Random(1), 81, 1 << 20));
            long sum = 0;
            for (int i = 0; i < 1000; i++) { f.TryHeightAt(SimFix.FromRaw(i * 37 % (8 * S)), SimFix.FromRaw(i * 91 % (8 * S)), out SimFix h); sum += h.Raw; }
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100000; i++) { f.TryHeightAt(SimFix.FromRaw(i * 37 % (8 * S)), SimFix.FromRaw(i * 91 % (8 * S)), out SimFix h); sum += h.Raw; }
            long used = GC.GetAllocatedBytesForCurrentThread() - before;
            long controlBefore = GC.GetAllocatedBytesForCurrentThread();
            var keep = new List<byte[]>();
            for (int i = 0; i < 100; i++) keep.Add(new byte[1024]);
            long control = GC.GetAllocatedBytesForCurrentThread() - controlBefore;
            if (control < 100 * 1024) Assert.Inconclusive("The allocation counter did not register a positive control.");
            Assert.That(used, Is.Zero, "sum " + sum);
        }
    }
}
