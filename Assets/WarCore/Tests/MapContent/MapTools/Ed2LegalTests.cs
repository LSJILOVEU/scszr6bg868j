using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Content.MapTools;
using static WarCore.Tests.Ed2Fixtures;

namespace WarCore.Tests
{
    // ED-2 acceptance 4: writes stay within 2/1 and the height range.
    public sealed class Ed2LegalTests
    {
        private const long Wide = 1L << 40;

        // For each corner of one cell (and the centre of a 2 x 2 map) with random neighbours, every height in the interval
        // keeps the cells legal under the 2/1 rule and the heights one outside do not; an empty interval has no legal height
        // among the natural candidates.
        [Test]
        public void TheIntervalMatchesTheSlopeRule()
        {
            var rng = new Ed1Rng(2002);
            int empty = 0, full = 0;
            for (int n = 0; n < 10000; n++)
            {
                bool centre = n % 5 == 4;
                int width = centre ? 2 : 1, row = width + 1;
                var h = new long[row * row];
                int i = centre ? 1 : n % 2, j = centre ? 1 : (n / 2) % 2;
                do
                {
                    for (int k = 0; k < h.Length; k++) h[k] = rng.Next(300001) - 150000;
                } while (!OthersLegal(h, width, i, j));
                long lo = -Wide, hi = Wide;
                MapBrush.Allowed(h, width, width, i, j, ref lo, ref hi);
                Func<long, bool> legal = v =>
                {
                    long keep = h[j * row + i];
                    h[j * row + i] = v;
                    bool ok = true;
                    for (int cz = 0; cz < width; cz++)
                        for (int cx = 0; cx < width; cx++)
                            ok &= MapBrush.CellWithin(h[cz * row + cx], h[cz * row + cx + 1], h[(cz + 1) * row + cx], h[(cz + 1) * row + cx + 1], 2, 1);
                    h[j * row + i] = keep;
                    return ok;
                };
                if (lo > hi)
                {
                    empty++;
                    var candidates = new List<long> { lo, hi };
                    foreach (long v in h) { candidates.Add(v); candidates.Add(v + 1); candidates.Add(v - 1); }
                    for (int a = 0; a < h.Length; a++)
                        for (int b = 0; b < h.Length; b++) { candidates.Add((h[a] + h[b]) >> 1); candidates.Add(((h[a] + h[b]) >> 1) + 1); }
                    foreach (long v in candidates) Assert.That(legal(v), Is.False, "case " + n + " empty, " + v);
                    continue;
                }
                full++;
                Assert.That(legal(lo), Is.True, "case " + n + " lo");
                Assert.That(legal(hi), Is.True, "case " + n + " hi");
                Assert.That(legal((lo + hi) >> 1), Is.True, "case " + n + " middle");
                Assert.That(legal(lo - 1), Is.False, "case " + n + " lo - 1");
                Assert.That(legal(hi + 1), Is.False, "case " + n + " hi + 1");
            }
            Assert.That(empty, Is.GreaterThan(1000));
            Assert.That(full, Is.GreaterThan(1000));
        }

        // The triangles that do not hold point (i, j) are legal, so the cell test depends only on the point's own triangles.
        // Triangle A is (0,0) (1,0) (1,1) and B is (0,0) (0,1) (1,1) of each cell (HeightField.CellWithinSlope).
        private static bool OthersLegal(long[] h, int width, int i, int j)
        {
            int row = width + 1;
            for (int cz = 0; cz < width; cz++)
                for (int cx = 0; cx < width; cx++)
                {
                    long h00 = h[cz * row + cx], h10 = h[cz * row + cx + 1], h01 = h[(cz + 1) * row + cx], h11 = h[(cz + 1) * row + cx + 1];
                    bool on00 = i == cx && j == cz, on10 = i == cx + 1 && j == cz, on01 = i == cx && j == cz + 1, on11 = i == cx + 1 && j == cz + 1;
                    if (!(on00 || on10 || on11) && !MapBrush.CellWithin(h00, h10, h00 + h11 - h10, h11, 2, 1)) return false;
                    if (!(on00 || on01 || on11) && !MapBrush.CellWithin(h00, h00 + h11 - h01, h01, h11, 2, 1)) return false;
                }
            return true;
        }

        [Test]
        public void TheIntervalIsCutByTheRangeGiven()
        {
            var h = new long[4];
            long lo = -5, hi = 7;
            MapBrush.Allowed(h, 1, 1, 0, 0, ref lo, ref hi);
            Assert.That((lo, hi), Is.EqualTo((-5L, 7L)));
        }

        [Test]
        public void RaisingASlopeAlreadyAtTwoToOneLeavesItLegal()
        {
            // h = 2 S i: every cell exactly 2/1 along x. No single point can rise without making a triangle steeper (each
            // move is one point at a time), so nothing moves and the map stays legal.
            MapDesc d = Map(8, 6, (i, j) => (int)(2 * S * i), 0, 1L << 21);
            AllLegal(d);
            Assert.That(MapBrush.Raise(d, 4, 3, 3, 50000), Is.Empty);
            // At 3/2 there is room: points rise, by less than asked where the slope stops them, and the map stays legal.
            MapDesc e = Map(8, 6, (i, j) => (int)(3 * S / 2 * i), 0, 1L << 21);
            MapHeightWrite[] writes = MapBrush.Raise(e, 4, 3, 3, 200000);
            Assert.That(writes.Length, Is.GreaterThan(0));
            MapDesc after = Apply(e, writes);
            AllLegal(after);
            foreach (MapHeightWrite w in writes) Assert.That(w.Raw, Is.GreaterThan(e.Heights[w.J][w.I]));
            Assert.That(after.Heights[3][4], Is.InRange(e.Heights[3][4] + 1, e.Heights[3][4] + 199999));
        }

        [Test]
        public void ABigRaiseBecomesTheSteepestLegalHill()
        {
            MapDesc d = Flat(16, 16);
            MapDesc after = Apply(d, MapBrush.Raise(d, 8, 8, 6, 200000));
            AllLegal(after);
            Assert.That(after.Heights[8][8], Is.GreaterThan(131072));
        }

        [Test]
        public void ABankIsSteepOnlyAtTheSegment()
        {
            // (0, 4) to (8, 4), half width 3, 200000: row 3 wants 177734 but stops at 2 S above row 4; row 2 gets its wanted
            // 110937 (weight 568); rows 0, 1 and 4.. do not move.
            MapDesc d = Flat(8, 8);
            MapDesc after = Apply(d, MapBrush.Bank(d, 0, 4, 8, 4, 3, 200000));
            AllLegal(after);
            for (int i = 0; i <= 8; i++)
            {
                Assert.That(after.Heights[3][i], Is.EqualTo(2 * S), "row 3, i " + i);
                Assert.That(after.Heights[2][i], Is.EqualTo(110937), "row 2, i " + i);
                foreach (int j in new[] { 0, 1, 4, 5, 8 }) Assert.That(after.Heights[j][i], Is.EqualTo(0), "row " + j + ", i " + i);
            }
        }

        [Test]
        public void WantedHeightsAreClampedToTheRange()
        {
            MapDesc d = Map(8, 8, (i, j) => 0, -1000, 3000);
            MapDesc up = Apply(d, MapBrush.Raise(d, 4, 4, 4, 100000));
            Assert.That(up.Heights[4][4], Is.EqualTo(3000));
            AllLegal(up);
            MapDesc down = Apply(d, MapBrush.Raise(d, 4, 4, 4, -100000));
            Assert.That(down.Heights[4][4], Is.EqualTo(-1000));
            Assert.That(MapBrush.Flatten(d, 4, 4, 2, 200000)[0].Raw, Is.EqualTo(3000));
        }

        [Test]
        public void PointsOfAnIllegalCellDoNotMove()
        {
            // (1, 0) is 300000 above its neighbours: cells (0, 0) and (1, 0) are illegal, through points (0, 0), (1, 0),
            // (2, 0), (1, 1) and (2, 1).
            MapDesc d = Map(4, 4, (i, j) => i == 1 && j == 0 ? 300000 : 0, -262144, 400000);
            Assert.That(CellLegal(d, 0, 0) || CellLegal(d, 1, 0), Is.False);
            MapHeightWrite[] writes = MapBrush.Raise(d, 1, 1, 2, 1000);
            var moved = new HashSet<(int, int)>();
            foreach (MapHeightWrite w in writes) moved.Add((w.I, w.J));
            foreach ((int, int) p in new[] { (0, 0), (1, 0), (2, 0), (1, 1), (2, 1) }) Assert.That(moved.Contains(p), Is.False, p.ToString());
            Assert.That(moved.Contains((1, 2)), Is.True);
            Assert.That(moved.Contains((0, 1)), Is.True);
            MapDesc after = Apply(d, writes);
            for (int cz = 0; cz < 4; cz++)
                for (int cx = 0; cx < 4; cx++)
                    if (cz > 0 || cx > 1) Assert.That(CellLegal(after, cx, cz), Is.True, cx + "," + cz);
        }

        [Test]
        public void APointOutsideTheRangeDoesNotMove()
        {
            // The reader does not check heights against floor and ceiling; (2, 2) at 2000 is above the ceiling 1000.
            MapDesc d = Map(4, 4, (i, j) => i == 2 && j == 2 ? 2000 : 0, -1000, 1000);
            MapHeightWrite[] writes = MapBrush.Raise(d, 2, 2, 2, -500);
            Assert.That(writes.Length, Is.GreaterThan(0));
            foreach (MapHeightWrite w in writes) Assert.That((w.I, w.J), Is.Not.EqualTo((2, 2)));
        }

        [Test]
        public void ALimitTravelsAgainstTheOrderOfThePoints()
        {
            // A 40 m strip at 80 m; every point but the last column wants 0. The last column holds, so the strip can only
            // fall 2 m per metre away from it, a limit that travels toward lower i, against the order the points were added.
            MapDesc d = Map(40, 1, (i, j) => (int)(80 * S), 0, 80 * S);
            var work = new MapBrush.HeightWork(d);
            for (int j = 0; j <= 1; j++)
                for (int i = 0; i < 40; i++) work.Add(i, j, 0);
            MapDesc after = Apply(d, work.Legalize());
            AllLegal(after);
            Assert.That(MapBrush.LastPasses, Is.LessThan(MapBrush.MaxPasses));
            Assert.That(after.Heights[0][0], Is.EqualTo(0));
            Assert.That(after.Heights[0][39], Is.EqualTo(78 * S));
        }

        [Test]
        public void ManyPassesSpreadAWallOfHeight()
        {
            // A flatten to the ceiling over a radius 40 disc: the hill can only grow 2 S per metre from the edge, which
            // needs passes in both directions.
            MapDesc d = Map(100, 100, (i, j) => 0, 0, 20 * 2 * S);
            MapDesc after = Apply(d, MapBrush.Flatten(d, 50, 50, 40, (int)(20 * 2 * S)));
            AllLegal(after);
            Assert.That(after.Heights[50][50], Is.EqualTo(20 * 2 * S));
        }
    }
}
