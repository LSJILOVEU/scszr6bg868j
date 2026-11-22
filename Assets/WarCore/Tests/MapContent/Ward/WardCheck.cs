using System;
using System.Collections.Generic;
using System.Linq;
using WarCore.Content;
using WarCore.Content.MapTools;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // MAP-BL0: the card's map checks (W3, W4 and rules 1 to 6) run on a layout and the document built from it. Each finding
    // is one line that starts with its item ("W3", "W4", "rule 1" .. "rule 6"). The expected numbers are the card's, written
    // here again, not read from WardLayout.
    internal static class WardCheck
    {
        internal const int Side = 160;
        internal const long Top = 196608, Low = 65536, Mid = 131072, Sill = 58982;

        internal sealed class Piece
        {
            internal readonly string Kind;
            internal readonly int X0, Z0, X1, Z1;
            internal readonly bool Small;
            internal Piece(string kind, WardBox b, bool small) { Kind = kind; X0 = b.X; Z0 = b.Z; X1 = b.X1; Z1 = b.Z1; Small = small; }
            public override string ToString() => Kind + " (" + X0 + ", " + Z0 + ")-(" + X1 + ", " + Z1 + ")";
        }

        internal static List<Piece> Pieces(WardLayout layout)
        {
            var all = new List<Piece>();
            foreach (WardBox b in WardLayout.WardWalls) all.Add(new Piece("ward-wall", b, false));
            foreach (WardShop s in layout.Shops) all.Add(new Piece("shop", s.Box, false));
            foreach (WardBox b in layout.Houses) all.Add(new Piece("solid-house", b, false));
            foreach (WardBox b in layout.Stalls) all.Add(new Piece("stall", b, true));
            foreach (WardBox b in layout.Handcarts) all.Add(new Piece("handcart", b, true));
            foreach (WardBox b in layout.Oxcarts) all.Add(new Piece("oxcart", b, true));
            foreach (WardBox b in layout.Wells) all.Add(new Piece("well", b, true));
            foreach (WardBox b in layout.Trees) all.Add(new Piece("tree", b, true));
            return all;
        }

        internal static NavGrid Nav(MapDocument doc)
        {
            MapBakeResult baked = MapBake.Run(doc.Save());
            return TerrainDerivation.Derive(WcMapReader.Read(baked.WcMapBytes), WardWorld.Pack).Nav;
        }

        internal static List<string> Run(WardLayout layout, MapDocument doc)
        {
            var found = new List<string>();
            NavGrid nav = Nav(doc);
            List<Piece> pieces = Pieces(layout);
            IReadOnlyList<MapWallInfo> walls = doc.Walls();
            Skeleton(walls, nav, found);
            Counts(layout, walls, found);
            ShopFaces(layout, walls, found);
            Rule1(walls, found);
            Rule2(pieces, found);
            Rule3(layout, pieces, nav, found);
            Rule4(doc, pieces, nav, found);
            Rule5(walls, found);
            Rule6(nav, found);
            return found;
        }

        internal static bool InStreet(int c) => c >= 73 && c <= 86;
        internal static bool InCarriage(int c) => c >= 77 && c <= 82;
        internal static bool InBand(int c) => (c >= 73 && c <= 76) || (c >= 83 && c <= 86);
        internal static bool InRing(int c) => c <= 7 || c >= 152;
        internal static bool InLane(int c) => (c >= 10 && c <= 13) || (c >= 146 && c <= 149);
        internal static bool InSquare(int x, int z) => x >= 68 && x <= 91 && z >= 111 && z <= 134;

        internal static IEnumerable<(int X, int Z)> Cells(MapWallInfo w)
        {
            for (int x = w.CellX; x < w.CellX + w.SizeX; x++)
                for (int z = w.CellZ; z < w.CellZ + w.SizeZ; z++) yield return (x, z);
        }

        // W3: the ward wall, the gates, and the places that hold no wall (ring road, lane, carriageway) or only small pieces
        // (the square).
        private static readonly (int X, int Z, int W, int D)[] WardWallTable =
        {
            (8, 8, 2, 70), (8, 82, 2, 70), (150, 8, 2, 70), (150, 82, 2, 70), (10, 8, 68, 2), (82, 8, 68, 2), (10, 150, 68, 2), (82, 150, 68, 2),
        };

        private static void Skeleton(IReadOnlyList<MapWallInfo> walls, NavGrid nav, List<string> found)
        {
            for (int i = 0; i < WardWallTable.Length; i++)
            {
                MapWallInfo w = walls.FirstOrDefault(v => v.Id == (ulong)(i + 1));
                var want = WardWallTable[i];
                if (w == null || (w.CellX, w.CellZ, w.SizeX, w.SizeZ) != want || w.HeightRaw != Top || w.HasBase || w.Kind != "ward-wall")
                    found.Add("W3: ward wall " + (i + 1) + " is not the card's");
            }
            if (walls.Count(w => w.Kind == "ward-wall") != 8) found.Add("W3: " + walls.Count(w => w.Kind == "ward-wall") + " ward wall pieces");
            for (int a = 78; a <= 81; a++)
                for (int d = 8; d <= 9; d++)
                    foreach ((int x, int z) in new[] { (d, a), (Side - 1 - d, a), (a, d), (a, Side - 1 - d) })
                        if (!nav.IsPassable(x, z)) found.Add("W3: gate cell (" + x + ", " + z + ") is not walkable");
            foreach (MapWallInfo w in walls)
            {
                if (w.Kind == "ward-wall") continue;
                foreach ((int x, int z) in Cells(w))
                {
                    if (InRing(x) || InRing(z)) found.Add("W3: wall " + w.Id + " on the ring road at (" + x + ", " + z + ")");
                    else if (InLane(x) || InLane(z)) found.Add("W3: wall " + w.Id + " in the lane at (" + x + ", " + z + ")");
                    else if (InCarriage(x) || InCarriage(z)) found.Add("W3: wall " + w.Id + " on the carriageway at (" + x + ", " + z + ")");
                    if (InSquare(x, z) && (w.Kind == "wall" || w.Kind == "solid-house"))
                        found.Add("W3: " + w.Kind + " " + w.Id + " in the square at (" + x + ", " + z + ")");
                }
            }
        }

        // W4: counts in the card's ranges and equal to the layout; the walls by kind match the layout.
        private static void Counts(WardLayout layout, IReadOnlyList<MapWallInfo> walls, List<string> found)
        {
            Range(found, "shops", layout.Shops.Count, 18, 24);
            Range(found, "solid houses", layout.Houses.Count, 8, 24);
            Range(found, "stalls", layout.Stalls.Count, 30, 40);
            Range(found, "carts", layout.Handcarts.Count + layout.Oxcarts.Count, 8, 12);
            Range(found, "ox carts", layout.Oxcarts.Count, 3, int.MaxValue);
            Range(found, "wells", layout.Wells.Count, 2, 3);
            Range(found, "trees", layout.Trees.Count, 20, 30);
            Same(found, walls, "wall", 10 * layout.Shops.Count);
            Same(found, walls, "solid-house", layout.Houses.Count);
            Same(found, walls, "stall", layout.Stalls.Count);
            Same(found, walls, "handcart", layout.Handcarts.Count);
            Same(found, walls, "oxcart", layout.Oxcarts.Count);
            Same(found, walls, "well", layout.Wells.Count);
            Same(found, walls, "tree", layout.Trees.Count);
            if (walls.Count != 8 + 10 * layout.Shops.Count + layout.Houses.Count + layout.Stalls.Count + layout.Handcarts.Count
                + layout.Oxcarts.Count + layout.Wells.Count + layout.Trees.Count)
                found.Add("W4: " + walls.Count + " walls, not the layout's sum");
            // The size and height of every hand-added piece, kind by kind, as the card's table.
            SizeAndHeight(found, walls, "solid-house", Top, layout.Houses, b => Math.Min(b.W, b.D) >= 6);
            SizeAndHeight(found, walls, "stall", Low, layout.Stalls, b => (b.W == 2 && b.D == 1) || (b.W == 1 && b.D == 2));
            SizeAndHeight(found, walls, "handcart", Low, layout.Handcarts, b => (b.W == 2 && b.D == 1) || (b.W == 1 && b.D == 2));
            SizeAndHeight(found, walls, "oxcart", Mid, layout.Oxcarts, b => (b.W == 3 && b.D == 2) || (b.W == 2 && b.D == 3));
            SizeAndHeight(found, walls, "well", Low, layout.Wells, b => b.W == 1 && b.D == 1);
            SizeAndHeight(found, walls, "tree", Top, layout.Trees, b => b.W == 1 && b.D == 1);
        }

        private static void Range(List<string> found, string what, int n, int low, int high)
        {
            if (n < low || n > high) found.Add("W4: " + n + " " + what + ", the card allows " + low + ".." + (high == int.MaxValue ? "" : high.ToString()));
        }

        private static void Same(List<string> found, IReadOnlyList<MapWallInfo> walls, string kind, int want)
        {
            int n = walls.Count(w => w.Kind == kind);
            if (n != want) found.Add("W4: " + n + " walls of kind " + kind + ", the layout makes " + want);
        }

        private static void SizeAndHeight(List<string> found, IReadOnlyList<MapWallInfo> walls, string kind, long height, List<WardBox> boxes,
            Func<WardBox, bool> size)
        {
            var mine = walls.Where(w => w.Kind == kind).ToList();
            for (int i = 0; i < mine.Count; i++)
            {
                MapWallInfo w = mine[i];
                if (w.HeightRaw != height || w.HasBase) found.Add("W4: " + kind + " " + w.Id + " is " + w.HeightRaw + " raw high");
                var b = new WardBox(w.CellX, w.CellZ, w.SizeX, w.SizeZ);
                if (!size(b)) found.Add("W4: " + kind + " " + w.Id + " is " + b.W + "x" + b.D);
                if (i < boxes.Count && !boxes[i].Equals(b)) found.Add("W4: " + kind + " " + w.Id + " is not at the layout's place");
            }
        }

        // W4: each shop holds exactly one door part and one window part (by stamp), on its street face; the door's outside
        // cell is in a street band; the plain shop walls are the full height; the prefab pieces are the card's heights.
        private static void ShopFaces(WardLayout layout, IReadOnlyList<MapWallInfo> walls, List<string> found)
        {
            foreach (WardShop s in layout.Shops)
            {
                WardBox box = s.Box;
                var inside = walls.Where(w => w.Kind == "wall" && w.CellX >= box.X && w.CellX + w.SizeX - 1 <= box.X1 && w.CellZ >= box.Z
                    && w.CellZ + w.SizeZ - 1 <= box.Z1).ToList();
                var doors = inside.Where(w => w.Stamp.StartsWith("door-", StringComparison.Ordinal)).GroupBy(w => w.Stamp).ToList();
                var windows = inside.Where(w => w.Stamp.StartsWith("window-", StringComparison.Ordinal)).GroupBy(w => w.Stamp).ToList();
                if (doors.Count != 1 || windows.Count != 1)
                {
                    found.Add("W4: shop " + s + " has " + doors.Count + " door parts and " + windows.Count + " window parts");
                    continue;
                }
                MapWallInfo lintel = doors[0].SingleOrDefault(w => w.HasBase);
                if (lintel == null || (lintel.CellX, lintel.CellZ) != s.Door) found.Add("W4: shop " + s + " has its door off the street face");
                MapWallInfo high = windows[0].SingleOrDefault(w => w.HasBase);
                if (high == null || (high.CellX, high.CellZ) != s.Window) found.Add("W4: shop " + s + " has its window off the street face");
                (int ox, int oz) = s.Outside(1);
                if (!(s.AlongZ ? InBand(ox) : InBand(oz)))
                    found.Add("W4: shop " + s + " has its door outside cell off the street bands");
                foreach (MapWallInfo w in doors[0])
                    if (w.HasBase ? (w.HeightRaw, w.BaseRaw) != (Low, Mid) : w.HeightRaw != Top)
                        found.Add("W4: door piece " + w.Id + " of shop " + s + " is " + w.HeightRaw + " raw high on " + w.BaseRaw);
                foreach (MapWallInfo w in windows[0])
                {
                    bool sill = !w.HasBase && w.SizeX == 1 && w.SizeZ == 1 && (w.CellX, w.CellZ) == s.Window;
                    if (w.HasBase ? (w.HeightRaw, w.BaseRaw) != (Low, Mid) : w.HeightRaw != (sill ? Sill : Top))
                        found.Add("W4: window piece " + w.Id + " of shop " + s + " is " + w.HeightRaw + " raw high on " + w.BaseRaw);
                }
                foreach (MapWallInfo w in inside.Where(v => v.Stamp == ""))
                    if (w.HeightRaw != Top || w.HasBase) found.Add("W4: shop wall " + w.Id + " is " + w.HeightRaw + " raw high");
                if (inside.Count != 10) found.Add("W4: shop " + s + " has " + inside.Count + " walls");
                // The floor walls close the whole ring of the footprint but the doorway, and leave the inside free.
                var floor = new HashSet<(int, int)>(inside.Where(w => !w.HasBase).SelectMany(Cells));
                for (int x = box.X; x <= box.X1; x++)
                    for (int z = box.Z; z <= box.Z1; z++)
                    {
                        bool ring = x == box.X || x == box.X1 || z == box.Z || z == box.Z1;
                        bool want = ring && (x, z) != s.Door;
                        if (floor.Contains((x, z)) != want)
                            found.Add("W4: shop " + s + " cell (" + x + ", " + z + ") is " + (want ? "open" : "walled"));
                    }
            }
        }

        // Rule 1: no two floor walls share a cell (lintels are raised walls and may sit on a sill or above a doorway).
        private static void Rule1(IReadOnlyList<MapWallInfo> walls, List<string> found)
        {
            var owner = new Dictionary<(int, int), ulong>();
            foreach (MapWallInfo w in walls)
            {
                if (w.HasBase) continue;
                foreach (var c in Cells(w))
                {
                    if (owner.TryGetValue(c, out ulong other)) found.Add("rule 1: walls " + other + " and " + w.Id + " share (" + c.Item1 + ", " + c.Item2 + ")");
                    else owner[c] = w.Id;
                }
            }
        }

        // Rule 2: no one-cell gap between two pieces. gap = max(gx, gz) on the bounding boxes; 1 is refused, 0 touches.
        internal static int Gap(Piece a, Piece b)
        {
            int gx = Math.Max(0, Math.Max(b.X0 - a.X1 - 1, a.X0 - b.X1 - 1));
            int gz = Math.Max(0, Math.Max(b.Z0 - a.Z1 - 1, a.Z0 - b.Z1 - 1));
            return Math.Max(gx, gz);
        }

        private static void Rule2(List<Piece> pieces, List<string> found)
        {
            for (int i = 0; i < pieces.Count; i++)
                for (int j = i + 1; j < pieces.Count; j++)
                    if (Gap(pieces[i], pieces[j]) == 1) found.Add("rule 2: one-cell gap between " + pieces[i] + " and " + pieces[j]);
        }

        // Rule 3: in front of and behind every door the cells are walkable and joined on the walking grid; nothing small
        // stands on the two cells straight out of the door.
        private static void Rule3(WardLayout layout, List<Piece> pieces, NavGrid nav, List<string> found)
        {
            foreach (WardShop s in layout.Shops)
            {
                (int ox, int oz) = s.Outside(1);
                (int ix, int iz) = s.Inside;
                if (!Passable(nav, ox, oz) || !Passable(nav, ix, iz) || !Joined(nav, (ox, oz), (ix, iz)))
                    found.Add("rule 3: the door of shop " + s + " does not lead in");
                for (int step = 1; step <= 2; step++)
                {
                    (int x, int z) = s.Outside(step);
                    foreach (Piece p in pieces)
                        if (p.Small && x >= p.X0 && x <= p.X1 && z >= p.Z0 && z <= p.Z1) found.Add("rule 3: " + p + " stands in front of the door of shop " + s);
                }
            }
        }

        private static bool Passable(NavGrid nav, int x, int z) => x >= 0 && z >= 0 && x < nav.Width && z < nav.Depth && nav.IsPassable(x, z);

        private static bool Joined(NavGrid nav, (int, int) from, (int, int) to)
        {
            var seen = new HashSet<(int, int)> { from };
            var open = new Queue<(int, int)>();
            open.Enqueue(from);
            while (open.Count > 0)
            {
                (int x, int z) = open.Dequeue();
                if ((x, z) == to) return true;
                foreach ((int nx, int nz) in new[] { (x + 1, z), (x - 1, z), (x, z + 1), (x, z - 1) })
                    if (Passable(nav, nx, nz) && seen.Add((nx, nz))) open.Enqueue((nx, nz));
            }
            return false;
        }

        // Rule 4: no small piece within 8 m of a point centre, on the four 4 x 4 deploy grids or the two starting squads
        // (each grown by 1 m), or in a gate and 2 cells either side of it; every one of the 16 deploy places of each grid stands.
        internal static readonly long[] GridOffsets = { -2250, -750, 750, 2250 };
        private static readonly (long X, long Z)[] PointCentres = { (80000, 80000), (80000, 123000) };
        private static readonly (long X, long Z)[] Entries = { (4000, 80000), (156000, 80000), (80000, 80000), (80000, 123000) };

        private static void Rule4(MapDocument doc, List<Piece> pieces, NavGrid nav, List<string> found)
        {
            var zones = new List<(string, long, long, long, long)>();
            foreach ((long x, long z) in Entries) zones.Add(("deploy grid at " + x + ", " + z, x - 3250, z - 3250, x + 3250, z + 3250));
            for (int team = 1; team <= 2; team++)
            {
                var mine = doc.Units().Where(u => u.Team == team && !u.Reserve).ToList();
                if (mine.Count == 0) continue;
                zones.Add(("starting squad " + team, mine.Min(u => u.XMm) - 1000, mine.Min(u => u.ZMm) - 1000, mine.Max(u => u.XMm) + 1000, mine.Max(u => u.ZMm) + 1000));
            }
            foreach ((int gx0, int gz0, int gx1, int gz1) in new[] { (6, 78, 11, 81), (148, 78, 153, 81), (78, 6, 81, 11), (78, 148, 81, 153) })
                zones.Add(("gate", gx0 * 1000L, gz0 * 1000L, (gx1 + 1) * 1000L, (gz1 + 1) * 1000L));
            foreach (Piece p in pieces.Where(v => v.Small))
            {
                long x0 = p.X0 * 1000L, z0 = p.Z0 * 1000L, x1 = (p.X1 + 1) * 1000L, z1 = (p.Z1 + 1) * 1000L;
                foreach ((long cx, long cz) in PointCentres)
                {
                    long dx = Math.Max(0, Math.Max(x0 - cx, cx - x1)), dz = Math.Max(0, Math.Max(z0 - cz, cz - z1));
                    if (dx * dx + dz * dz < 8000L * 8000L) found.Add("rule 4: " + p + " is within 8 m of the point at " + cx + ", " + cz);
                }
                foreach ((string what, long ax0, long az0, long ax1, long az1) in zones)
                    if (x0 < ax1 && x1 > ax0 && z0 < az1 && z1 > az0) found.Add("rule 4: " + p + " is in the " + what);
            }
            SimFix foot = SimFix.FromRaw(MapGate.FootprintRaw);
            foreach ((long x, long z) in Entries)
                foreach (long ox in GridOffsets)
                    foreach (long oz in GridOffsets)
                    {
                        var at = new SimVec3(SimFix.Ratio(x + ox, 1000), SimFix.FromRaw(0), SimFix.Ratio(z + oz, 1000));
                        if (!nav.SegmentClear(at, at, foot)) found.Add("rule 4: deploy place " + (x + ox) + ", " + (z + oz) + " does not stand");
                    }
        }

        // Rule 5: the hand-added walls are 1, 2 or 3 m high on the floor; the prefab pieces are checked as prefab pieces.
        private static void Rule5(IReadOnlyList<MapWallInfo> walls, List<string> found)
        {
            foreach (MapWallInfo w in walls)
            {
                if (w.Stamp != "") continue;
                if (w.HasBase || (w.HeightRaw != Low && w.HeightRaw != Mid && w.HeightRaw != Top))
                    found.Add("rule 5: wall " + w.Id + " (" + w.Kind + ") is " + w.HeightRaw + " raw high");
            }
            foreach (MapWallInfo w in walls.Where(v => v.Stamp != ""))
            {
                bool ok = w.HasBase ? w.HeightRaw == Low && w.BaseRaw == Mid
                    : w.HeightRaw == Top || (w.Stamp.StartsWith("window-", StringComparison.Ordinal) && w.HeightRaw == Sill);
                if (!ok) found.Add("rule 5: prefab piece " + w.Id + " (" + w.Stamp + ") is " + w.HeightRaw + " raw high on " + w.BaseRaw);
            }
        }

        // Rule 6: the walkable cells are one piece (4-neighbour); in every block a path that stays in the block runs from its
        // main street edge to its lane edge.
        private static void Rule6(NavGrid nav, List<string> found)
        {
            int total = 0;
            (int, int)? first = null;
            for (int x = 0; x < Side; x++)
                for (int z = 0; z < Side; z++)
                    if (nav.IsPassable(x, z)) { total++; if (first == null) first = (x, z); }
            int reached = first == null ? 0 : Reach(nav, new[] { first.Value }, (x, z) => true).Count;
            if (reached != total) found.Add("rule 6: " + (total - reached) + " walkable cells are cut off");
            foreach ((int x0, int x1, int z0, int z1, int sx, int sz, int lx, int lz) in new[]
            {
                (14, 72, 14, 72, 72, 72, 14, 14), (87, 145, 14, 72, 87, 72, 145, 14), (14, 72, 87, 145, 72, 87, 14, 145), (87, 145, 87, 145, 87, 87, 145, 145),
            })
            {
                Func<int, int, bool> inBlock = (x, z) => x >= x0 && x <= x1 && z >= z0 && z <= z1;
                var starts = new List<(int, int)>();
                for (int a = x0; a <= x1; a++) if (Passable(nav, a, sz)) starts.Add((a, sz));
                for (int c = z0; c <= z1; c++) if (Passable(nav, sx, c)) starts.Add((sx, c));
                HashSet<(int, int)> seen = Reach(nav, starts, inBlock);
                bool lane = seen.Any(c => c.Item1 == lx || c.Item2 == lz);
                if (!lane) found.Add("rule 6: the block at " + x0 + ", " + z0 + " has no way from the main street to the lane");
            }
        }

        private static HashSet<(int, int)> Reach(NavGrid nav, IEnumerable<(int, int)> starts, Func<int, int, bool> allowed)
        {
            var seen = new HashSet<(int, int)>();
            var open = new Queue<(int, int)>();
            foreach (var s in starts) if (seen.Add(s)) open.Enqueue(s);
            while (open.Count > 0)
            {
                (int x, int z) = open.Dequeue();
                foreach ((int nx, int nz) in new[] { (x + 1, z), (x - 1, z), (x, z + 1), (x, z - 1) })
                    if (Passable(nav, nx, nz) && allowed(nx, nz) && seen.Add((nx, nz))) open.Enqueue((nx, nz));
            }
            return seen;
        }
    }
}
