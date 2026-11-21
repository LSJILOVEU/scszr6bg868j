using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Sim;
using static WarCore.Tests.Ml6Nav;

namespace WarCore.Tests
{
    // TRV-7a: the entry table (narrow entries and seam entries) on hand-made graphs, the demo house and seeded maps against a
    // slow implementation written apart (T7-01, T7-03, T7-04, T7-05, T7-15, T7-16, acceptance 6).
    public sealed class Trv7aEntryTests
    {
        private static (EntryKind, int, string, string)[] Rows(LayeredNav nav, EntryTable t) =>
            Enumerable.Range(0, t.Count).Select(e => (t[e].Kind, t[e].Width, Trv7aFixture.Cell(nav, t[e].NodeA), Trv7aFixture.Cell(nav, t[e].NodeB))).ToArray();

        // 9 x 9 open ground with the row z = 4 blocked except the given cells: rooms four deep on both sides, so no column of the
        // rooms is narrow.
        private static TerrainTables WallWithGap(params int[] open)
        {
            var blocked = Enumerable.Range(0, 9).Where(x => !open.Contains(x)).Select(x => (x, 4)).ToArray();
            return Open(9, 9, blocked);
        }

        // T7-01: the demo house has exactly three entries: the west doorway (narrow, 2 wide, its sides the cells west and east of
        // it), the stair's foot and the stair's top (seams, 1 wide). The stair itself is a 1-wide run 4 long: its end rows would be
        // narrow entries too, but each has a side equal to a seam entry's, and a seam entry is kept before a narrow one.
        [Test]
        public void T7_01_TheDemoHouseHasADoorwayAndTheStairsTwoEnds()
        {
            WcMap map = WcMapReader.Read(PlateDemoMap.Bytes());
            LayeredNav nav = WcMapLayeredNav.Build(map, null, map.ContentId);
            EntryTable t = EntryTable.Build(nav);
            Assert.That(Rows(nav, t), Is.EqualTo(new[]
            {
                (EntryKind.Narrow, 2, "L0(7,7)", "L0(9,7)"),
                (EntryKind.Seam, 1, "L0(9,9)", "L1(10,9)"),
                (EntryKind.Seam, 1, "L1(13,9)", "L2(14,9)"),
            }));
            // The doorway's own nodes are its two cells; the stair foot's are the two seam nodes.
            Assert.That(Enumerable.Range(0, t.OwnCount(0)).Select(k => Trv7aFixture.Cell(nav, t.Own(0, k))), Is.EqualTo(new[] { "L0(8,7)", "L0(8,8)" }));
            Assert.That(Enumerable.Range(0, t.OwnCount(1)).Select(k => Trv7aFixture.Cell(nav, t.Own(1, k))), Is.EqualTo(new[] { "L0(9,9)", "L1(10,9)" }));
            Assert.That((t.Owns(0, Node(nav, 0, 8, 8)), t.Owns(0, Node(nav, 0, 7, 7)), t.Owns(1, Node(nav, 1, 10, 9))), Is.EqualTo((true, false, true)));
        }

        // T7-03, T7-04: a gap 1 or 2 wide is one entry (its node A the smaller of the two side representatives, node B the
        // other); 3 wide is none. A 2-wide gap with nothing walkable behind it is none.
        [Test]
        public void T7_03_TwoWideCountsThreeWideDoesNot()
        {
            LayeredNav one = Nav(WallWithGap(3)), two = Nav(WallWithGap(3, 4)), three = Nav(WallWithGap(3, 4, 5));
            Assert.That(Rows(one, EntryTable.Build(one)), Is.EqualTo(new[] { (EntryKind.Narrow, 1, "L0(3,3)", "L0(3,5)") }));
            Assert.That(Rows(two, EntryTable.Build(two)), Is.EqualTo(new[] { (EntryKind.Narrow, 2, "L0(3,3)", "L0(3,5)") }));
            Assert.That(EntryTable.Build(three).Count, Is.EqualTo(0));
            var blocked = Enumerable.Range(0, 9).Where(x => x != 3 && x != 4).Select(x => (x, 4)).Concat(new[] { (3, 5), (4, 5) }).ToArray();
            LayeredNav shut = Nav(Open(9, 9, blocked));
            Assert.That(EntryTable.Build(shut).Count, Is.EqualTo(0), "nothing behind the gap");
            // Only a side in front: behind one of the two cells is enough.
            var half = Enumerable.Range(0, 9).Where(x => x != 3 && x != 4).Select(x => (x, 4)).Concat(new[] { (4, 5) }).ToArray();
            LayeredNav open = Nav(Open(9, 9, half));
            Assert.That(Rows(open, EntryTable.Build(open)), Is.EqualTo(new[] { (EntryKind.Narrow, 2, "L0(3,3)", "L0(3,5)") }));
        }

        // T7-03: a run ends open when the cell past its end belongs to another layer joined by a seam: a 1-wide gap whose east
        // neighbour is blocked ground under a plate 0.3 m up (joined to the gap and to the cells north and south of it) is not
        // narrow. The plate's three seams are three seam entries sharing the plate's node: one crossing, kept once, the one
        // with the smallest representative ((4, 3) = 31; (3, 4) = 39, (4, 5) = 49).
        [Test]
        public void T7_03_ARunEndingAtASeamIsNotNarrow()
        {
            var blocked = Enumerable.Range(0, 9).Where(x => x != 3).Select(x => (x, 4)).ToArray();
            LayeredNav nav = Nav(Open(9, 9, blocked), Ml5Plates.Flat(1, 4, 4, 1, 1, 19660, 32768));
            EntryTable t = EntryTable.Build(nav);
            Assert.That(Rows(nav, t), Is.EqualTo(new[] { (EntryKind.Seam, 1, "L0(4,3)", "L1(4,4)") }));
        }

        // T7-04: narrow rows join only when they overlap across, never corner to corner. Two walls (z 4 and z 5): gaps (2..3, 4)
        // and (3..4, 5) overlap at x = 3 and are one 2-row passage, one entry (both rows 2 wide). Gaps (1..2, 4) and (3..4, 5) only
        // touch at a corner: each has the other wall behind it, so neither is narrow and there is no entry.
        [Test]
        public void T7_04_RowsJoinOnlyWhereTheyOverlap()
        {
            (int, int)[] Walls(int[] at4, int[] at5) =>
                Enumerable.Range(0, 10).Where(x => !at4.Contains(x)).Select(x => (x, 4)).Concat(Enumerable.Range(0, 10).Where(x => !at5.Contains(x)).Select(x => (x, 5))).ToArray();
            LayeredNav overlap = Nav(Open(10, 10, Walls(new[] { 2, 3 }, new[] { 3, 4 })));
            Assert.That(Rows(overlap, EntryTable.Build(overlap)), Is.EqualTo(new[] { (EntryKind.Narrow, 2, "L0(2,3)", "L0(3,6)") }));
            LayeredNav corner = Nav(Open(10, 10, Walls(new[] { 1, 2 }, new[] { 3, 4 })));
            Assert.That(EntryTable.Build(corner).Count, Is.EqualTo(0));
        }

        // The door's width is its narrowest row (coordinator 11-21): a 2-row doorway 2 wide in one row and 1 wide in the other.
        [Test]
        public void T7_04_ADoorwaysWidthIsItsNarrowestRow()
        {
            var blocked = Enumerable.Range(0, 10).Where(x => x != 3 && x != 4).Select(x => (x, 4))
                .Concat(Enumerable.Range(0, 10).Where(x => x != 3).Select(x => (x, 5))).ToArray();
            LayeredNav nav = Nav(Open(10, 10, blocked));
            Assert.That(Rows(nav, EntryTable.Build(nav)), Is.EqualTo(new[] { (EntryKind.Narrow, 1, "L0(3,3)", "L0(3,6)") }));
        }

        // T7-05: a narrow entry and a seam entry with an equal side keep the seam (the demo's stair foot and top; coordinator
        // 11-21: a seam before a narrow one whatever the node numbers). A 1 x 1 notch seen along both axes is one entry.
        [Test]
        public void T7_05_AnOverlapKeepsOne()
        {
            WcMap map = WcMapReader.Read(PlateDemoMap.Bytes());
            LayeredNav nav = WcMapLayeredNav.Build(map, null, map.ContentId);
            EntryTable t = EntryTable.Build(nav);
            Assert.That(Enumerable.Range(0, t.Count).Count(e => t[e].Kind == EntryKind.Seam), Is.EqualTo(2));
            // The stair top: its narrow end row (386, the stair's cell (12, 9) as the inner side) has the smaller representative
            // than the seam entry (387), and still the seam entry is kept.
            Assert.That(Trv7aFixture.Cell(nav, t[2].NodeA), Is.EqualTo("L1(13,9)"));
            Assert.That(t.Count, Is.EqualTo(3));
        }

        // T7-05: an equal side alone is not a duplicate. An L of 1-wide corridors (12 x 12, all blocked but these): room x 0..3,
        // z 0..3; corridor (4..7, 1) found by the column search (4 rows: entries at x 4 and x 7); the corner (8, 1); corridor
        // (8, 2..7) found by the row search (6 rows: entries at z 2 and z 7); room x 6..10, z 8..11. The entries at x 7 (sides (6, 1)
        // and the corner) and at z 2 (sides the corner and (8, 3)) share the side {corner} but no own node: both stay. Four entries.
        [Test]
        public void T7_05_AnEqualSideAloneIsNotADuplicate()
        {
            var open = new HashSet<(int, int)>();
            for (int x = 0; x <= 3; x++) for (int z = 0; z <= 3; z++) open.Add((x, z));
            for (int x = 4; x <= 8; x++) open.Add((x, 1));
            for (int z = 2; z <= 7; z++) open.Add((8, z));
            for (int x = 6; x <= 10; x++) for (int z = 8; z <= 11; z++) open.Add((x, z));
            var blocked = new List<(int, int)>();
            for (int x = 0; x < 12; x++) for (int z = 0; z < 12; z++) if (!open.Contains((x, z))) blocked.Add((x, z));
            LayeredNav nav = Nav(Open(12, 12, blocked.ToArray()));
            Assert.That(Rows(nav, EntryTable.Build(nav)), Is.EqualTo(new[]
            {
                (EntryKind.Narrow, 1, "L0(3,1)", "L0(5,1)"),
                (EntryKind.Narrow, 1, "L0(6,1)", "L0(8,1)"),
                (EntryKind.Narrow, 1, "L0(8,1)", "L0(8,3)"),
                (EntryKind.Narrow, 1, "L0(8,6)", "L0(8,8)"),
            }));
        }

        // T7-16: a corridor 2 wide between two rooms: 10 rows long gives 2 entries (its two end rows), 3 rows 2, 2 rows 1, 1 row 1.
        [TestCase(10, 2)]
        [TestCase(3, 2)]
        [TestCase(2, 1)]
        [TestCase(1, 1)]
        public void T7_16_ACorridorCountsAtItsEnds(int length, int entries)
        {
            LayeredNav nav = Nav(Corridor(length));
            EntryTable t = EntryTable.Build(nav);
            Assert.That(t.Count, Is.EqualTo(entries));
            int east = 4 + length;
            if (entries == 1)
                Assert.That(Rows(nav, t), Is.EqualTo(new[] { (EntryKind.Narrow, 2, "L0(3,3)", "L0(" + east + ",3)") }));
            else
                Assert.That(Rows(nav, t), Is.EqualTo(new[]
                {
                    (EntryKind.Narrow, 2, "L0(3,3)", "L0(5,3)"),
                    (EntryKind.Narrow, 2, "L0(" + (east - 2) + ",3)", "L0(" + east + ",3)"),
                }));
        }

        // Rooms x 0..3 and from 4 + length on, 4 wide and 8 deep; the corridor cells x 4 .. 3 + length on rows z 3..4.
        private static TerrainTables Corridor(int length)
        {
            int w = 8 + length;
            var blocked = new List<(int, int)>();
            for (int x = 4; x < 4 + length; x++)
                for (int z = 0; z < 8; z++)
                    if (z != 3 && z != 4) blocked.Add((x, z));
            return Open(w, 8, blocked.ToArray());
        }

        // Acceptance 6: built twice, the same in every field.
        [Test]
        public void BuiltTwiceTheSame()
        {
            WcMap map = WcMapReader.Read(PlateAssaultMap.Bytes());
            LayeredNav nav = WcMapLayeredNav.Build(map, null, map.ContentId);
            EntryTable a = EntryTable.Build(nav), b = EntryTable.Build(nav);
            Assert.That(b.Count, Is.EqualTo(a.Count));
            for (int e = 0; e < a.Count; e++)
            {
                Assert.That(b[e], Is.EqualTo(a[e]));
                Assert.That(Enumerable.Range(0, b.OwnCount(e)).Select(k => b.Own(e, k)), Is.EqualTo(Enumerable.Range(0, a.OwnCount(e)).Select(k => a.Own(e, k))));
            }
        }

        // T7-19 (the points of an entry): a representative's point is its cell middle on its layer: the demo's stair foot is
        // (10.5, 0.375, 9.5) on the stair (corners 0, 0.75, 0, 0.75 m).
        [Test]
        public void EntryPointsAreCellMiddlesOnTheLayer()
        {
            WcMap map = WcMapReader.Read(PlateDemoMap.Bytes());
            LayeredNav nav = WcMapLayeredNav.Build(map, null, map.ContentId);
            EntryInfo foot = EntryTable.Build(nav)[1];
            Assert.That((foot.PointB.X.Raw, foot.PointB.Y.Raw, foot.PointB.Z.Raw), Is.EqualTo((10 * S + S / 2, 3 * S / 8, 9 * S + S / 2)));
            Assert.That((foot.LayerA, foot.LayerB), Is.EqualTo((0, 1)));
        }

        // T7-15: 200 seeded maps; the narrow and seam entries equal the slow implementation's.
        [Test]
        public void T7_15_SeededMapsMatchTheSlowSearch()
        {
            var rnd = new Random(7015);
            int narrow = 0, seam = 0, corridors = 0;
            for (int m = 0; m < 200; m++)
            {
                LayeredNav nav = Seeded(rnd);
                EntryTable t = EntryTable.Build(nav);
                var fast = new List<string>();
                for (int e = 0; e < t.Count; e++)
                {
                    fast.Add(Key(t[e].Kind, t[e].Width, Enumerable.Range(0, t.OwnCount(e)).Select(k => t.Own(e, k))) + " A" + t[e].NodeA + " B" + t[e].NodeB);
                    if (e > 0) Assert.That((t[e - 1].NodeA, t[e - 1].NodeB), Is.LessThanOrEqualTo((t[e].NodeA, t[e].NodeB)), "order, map " + m);
                    if (t[e].Kind == EntryKind.Narrow) narrow++; else seam++;
                }
                List<string> slow = Slow.Entries(nav, ref corridors);
                Assert.That(fast, Is.EquivalentTo(slow), "map " + m);
            }
            TestContext.Out.WriteLine("TRV-7a seeded: narrow " + narrow + ", seam " + seam + ", corridors " + corridors);
            Assert.That(narrow, Is.GreaterThanOrEqualTo(200));
            Assert.That(seam, Is.GreaterThanOrEqualTo(100));
            Assert.That(corridors, Is.GreaterThanOrEqualTo(20));
        }

        private static string Key(EntryKind kind, int width, IEnumerable<int> own) => kind + " w" + width + " [" + string.Join(",", own) + "]";

        // Up to 14 x 14, ground blocked about one cell in three (walls of 1 cell), 0-3 plates flat or sloped, near the ground at
        // one end so seams appear.
        private static LayeredNav Seeded(Random rnd)
        {
            int w = rnd.Next(3, 15), d = rnd.Next(3, 15);
            var groundCost = new byte[w * d];
            for (int k = 0; k < groundCost.Length; k++) groundCost[k] = rnd.Next(3) == 0 ? (byte)0 : (byte)1;
            var slabs = new List<PlateSlab>();
            var costs = new List<byte[]>();
            int count = rnd.Next(4), solidCells = 0;
            for (int k = 0; k < count; k++)
            {
                int pw = rnd.Next(1, Math.Min(w, 6) + 1), pd = rnd.Next(1, Math.Min(d, 6) + 1);
                int px = rnd.Next(0, w - pw + 1), pz = rnd.Next(0, d - pd + 1);
                long from = new long[] { 0, 16384, 19660, 65536 }[rnd.Next(4)];
                int axis = rnd.Next(3);
                var points = new int[(pw + 1) * (pd + 1)];
                for (int j = 0; j <= pd; j++)
                    for (int i = 0; i <= pw; i++) points[j * (pw + 1) + i] = (int)(from + 16384 * (axis == 1 ? i : axis == 2 ? j : 0));
                var solid = new bool[pw * pd];
                var cost = new byte[pw * pd];
                for (int c = 0; c < solid.Length; c++)
                {
                    solid[c] = rnd.Next(6) != 0;
                    if (solid[c] && rnd.Next(5) != 0) cost[c] = 1;
                }
                int cells = solid.Count(s => s);
                if (solidCells + cells > w * d) break;
                solidCells += cells;
                slabs.Add(new PlateSlab((ulong)k + 1, px, pz, pw, pd, points, 32768, solid));
                costs.Add(cost);
            }
            return new LayeredNav(Ground(w, d, groundCost), 0, slabs, costs.ToArray(), costs.Select(Speeds).ToArray());
        }

        // The slow search: every cell's run found by walking from that cell, passages by a flood over overlapping narrow rows,
        // seams joined by a union-find; the same rules, written apart.
        private static class Slow
        {
            private static bool Walk(LayeredNav nav, int layer, int x, int z, out int node) => nav.TryNode(layer, x, z, out node) && nav.IsPassable(node);

            private static List<int> Step(LayeredNav nav, int node, int dx, int dz)
            {
                var list = new List<int>();
                int x = nav.XOf(node) + dx, z = nav.ZOf(node) + dz;
                if (Walk(nav, nav.LayerOf(node), x, z, out int same)) list.Add(same);
                for (int k = 0; k < nav.SeamCount(node); k++)
                {
                    int o = nav.Seam(node, k);
                    if (nav.XOf(o) == x && nav.ZOf(o) == z) list.Add(o);
                }
                return list;
            }

            private sealed class RunRow
            {
                public int Layer, Line, From, To;
                public List<int> Nodes = new List<int>();
            }

            internal static List<string> Entries(LayeredNav nav, ref int corridors)
            {
                var all = new List<(EntryKind kind, int width, int[] own, int[] a, int[] b, int pass)>();
                for (int pass = 0; pass < 2; pass++)
                {
                    bool alongX = pass == 0;
                    int ux = alongX ? 1 : 0, uz = 1 - ux;
                    var rows = new List<RunRow>();
                    var seen = new HashSet<int>();
                    for (int layer = 0; layer < nav.LayerCount; layer++)
                        for (int z = 0; z < nav.Depth; z++)
                            for (int x = 0; x < nav.Width; x++)
                            {
                                if (!Walk(nav, layer, x, z, out int start) || seen.Contains(start)) continue;
                                // Walk back to the run's first cell, then forward to its last.
                                int fx = x, fz = z;
                                while (Walk(nav, layer, fx - ux, fz - uz, out _)) { fx -= ux; fz -= uz; }
                                var r = new RunRow { Layer = layer, Line = alongX ? z : x, From = alongX ? fx : fz };
                                int cx = fx, cz = fz;
                                while (Walk(nav, layer, cx, cz, out int n)) { r.Nodes.Add(n); seen.Add(n); cx += ux; cz += uz; }
                                r.To = r.From + r.Nodes.Count - 1;
                                bool narrow = r.Nodes.Count <= 2
                                    && Step(nav, r.Nodes[0], -ux, -uz).Count == 0 && Step(nav, r.Nodes[r.Nodes.Count - 1], ux, uz).Count == 0
                                    && r.Nodes.Any(n => Step(nav, n, -uz, -ux).Count > 0) && r.Nodes.Any(n => Step(nav, n, uz, ux).Count > 0);
                                if (narrow) rows.Add(r);
                            }
                    var done = new bool[rows.Count];
                    for (int i = 0; i < rows.Count; i++)
                    {
                        if (done[i]) continue;
                        var group = new List<RunRow>();
                        var stack = new Stack<int>();
                        stack.Push(i); done[i] = true;
                        while (stack.Count > 0)
                        {
                            RunRow r = rows[stack.Pop()];
                            group.Add(r);
                            for (int j = 0; j < rows.Count; j++)
                            {
                                RunRow o = rows[j];
                                if (done[j] || o.Layer != r.Layer || Math.Abs(o.Line - r.Line) != 1 || o.To < r.From || r.To < o.From) continue;
                                done[j] = true; stack.Push(j);
                            }
                        }
                        group = group.OrderBy(g => g.Line).ToList();
                        for (int k = 1; k < group.Count; k++) Assert.That(group[k].Line, Is.EqualTo(group[k - 1].Line + 1), "a passage is one row a line");
                        int[] Out(RunRow r, int s) => r.Nodes.SelectMany(n => Step(nav, n, s * uz, s * ux)).Distinct().OrderBy(v => v).ToArray();
                        int[] Own(RunRow r) => r.Nodes.OrderBy(v => v).ToArray();
                        RunRow first = group[0], last = group[group.Count - 1];
                        if (group.Count <= 2)
                            all.Add((EntryKind.Narrow, group.Min(g => g.Nodes.Count), group.SelectMany(g => g.Nodes).OrderBy(v => v).ToArray(), Out(first, -1), Out(last, 1), pass));
                        else
                        {
                            corridors++;
                            all.Add((EntryKind.Narrow, first.Nodes.Count, Own(first), Out(first, -1), Own(group[1]), pass));
                            all.Add((EntryKind.Narrow, last.Nodes.Count, Own(last), Own(group[group.Count - 2]), Out(last, 1), pass));
                        }
                    }
                }
                // Seams: once each from the lower layer; joined when the same layers, the same way across, the same line, next to each other.
                var seams = new List<(int a, int b)>();
                for (int a = 0; a < nav.Count; a++)
                    for (int k = 0; k < nav.SeamCount(a); k++)
                    {
                        int b = nav.Seam(a, k);
                        if (nav.LayerOf(a) < nav.LayerOf(b)) seams.Add((a, b));
                    }
                var parent = Enumerable.Range(0, seams.Count).ToArray();
                int Find(int i) => parent[i] == i ? i : parent[i] = Find(parent[i]);
                for (int i = 0; i < seams.Count; i++)
                    for (int j = 0; j < seams.Count; j++)
                    {
                        var (a1, b1) = seams[i]; var (a2, b2) = seams[j];
                        int dx1 = nav.XOf(b1) - nav.XOf(a1), dz1 = nav.ZOf(b1) - nav.ZOf(a1), dx2 = nav.XOf(b2) - nav.XOf(a2), dz2 = nav.ZOf(b2) - nav.ZOf(a2);
                        if (nav.LayerOf(a1) != nav.LayerOf(a2) || nav.LayerOf(b1) != nav.LayerOf(b2) || dx1 != dx2 || dz1 != dz2) continue;
                        // Next to each other across the way: along z when the way is along x, and the other way round.
                        int ox = nav.XOf(a2) - nav.XOf(a1), oz = nav.ZOf(a2) - nav.ZOf(a1);
                        bool beside = dx1 != 0 ? ox == 0 && Math.Abs(oz) == 1 : oz == 0 && Math.Abs(ox) == 1;
                        if (beside) parent[Find(i)] = Find(j);
                    }
                foreach (var g in Enumerable.Range(0, seams.Count).GroupBy(Find))
                {
                    int[] lower = g.Select(i => seams[i].a).OrderBy(v => v).ToArray(), upper = g.Select(i => seams[i].b).OrderBy(v => v).ToArray();
                    all.Add((EntryKind.Seam, lower.Length, lower.Concat(upper).OrderBy(v => v).ToArray(), lower, upper, -1));
                }
                // Keep: seam first, then the smaller smaller-representative, the smaller other one, the row search first, the smaller own node;
                // a duplicate shares a side and an own node with one kept.
                var ordered = all.Select(e => (e.kind, e.width, e.own, a: e.a[0] <= e.b[0] ? e.a : e.b, b: e.a[0] <= e.b[0] ? e.b : e.a, e.pass))
                    .OrderBy(e => e.kind == EntryKind.Seam ? 0 : 1).ThenBy(e => e.a[0]).ThenBy(e => e.b[0]).ThenBy(e => e.pass).ThenBy(e => e.own[0]).ToList();
                var kept = new List<string>();
                var taken = new List<(int[] a, int[] b, int[] own)>();
                foreach (var e in ordered)
                {
                    bool twice = taken.Any(k => (k.a.SequenceEqual(e.a) || k.a.SequenceEqual(e.b) || k.b.SequenceEqual(e.a) || k.b.SequenceEqual(e.b))
                        && k.own.Intersect(e.own).Any());
                    if (twice) continue;
                    taken.Add((e.a, e.b, e.own));
                    kept.Add(Key(e.kind, e.width, e.own) + " A" + e.a[0] + " B" + e.b[0]);
                }
                return kept;
            }
        }
    }
}
