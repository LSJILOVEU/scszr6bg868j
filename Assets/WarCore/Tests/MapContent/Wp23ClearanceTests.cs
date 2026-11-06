using System;
using System.Numerics;
using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    // T1a-2a acceptance 8: NavGrid.SegmentClear is exact. The oracle below shares no code with it: it works in BigInteger
    // and finds the distance between segment and rectangle as the smaller of the vertex-to-other-shape distances.
    public sealed class Wp23ClearanceTests
    {
        private const long S = SimFix.Scale;
        private static SimVec3 P(long x, long z) => new SimVec3(SimFix.FromRaw(x), default, SimFix.FromRaw(z));
        private static SimFix R(long raw) => SimFix.FromRaw(raw);

        // 8 x 8 cells; `blocked` lists cells; origin shifts everything (also to negative coordinates).
        private static NavGrid Grid(long originCellsX, long originCellsZ, params (int x, int z)[] blocked)
        {
            const int n = 8;
            var bits = new byte[(n * n + 7) / 8];
            for (int k = 0; k < n * n; k++) bits[k >> 3] |= (byte)(1 << (k & 7));
            foreach (var (x, z) in blocked) { int k = z * n + x; bits[k >> 3] &= (byte)~(1 << (k & 7)); }
            return new NavGrid(n, n, SimFix.FromRaw(originCellsX * S), SimFix.FromRaw(originCellsZ * S), bits);
        }

        // ---- the oracle ----

        private static BigInteger Cross(BigInteger ax, BigInteger az, BigInteger bx, BigInteger bz) => ax * bz - az * bx;

        private static bool OnSegment(BigInteger ax, BigInteger az, BigInteger bx, BigInteger bz, BigInteger px, BigInteger pz) =>
            BigInteger.Min(ax, bx) <= px && px <= BigInteger.Max(ax, bx) && BigInteger.Min(az, bz) <= pz && pz <= BigInteger.Max(az, bz);

        private static bool SegmentsMeet(BigInteger ax, BigInteger az, BigInteger bx, BigInteger bz, BigInteger cx, BigInteger cz, BigInteger dx, BigInteger dz)
        {
            int o1 = Cross(bx - ax, bz - az, cx - ax, cz - az).Sign, o2 = Cross(bx - ax, bz - az, dx - ax, dz - az).Sign;
            int o3 = Cross(dx - cx, dz - cz, ax - cx, az - cz).Sign, o4 = Cross(dx - cx, dz - cz, bx - cx, bz - cz).Sign;
            if (o1 != o2 && o3 != o4) return true;
            if (o1 == 0 && OnSegment(ax, az, bx, bz, cx, cz)) return true;
            if (o2 == 0 && OnSegment(ax, az, bx, bz, dx, dz)) return true;
            if (o3 == 0 && OnSegment(cx, cz, dx, dz, ax, az)) return true;
            if (o4 == 0 && OnSegment(cx, cz, dx, dz, bx, bz)) return true;
            return false;
        }

        private static BigInteger PointRect2(BigInteger px, BigInteger pz, BigInteger x0, BigInteger z0, BigInteger x1, BigInteger z1)
        {
            BigInteger dx = BigInteger.Max(BigInteger.Zero, BigInteger.Max(x0 - px, px - x1));
            BigInteger dz = BigInteger.Max(BigInteger.Zero, BigInteger.Max(z0 - pz, pz - z1));
            return dx * dx + dz * dz;
        }

        // Is the distance from point to segment strictly below r (r2 = r squared)? Exact, by cross-multiplying the fraction.
        private static bool PointNearSegment(BigInteger px, BigInteger pz, BigInteger ax, BigInteger az, BigInteger bx, BigInteger bz, BigInteger r2)
        {
            BigInteger dx = bx - ax, dz = bz - az, l2 = dx * dx + dz * dz;
            BigInteger wx = px - ax, wz = pz - az, dot = wx * dx + wz * dz;
            if (l2.IsZero || dot <= 0) return wx * wx + wz * wz < r2;
            if (dot >= l2) { BigInteger ex = px - bx, ez = pz - bz; return ex * ex + ez * ez < r2; }
            BigInteger cross = Cross(dx, dz, wx, wz);
            return cross * cross < r2 * l2;
        }

        private static bool OracleClear(NavGrid g, long fx, long fz, long tx, long tz, long r)
        {
            BigInteger r2 = new BigInteger(r) * r;
            long minX = g.OriginX.Raw + r, maxX = g.OriginX.Raw + g.Width * S - r, minZ = g.OriginZ.Raw + r, maxZ = g.OriginZ.Raw + g.Depth * S - r;
            if (fx < minX || fx > maxX || tx < minX || tx > maxX || fz < minZ || fz > maxZ || tz < minZ || tz > maxZ) return false;
            for (int z = 0; z < g.Depth; z++)
                for (int x = 0; x < g.Width; x++)
                {
                    if (g.IsPassable(x, z)) continue;
                    BigInteger x0 = g.OriginX.Raw + x * S, z0 = g.OriginZ.Raw + z * S, x1 = x0 + S, z1 = z0 + S;
                    if (PointRect2(fx, fz, x0, z0, x1, z1) < r2 || PointRect2(tx, tz, x0, z0, x1, z1) < r2) return false;
                    // Segment through the rectangle (an end inside is caught above because the distance is then 0).
                    if (SegmentsMeet(fx, fz, tx, tz, x0, z0, x1, z0) || SegmentsMeet(fx, fz, tx, tz, x1, z0, x1, z1)
                        || SegmentsMeet(fx, fz, tx, tz, x1, z1, x0, z1) || SegmentsMeet(fx, fz, tx, tz, x0, z1, x0, z0)) return false;
                    foreach (var (cx, cz) in new[] { (x0, z0), (x1, z0), (x0, z1), (x1, z1) })
                        if (PointNearSegment(cx, cz, fx, fz, tx, tz, r2)) return false;
                }
            return true;
        }

        // ---- fixed cases ----

        // Counterexamples: every sampled point is clear, the middle of the segment is not. The blocked cell is in
        // the map interior (cell 3,3), so a "outside the map" answer cannot make these pass by accident.
        [TestCase(0L), TestCase(-4L)]
        public void SampledPointsClear_ButTheSegmentCutsTheCorner_IsBlocked(long origin)
        {
            NavGrid g = Grid(origin, origin, (3, 3));
            long ox = (origin + 3) * S, oz = (origin + 3) * S;
            Assert.That(g.SegmentClear(P(ox - 17000, oz - 6000), P(ox - 17000 + 23170, oz - 6000 - 23170), R(16384)), Is.False);
            Assert.That(g.SegmentClear(P(ox - 14000, oz - 9000), P(ox - 14000 + 20000, oz - 9000 - 20000), R(16384)), Is.False);
            // The same moves one cell-width away from the cell are clear.
            Assert.That(g.SegmentClear(P(ox - 17000 - S, oz - 6000), P(ox - 17000 - S + 23170, oz - 6000 - 23170), R(16384)), Is.True);
        }

        [TestCase(0L), TestCase(-4L)]
        public void BoundaryTable(long origin)
        {
            NavGrid g = Grid(origin, origin, (3, 3));
            long x0 = (origin + 3) * S, z0 = (origin + 3) * S, x1 = x0 + S, r = 16384;
            // Exactly r away, moving away or along the edge: clear.
            Assert.That(g.SegmentClear(P(x0 - r, z0 + 100), P(x0 - r - 5000, z0 + 100), R(r)), Is.True);
            Assert.That(g.SegmentClear(P(x0 - r, z0 + 100), P(x0 - r, z0 + 20000), R(r)), Is.True);
            // No displacement, exactly r away: clear. One raw closer: blocked.
            Assert.That(g.SegmentClear(P(x0 - r, z0 + 100), P(x0 - r, z0 + 100), R(r)), Is.True);
            Assert.That(g.SegmentClear(P(x0 - r + 1, z0 + 100), P(x0 - r + 1, z0 + 100), R(r)), Is.False);
            Assert.That(g.SegmentClear(P(x0 - r, z0 + 100), P(x0 - r + 1, z0 + 100), R(r)), Is.False, "the far end invades by one raw");
            // A segment entering the cell, and one wholly inside it.
            Assert.That(g.SegmentClear(P(x0 - 10000, z0 + 30000), P(x0 + 20000, z0 + 30000), R(r)), Is.False);
            Assert.That(g.SegmentClear(P(x0 + 1000, z0 + 1000), P(x0 + 2000, z0 + 2000), R(r)), Is.False);
            // A corner graze: |cross|^2 == r^2 * L^2 exactly clear, one raw further in blocked.
            Assert.That(g.SegmentClear(P(x0 - 12000, z0 - 11480), P(x0 - 8000, z0 - 14480), R(r)), Is.True);
            Assert.That(g.SegmentClear(P(x0 - 11999, z0 - 11480), P(x0 - 7999, z0 - 14480), R(r)), Is.False);
            // Map edge: centre exactly min + r / max - r is clear, one raw outward is not.
            long minX = g.OriginX.Raw + r, maxX = g.OriginX.Raw + 8 * S - r;
            Assert.That(g.SegmentClear(P(minX, minX), P(minX + 3000, minX), R(r)), Is.True);
            Assert.That(g.SegmentClear(P(minX - 1, minX), P(minX + 3000, minX), R(r)), Is.False);
            Assert.That(g.SegmentClear(P(maxX, maxX), P(maxX - 3000, maxX), R(r)), Is.True);
            Assert.That(g.SegmentClear(P(maxX + 1, maxX), P(maxX - 3000, maxX), R(r)), Is.False);
            Assert.That(g.SegmentClear(P(g.OriginX.Raw - 5 * S, g.OriginZ.Raw), P(g.OriginX.Raw - 5 * S, g.OriginZ.Raw), R(r)), Is.False, "far outside the map");
        }

        [Test]
        public void InputDomain_IsEnforced()
        {
            NavGrid g = Grid(0, 0, (3, 3));
            SimVec3 a = P(S, S);
            Assert.Throws<ArgumentOutOfRangeException>(() => g.SegmentClear(a, a, R(0)));
            Assert.Throws<ArgumentOutOfRangeException>(() => g.SegmentClear(a, a, R(-5)));
            Assert.Throws<ArgumentOutOfRangeException>(() => g.SegmentClear(a, a, R(16385)));
            Assert.Throws<ArgumentOutOfRangeException>(() => g.SegmentClear(a, a, R(33000)), "the 33000 counterexample is outside the domain");
            Assert.DoesNotThrow(() => g.SegmentClear(a, a, R(16384)));
            Assert.DoesNotThrow(() => g.SegmentClear(a, P(S + 32768, S - 32768), R(1)));
            Assert.Throws<ArgumentOutOfRangeException>(() => g.SegmentClear(a, P(S + 32769, S), R(100)));
            Assert.Throws<ArgumentOutOfRangeException>(() => g.SegmentClear(a, P(S, S - 32769), R(100)));
        }

        // Extreme coordinates must be refused by range, never by wrapping (a subtraction of long.MinValue and long.MaxValue
        // wraps in an unchecked long) and never by OverflowException.
        [Test]
        public void ExtremeCoordinates_AreRefusedByRange_NotByWrapping()
        {
            NavGrid g = Grid(0, 0, (3, 3));
            long[] values = { long.MinValue, long.MinValue + 1, -S, -1, 0, 1, 3 * S, long.MaxValue - 1, long.MaxValue };
            int thrown = 0, answered = 0;
            foreach (long fx in values) foreach (long tx in values) foreach (long fz in new[] { long.MinValue, 0, long.MaxValue }) foreach (long tz in new[] { long.MinValue, 0, long.MaxValue })
            {
                bool outOfDomain = BigInteger.Abs(new BigInteger(tx) - fx) > 32768 || BigInteger.Abs(new BigInteger(tz) - fz) > 32768;
                if (outOfDomain)
                {
                    Assert.Throws<ArgumentOutOfRangeException>(() => g.SegmentClear(P(fx, fz), P(tx, tz), R(100)), $"{fx},{fz} -> {tx},{tz}");
                    thrown++;
                }
                else
                {
                    Assert.That(g.SegmentClear(P(fx, fz), P(tx, tz), R(100)), Is.EqualTo(OracleClear(g, fx, fz, tx, tz, 100)), $"{fx},{fz} -> {tx},{tz}");
                    answered++;
                }
            }
            Assert.That(thrown, Is.GreaterThan(100));
            Assert.That(answered, Is.GreaterThan(5));
        }

        // ---- randomised against the oracle ----

        [Test]
        public void RandomSegments_AgreeWithTheExactOracle()
        {
            var random = new Random(23);
            int clear = 0, blocked = 0;
            for (int world = 0; world < 40; world++)
            {
                var cells = new System.Collections.Generic.List<(int, int)>();
                int count = 1 + random.Next(10);
                for (int i = 0; i < count; i++) cells.Add((random.Next(8), random.Next(8)));
                long origin = random.Next(3) == 0 ? 0 : random.Next(-20, 20);
                NavGrid g = Grid(origin, random.Next(3) == 0 ? 0 : random.Next(-20, 20), cells.ToArray());
                for (int n = 0; n < 2500; n++)
                {
                    long r = random.Next(4) == 0 ? 16384 : random.Next(4) == 0 ? 1 + random.Next(64) : 1 + random.Next(16384);
                    long minX = g.OriginX.Raw, minZ = g.OriginZ.Raw;
                    long fx, fz;
                    if (random.Next(2) == 0)
                    {
                        // Near a cell corner or edge of a random blocked cell, to hit the boundary cases.
                        var (cx, cz) = cells[random.Next(cells.Count)];
                        fx = minX + (cx + random.Next(2)) * S + random.Next(-2 * (int)r - 3000, 2 * (int)r + 3000);
                        fz = minZ + (cz + random.Next(2)) * S + random.Next(-2 * (int)r - 3000, 2 * (int)r + 3000);
                    }
                    else { fx = minX + random.Next(8 * (int)S); fz = minZ + random.Next(8 * (int)S); }
                    long dx = random.Next(-32768, 32769), dz = random.Next(-32768, 32769);
                    if (random.Next(5) == 0) { dx = 0; if (random.Next(2) == 0) dz = 0; }
                    bool expected = OracleClear(g, fx, fz, fx + dx, fz + dz, r);
                    bool actual = g.SegmentClear(P(fx, fz), P(fx + dx, fz + dz), R(r));
                    Assert.That(actual, Is.EqualTo(expected), $"world {world} r={r} from=({fx},{fz}) d=({dx},{dz})");
                    if (expected) clear++; else blocked++;
                }
            }
            Assert.That(clear + blocked, Is.EqualTo(100000));
            Assert.That(clear, Is.GreaterThan(10000)); Assert.That(blocked, Is.GreaterThan(10000));
        }

        // Distance exactly r is touching, hence clear; one raw closer blocks. One case for each way a corner can be the nearest
        // point of the segment: its start, its end and its interior (the cross-product branch). r = 5 raw, offsets (3,4) and
        // (4,3)-multiples, the corner being the lower-left corner of the blocked cell (3,3).
        [TestCase(3L, 4L, -13L, -14L, 0)]   // start nearest
        [TestCase(13L, 14L, -3L, -4L, 1)]   // end nearest
        [TestCase(23L, -11L, 17L, -19L, 2)] // interior nearest (perpendicular 5)
        public void CornerTouch_IsClear_AndOneCloserIsBlocked(long fromDx, long fromDz, long toDx, long toDz, int branch)
        {
            NavGrid g = Grid(0, 0, (3, 3));
            long cx = 3 * S, cz = 3 * S;
            SimVec3 from, to;
            if (branch == 0) { from = P(cx - 3, cz - 4); to = P(cx - 13, cz - 14); }
            else if (branch == 1) { from = P(cx - 13, cz - 14); to = P(cx - 3, cz - 4); }
            else { from = P(cx - 23, cz + 11); to = P(cx + 17, cz - 19); }
            Assert.That(OracleClear(g, from.X.Raw, from.Z.Raw, to.X.Raw, to.Z.Raw, 5), Is.True, "oracle: touching is clear");
            Assert.That(g.SegmentClear(from, to, R(5)), Is.True, "touching");
            Assert.That(OracleClear(g, from.X.Raw, from.Z.Raw, to.X.Raw, to.Z.Raw, 6), Is.False);
            Assert.That(g.SegmentClear(from, to, R(6)), Is.False, "one raw closer");
        }
    }
}
