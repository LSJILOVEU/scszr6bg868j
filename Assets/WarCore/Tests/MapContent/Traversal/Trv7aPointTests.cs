using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using WarCore.Sim;
using static WarCore.Tests.Ml6Nav;

namespace WarCore.Tests
{
    // TRV-7a: which entries belong to a point and in what order, the watch cells' ring, order and points, on hand-made graphs
    // with a sight table written by hand (T7-06, T7-07, T7-08, T7-09, T7-10, T7-19). Sight against real walls and plates with
    // the ML-3 body is in Trv7aWiringTests.
    public sealed class Trv7aPointTests
    {
        private const long S = SimFix.Scale;
        private const long Far = 100000;

        private static TerrainTables Wall(int w, int d, int wallZ, params int[] gaps) =>
            Open(w, d, Enumerable.Range(0, w).Where(x => !gaps.Contains(x)).Select(x => (x, wallZ)).ToArray());

        private static int Cell(SimFix v) => (int)(v.Raw / S);

        // A sight table: from one cell middle to another, true only for the listed pairs.
        private static Func<SimVec3, SimVec3, bool> Table(params ((int, int) from, (int, int) to)[] pairs)
        {
            var set = new HashSet<(int, int, int, int)>(pairs.Select(p => (p.from.Item1, p.from.Item2, p.to.Item1, p.to.Item2)));
            return (a, b) => set.Contains((Cell(a.X), Cell(a.Z), Cell(b.X), Cell(b.Z)));
        }

        // T7-06: an entry belongs when its inner cost is at most the reach: 9 x 9, wall z = 4, gap x = 3, the point's cell (3, 8):
        // the inner side (3, 5) costs 3000; reach 3000 takes it, 2999 does not.
        [Test]
        public void T7_06_TheInnerCostIsAtMostTheReach()
        {
            LayeredNav nav = Nav(Wall(9, 9, 4, 3));
            EntryTable t = EntryTable.Build(nav);
            var reach = new LayeredReach(nav, Node(nav, 0, 3, 8));
            PointEntry[] taken = PointEntryTable.Of(nav, t, reach, 3000);
            Assert.That(taken.Select(e => (e.Entry, e.InnerCost, e.OuterCost)), Is.EqualTo(new[] { (0, 3000L, 5000L) }));
            Assert.That(PointEntryTable.Of(nav, t, reach, 2999), Is.Empty);
        }

        // T7-07: the inner side is the nearer one, whichever of A and B it is; equal costs belong nowhere. 9 x 9, wall z = 4 on
        // x 0..5 only, gap x = 2. Point north at (2, 8): inner = B (2, 5). Point south at (2, 0): inner = A (2, 3). Point at (8, 4),
        // on the wall's line past its end: both sides cost the same (3414 + 3000), so the entry is not the point's.
        [Test]
        public void T7_07_TheNearerSideIsInnerAndATieBelongsNowhere()
        {
            LayeredNav nav = Nav(Open(9, 9, Enumerable.Range(0, 6).Where(x => x != 2).Select(x => (x, 4)).ToArray()));
            EntryTable t = EntryTable.Build(nav);
            Assert.That(t.Count, Is.EqualTo(1));
            int a = Node(nav, 0, 2, 3), b = Node(nav, 0, 2, 5);
            Assert.That((t[0].NodeA, t[0].NodeB), Is.EqualTo((a, b)));
            PointEntry north = PointEntryTable.Of(nav, t, new LayeredReach(nav, Node(nav, 0, 2, 8)), Far).Single();
            Assert.That((north.InnerNode, north.OuterNode, north.InnerLayer, north.InnerPoint), Is.EqualTo((b, a, 0, t[0].PointB)));
            PointEntry south = PointEntryTable.Of(nav, t, new LayeredReach(nav, Node(nav, 0, 2, 0)), Far).Single();
            Assert.That((south.InnerNode, south.OuterNode, south.OuterPoint), Is.EqualTo((a, b, t[0].PointB)));
            var side = new LayeredReach(nav, Node(nav, 0, 8, 4));
            Assert.That(side.CostFrom(a), Is.EqualTo(side.CostFrom(b)));
            Assert.That(PointEntryTable.Of(nav, t, side, Far), Is.Empty);
        }

        // T7-08: ordered by inner cost, then entry number. 9 x 9, wall z = 4, gaps x = 1 and x = 7 (entries 0 and 1). Point (4, 8):
        // both inner sides cost 3 x 1414: entry order. Point (2, 8): 3414 against 6242: 0, 1. Point (6, 8): 1, 0.
        [Test]
        public void T7_08_ByInnerCostThenEntryNumber()
        {
            LayeredNav nav = Nav(Wall(9, 9, 4, 1, 7));
            EntryTable t = EntryTable.Build(nav);
            int[] Order(int x) => PointEntryTable.Of(nav, t, new LayeredReach(nav, Node(nav, 0, x, 8)), Far).Select(e => e.Entry).ToArray();
            long[] Costs(int x) => PointEntryTable.Of(nav, t, new LayeredReach(nav, Node(nav, 0, x, 8)), Far).Select(e => e.InnerCost).ToArray();
            Assert.That(Order(4), Is.EqualTo(new[] { 0, 1 }));
            Assert.That(Costs(4), Is.EqualTo(new[] { 4242L, 4242L }));
            Assert.That(Order(2), Is.EqualTo(new[] { 0, 1 }));
            Assert.That(Costs(2), Is.EqualTo(new[] { 3414L, 6242L }));
            Assert.That(Order(6), Is.EqualTo(new[] { 1, 0 }));
            Assert.That(Costs(6), Is.EqualTo(new[] { 3414L, 6242L }));
        }

        // The watch fixture: 24 x 24, wall z = 10 with a gap at x = 10; the point's cell (13, 23). Inner I = (10, 11) (cost
        // 3 x 1414 + 9000 = 13242), outer O = (10, 9) (15242).
        private static (LayeredNav nav, EntryTable t, LayeredReach reach) Watchable()
        {
            LayeredNav nav = Nav(Wall(24, 24, 10, 10));
            return (nav, EntryTable.Build(nav), new LayeredReach(nav, Node(nav, 0, 13, 23)));
        }

        private static (int, int)[] Cells(LayeredNav nav, PointEntryTable p) =>
            Enumerable.Range(0, p.WatchCount(0, 0)).Select(j => (nav.XOf(p.Watch(0, 0, j).Node), nav.ZOf(p.Watch(0, 0, j).Node))).ToArray();

        // T7-09: five candidates, by hand. Offsets from I: A (4, 0) and B (6, 0) see I and O; C (3, 2) and D (6, 2) see I only;
        // E (0, 5) sees O only (not a candidate). All are on the point's side (costs 12414, 13242, 10000, 11242, 8242 < 15242). Order:
        // A, B first (they see O; both 1 m from 5 m, A the smaller node), then D (sqrt 40 = 6.325 m, 1.325 off) before C (sqrt 13 =
        // 3.606 m, 1.394 off; ranking by the squared difference would take C: 12 < 15). Kept: A, B, D.
        [Test]
        public void T7_09_FiveCandidatesByHand()
        {
            (LayeredNav nav, EntryTable t, LayeredReach reach) = Watchable();
            (int, int) i = (10, 11), o = (10, 9), a = (14, 11), b = (16, 11), c = (13, 13), d = (16, 13), e = (10, 16);
            var sees = Table((a, i), (a, o), (b, i), (b, o), (c, i), (d, i), (e, o));
            PointEntryTable p = PointEntryTable.Build(nav, t, new[] { reach }, 20000, 3, sees);
            Assert.That(p.EntryCount(0), Is.EqualTo(1));
            Assert.That((p.Entry(0, 0).InnerCost, p.Entry(0, 0).OuterCost), Is.EqualTo((13242L, 15242L)));
            Assert.That(Cells(nav, p), Is.EqualTo(new[] { a, b, d }));
            Assert.That(Enumerable.Range(0, 3).Select(j => p.Watch(0, 0, j).SeesOuter), Is.EqualTo(new[] { true, true, false }));
            // Every candidate is asked about I, and the ones that see I about O too.
            Assert.That(PointEntryTable.Build(nav, t, new[] { reach }, 20000, 10, sees).WatchCount(0, 0), Is.EqualTo(4));
        }

        // T7-09: seeing the outer side comes before being nearer 5 m. G (4, 0) and A' (3, 2) see I and O; F (5, 0) sees I only and is
        // exactly 5 m off. Order: G, A', F.
        [Test]
        public void T7_09_SeeingOutsideComesFirst()
        {
            (LayeredNav nav, EntryTable t, LayeredReach reach) = Watchable();
            (int, int) i = (10, 11), o = (10, 9), g = (14, 11), a = (13, 13), f = (15, 11);
            PointEntryTable p = PointEntryTable.Build(nav, t, new[] { reach }, 20000, 3, Table((g, i), (g, o), (a, i), (a, o), (f, i)));
            Assert.That(Cells(nav, p), Is.EqualTo(new[] { g, a, f }));
        }

        // T7-09: the order key by itself, each level deciding alone.
        [Test]
        public void T7_09_TheOrderKey()
        {
            Assert.That(PointEntryTable.Rank(true, 9, 9, false, 0, 0), Is.LessThan(0), "outer first");
            Assert.That(PointEntryTable.Rank(false, 0, 0, true, 9, 9), Is.GreaterThan(0));
            Assert.That(PointEntryTable.Rank(true, 1, 9, true, 2, 0), Is.LessThan(0), "nearer 5 m");
            Assert.That(PointEntryTable.Rank(true, 2, 0, true, 1, 9), Is.GreaterThan(0));
            Assert.That(PointEntryTable.Rank(false, 3, 4, false, 3, 5), Is.LessThan(0), "smaller node");
            Assert.That(PointEntryTable.Rank(false, 3, 5, false, 3, 4), Is.GreaterThan(0));
            Assert.That(PointEntryTable.Rank(false, 3, 5, false, 3, 5), Is.EqualTo(0));
            // |floor(sqrt(d^2)) - 5 m| in raw: 4 m and 6 m are both 1 m off; sqrt 13 m and sqrt 40 m by whole raw.
            Assert.That(PointEntryTable.FromPreferred(16 * S * S), Is.EqualTo(S));
            Assert.That(PointEntryTable.FromPreferred(36 * S * S), Is.EqualTo(S));
            Assert.That(PointEntryTable.FromPreferred(13 * S * S), Is.EqualTo(5 * S - 236293));
            Assert.That(PointEntryTable.FromPreferred(40 * S * S), Is.EqualTo(414486 - 5 * S));
        }

        // The whole square root rounded down, without floating point: around squares and at the top of the range used.
        [Test]
        public void IsqrtRoundsDown()
        {
            foreach (long r in new long[] { 0, 1, 2, 3, 65535, 65536, 236293, 3037000499 })
            {
                long sq = r * r;
                Assert.That(PointEntryTable.Isqrt(sq), Is.EqualTo(r), "square of " + r);
                if (r > 0) Assert.That(PointEntryTable.Isqrt(sq - 1), Is.EqualTo(r - 1), "below the square of " + r);
                if (r > 0) Assert.That(PointEntryTable.Isqrt(sq + 1), Is.EqualTo(r), "above the square of " + r);
            }
            Assert.That(PointEntryTable.Isqrt(long.MaxValue), Is.EqualTo(3037000499L));
            Assert.Throws<ArgumentOutOfRangeException>(() => PointEntryTable.Isqrt(-1));
        }

        // T7-10: 2 m and 10 m are in, 1 raw closer or farther is out; on a graph the ring keeps exactly the cells whose middles are
        // 2 to 10 m off (everyone sees everyone, keep all).
        [Test]
        public void T7_10_TwoToTenMetresBothIncluded()
        {
            long lo = 2 * S, hi = 10 * S;
            Assert.That((PointEntryTable.InRing(lo * lo), PointEntryTable.InRing(lo * lo - 1)), Is.EqualTo((true, false)));
            Assert.That((PointEntryTable.InRing(hi * hi), PointEntryTable.InRing(hi * hi + 1)), Is.EqualTo((true, false)));
            (LayeredNav nav, EntryTable t, LayeredReach reach) = Watchable();
            PointEntryTable p = PointEntryTable.Build(nav, t, new[] { reach }, Far, int.MaxValue, (x, y) => true);
            var kept = new HashSet<(int, int)>(Cells(nav, p));
            for (int x = 0; x < 24; x++)
                for (int z = 11; z < 24; z++)
                {
                    long d2 = (long)(x - 10) * (x - 10) + (long)(z - 11) * (z - 11);
                    bool cheaper = reach.CostFrom(Node(nav, 0, x, z)) < 15242;
                    Assert.That(kept.Contains((x, z)), Is.EqualTo(cheaper && d2 >= 4 && d2 <= 100), "cell " + x + "," + z);
                }
            Assert.That(kept.Contains((12, 11)) && kept.Contains((10, 21)) && kept.Contains((16, 19)), Is.True, "2 m, 10 m and (6, 8) are in");
            Assert.That(kept.Contains((11, 12)) || kept.Contains((11, 21)), Is.False, "sqrt 2 m and sqrt 101 m are out");
        }

        // T7-19: a cell middle's height is its layer's four corners added and divided by 4, rounded down: corners 1, 2, 3, 5 raw
        // give 2; -1, -2, -3, -5 give -3 (11 / 4 down); a flat plate gives its top; a ground cell of a height field its corners' mean.
        [Test]
        public void T7_19_TheMiddleIsTheFourCornersMeanRoundedDown()
        {
            PlateSlab Corners(params int[] p) => new PlateSlab(1, 1, 1, 1, 1, new[] { p[0], p[1], p[2], p[3] }, 32768, new[] { true });
            LayeredNav up = Nav(Open(3, 3), Corners(1, 2, 3, 5));
            LayeredNav down = Nav(Open(3, 3), Corners(-1, -2, -3, -5));
            Assert.That(EntryTable.Centre(up, Node(up, 1, 1, 1)), Is.EqualTo(new SimVec3(SimFix.FromRaw(S + S / 2), SimFix.FromRaw(2), SimFix.FromRaw(S + S / 2))));
            Assert.That(EntryTable.Centre(down, Node(down, 1, 1, 1)).Y.Raw, Is.EqualTo(-3L));
            LayeredNav flat = Nav(Open(3, 3), Ml5Plates.Flat(1, 0, 0, 2, 2, 196608));
            Assert.That(EntryTable.Centre(flat, Node(flat, 1, 1, 0)).Y.Raw, Is.EqualTo(196608L));
            // Ground 2 x 1 with corner heights (row-major, 3 x 2): 0, 4, 9 / 1, 6, 10: cell (1, 0) has 4 + 9 + 6 + 10 = 29 -> 7.
            LayeredNav field = Nav(Ground(2, 1, new byte[] { 1, 1 }, new[] { 0, 4, 9, 1, 6, 10 }));
            Assert.That(EntryTable.Centre(field, 1).Y.Raw, Is.EqualTo(7L));
            Assert.That((EntryTable.FloorQuarter(8), EntryTable.FloorQuarter(-8), EntryTable.FloorQuarter(-1)), Is.EqualTo((2L, -2L, -1L)));
        }

        // An entry's own nodes are never its watch cells: a 2-row doorway (gaps (2..3, 4) and (3..4, 5) in walls on rows 4 and 5,
        // 10 x 10) with the point north at (3, 9): the inner side is (3, 6); the own cell (2, 4) is 2.24 m from it, seen by
        // everyone, on the point's side of the outer side (cost 6000 against 6414), and still not a candidate. Everyone sees
        // everyone, keep all.
        [Test]
        public void OwnNodesAreNotWatchCells()
        {
            var walls = Enumerable.Range(0, 10).Where(x => x != 2 && x != 3).Select(x => (x, 4))
                .Concat(Enumerable.Range(0, 10).Where(x => x != 3 && x != 4).Select(x => (x, 5))).ToArray();
            LayeredNav nav = Nav(Open(10, 10, walls));
            EntryTable t = EntryTable.Build(nav);
            var reach = new LayeredReach(nav, Node(nav, 0, 3, 9));
            PointEntryTable p = PointEntryTable.Build(nav, t, new[] { reach }, Far, int.MaxValue, (x, y) => true);
            PointEntry e = p.Entry(0, 0);
            Assert.That((nav.XOf(e.InnerNode), nav.ZOf(e.InnerNode), e.OuterCost), Is.EqualTo((3, 6, 6414L)));
            int own = Node(nav, 0, 2, 4);
            Assert.That((t.Owns(0, own), reach.CostFrom(own)), Is.EqualTo((true, 6000L)));
            (int, int)[] cells = Cells(nav, p);
            Assert.That(cells, Has.No.Member((2, 4)).And.No.Member((3, 4)).And.No.Member((3, 5)).And.No.Member((4, 5)));
            Assert.That(cells, Has.Member((5, 6)), "2 m along the row is a candidate");
        }

        // Bad arguments are refused before anything is built.
        [Test]
        public void ArgumentsAreChecked()
        {
            (LayeredNav nav, EntryTable t, LayeredReach reach) = Watchable();
            Func<SimVec3, SimVec3, bool> any = (x, y) => true;
            Assert.Throws<ArgumentNullException>(() => PointEntryTable.Build(null, t, new[] { reach }, 1, 1, any));
            Assert.Throws<ArgumentNullException>(() => PointEntryTable.Build(nav, null, new[] { reach }, 1, 1, any));
            Assert.Throws<ArgumentNullException>(() => PointEntryTable.Build(nav, t, null, 1, 1, any));
            Assert.Throws<ArgumentNullException>(() => PointEntryTable.Build(nav, t, new[] { reach }, 1, 1, null));
            Assert.Throws<ArgumentOutOfRangeException>(() => PointEntryTable.Build(nav, t, new[] { reach }, -1, 1, any));
            Assert.Throws<ArgumentOutOfRangeException>(() => PointEntryTable.Build(nav, t, new[] { reach }, 1, -1, any));
            Assert.Throws<ArgumentNullException>(() => EntryTable.Build(null));
            // A point nobody reaches has no entries.
            Assert.That(PointEntryTable.Build(nav, t, new LayeredReach[] { null }, Far, 3, any).EntryCount(0), Is.EqualTo(0));
        }
    }
}
