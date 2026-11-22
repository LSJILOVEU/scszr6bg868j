using System;
using System.Collections.Generic;

namespace WarCore.Sim
{
    // TRV-7a: what an entry is. Narrow: a run of at most two walkable cells shut at both ends (a gap in a wall, the end of a
    // corridor). Seam: a row of seams between the same two layers crossing the same way (the foot or the top of a stair).
    public enum EntryKind : byte
    {
        Narrow = 1,
        Seam = 2,
    }

    // One entry of the map. Side A holds the smaller of the two side representatives (each side's smallest node). The points
    // are the representatives' cell middles on their layers (EntryTable.Centre). Width: how many cells wide it is (static; it
    // says nothing of who is walking through).
    public readonly struct EntryInfo
    {
        public readonly EntryKind Kind;
        public readonly int Width;
        public readonly int NodeA, NodeB, LayerA, LayerB;
        public readonly SimVec3 PointA, PointB;

        public EntryInfo(EntryKind kind, int width, int nodeA, int nodeB, int layerA, int layerB, SimVec3 pointA, SimVec3 pointB)
        {
            Kind = kind; Width = width; NodeA = nodeA; NodeB = nodeB; LayerA = layerA; LayerB = layerB; PointA = pointA; PointB = pointB;
        }
    }

    // TRV-7a (K19): the entries of a map with floor plates, found from the walkable graph alone, once (the graph never changes).
    //
    // Narrow entries. A run is a row (or column) of consecutive walkable nodes of one layer. It is shut at an end when the cell
    // past that end is walkable on no layer reached from the end node (off the map, off the plate, a hole, a blocked cell; a
    // cell of another layer joined by a seam is walkable, and then the run is not shut). A narrow row is a run at most
    // MaxNarrowWidth wide, shut at both ends, with a walkable neighbour (its own layer or a seam) in front of one of its cells
    // and one behind one of its cells. Narrow rows of the same layer on neighbouring lines that overlap across join into a
    // passage. A passage at most MaxDoorRows long is one entry (its sides: the walkable neighbours in front of its first row and
    // behind its last; its width: its narrowest row). A longer one (a corridor) gives an entry at each end row (sides: the
    // neighbours outside and the next row inside; width: that row's). Rows are searched along x, then along z.
    //
    // Seam entries. Seams between the same two layers crossing the same way, next to each other across that way, form one entry;
    // its sides are the nodes of each layer, its width the number of seams.
    //
    // Duplicates: two entries with an equal side (the same set of nodes) that also share an own node (the same crossing found
    // twice) keep one: a seam entry before a narrow one, then the smaller representative, then the smaller other representative,
    // then the row search before the column search, then the smaller own node. (The two ends of a corridor three rows long share
    // their inner side, the middle row, but no own node: both stay.)
    //
    // Order: by (NodeA, NodeB), then kind, then the entry's smallest own node. The index is the map's entry number.
    public sealed class EntryTable
    {
        public const int AlgorithmVersion = 1;
        public const int MaxNarrowWidth = 2;
        public const int MaxDoorRows = 2;

        private readonly EntryInfo[] entries;
        // The entry's own nodes (a narrow entry's row or rows; a seam entry's nodes on both layers), ascending.
        private readonly int[] ownStart, own;

        private EntryTable(EntryInfo[] entries, int[] ownStart, int[] own)
        {
            this.entries = entries; this.ownStart = ownStart; this.own = own;
        }

        public int Count => entries.Length;

        public EntryInfo this[int entry] => entries[entry];

        public int OwnCount(int entry) => ownStart[entry + 1] - ownStart[entry];

        public int Own(int entry, int k)
        {
            if (k < 0 || k >= OwnCount(entry)) throw new ArgumentOutOfRangeException(nameof(k));
            return own[ownStart[entry] + k];
        }

        // Is the node one of the entry's own nodes?
        public bool Owns(int entry, int node)
        {
            int lo = ownStart[entry], hi = ownStart[entry + 1] - 1;
            while (lo <= hi)
            {
                int mid = lo + (hi - lo) / 2;
                if (own[mid] == node) return true;
                if (own[mid] < node) lo = mid + 1; else hi = mid - 1;
            }
            return false;
        }

        // The middle of the node's cell on its layer: x, z the cell's middle, y the layer's four corner heights added and divided
        // by 4, rounded down (a flat plate gives its top; a plate sloping along one axis the mean of its two grid lines).
        public static SimVec3 Centre(LayeredNav nav, int node)
        {
            if (nav == null) throw new ArgumentNullException(nameof(nav));
            int x = nav.XOf(node), z = nav.ZOf(node), layer = nav.LayerOf(node);
            long sum = Corner(nav, layer, x, z) + Corner(nav, layer, x + 1, z) + Corner(nav, layer, x, z + 1) + Corner(nav, layer, x + 1, z + 1);
            return nav.Ground.Nav.Center(z * nav.Width + x, SimFix.FromRaw(FloorQuarter(sum)));
        }

        internal static long FloorQuarter(long sum) => sum >= 0 ? sum / 4 : -((-sum + 3) / 4);

        private static long Corner(LayeredNav nav, int layer, int i, int j)
        {
            if (!nav.TryLayerHeight(layer, i, j, out long h)) throw new InvalidOperationException("A node's corner is off its layer.");
            return h;
        }

        public static EntryTable Build(LayeredNav nav)
        {
            if (nav == null) throw new ArgumentNullException(nameof(nav));
            var cells = new NavCells(nav);
            var found = new List<Candidate>();
            Narrow(cells, true, found);
            Narrow(cells, false, found);
            Seams(nav, found);
            // Duplicates: in priority order, keep an entry unless one of its sides equals a side of one kept already.
            found.Sort(Candidate.Priority);
            var sides = new Dictionary<int[], List<Candidate>>(new SetComparer());
            var kept = new List<Candidate>();
            foreach (Candidate c in found)
            {
                if (Duplicate(sides, c.SideA, c) || Duplicate(sides, c.SideB, c)) continue;
                Keep(sides, c.SideA, c); Keep(sides, c.SideB, c);
                kept.Add(c);
            }
            kept.Sort(Candidate.Order);
            var entries = new EntryInfo[kept.Count];
            var ownStart = new int[kept.Count + 1];
            for (int e = 0; e < kept.Count; e++) ownStart[e + 1] = ownStart[e] + kept[e].Own.Length;
            var own = new int[ownStart[kept.Count]];
            for (int e = 0; e < kept.Count; e++)
            {
                Candidate c = kept[e];
                entries[e] = new EntryInfo(c.Kind, c.Width, c.RepA, c.RepB, nav.LayerOf(c.RepA), nav.LayerOf(c.RepB), Centre(nav, c.RepA), Centre(nav, c.RepB));
                Array.Copy(c.Own, 0, own, ownStart[e], c.Own.Length);
            }
            return new EntryTable(entries, ownStart, own);
        }

        // A kept entry with this side shares an own node with the candidate: the same crossing found twice.
        private static bool Duplicate(Dictionary<int[], List<Candidate>> sides, int[] side, Candidate c)
        {
            if (!sides.TryGetValue(side, out List<Candidate> list)) return false;
            foreach (Candidate k in list)
                if (Overlap(k.Own, c.Own)) return true;
            return false;
        }

        private static void Keep(Dictionary<int[], List<Candidate>> sides, int[] side, Candidate c)
        {
            if (!sides.TryGetValue(side, out List<Candidate> list)) sides.Add(side, list = new List<Candidate>());
            list.Add(c);
        }

        // Two ascending sets share a value.
        private static bool Overlap(int[] a, int[] b)
        {
            int i = 0, j = 0;
            while (i < a.Length && j < b.Length)
            {
                if (a[i] == b[j]) return true;
                if (a[i] < b[j]) i++; else j++;
            }
            return false;
        }

        // Narrow rows along x (alongX) or along z, joined into passages. Works on "is this cell walkable" and "which walkable
        // nodes lie one step across" only (IEntryCells), so a map without plates can reuse it.
        internal static void Narrow(IEntryCells cells, bool alongX, List<Candidate> found)
        {
            int lines = alongX ? cells.Depth : cells.Width, length = alongX ? cells.Width : cells.Depth;
            int ux = alongX ? 1 : 0, uz = alongX ? 0 : 1, vx = uz, vz = ux;
            var across = new List<int>();
            for (int layer = 0; layer < cells.LayerCount; layer++)
            {
                // The narrow rows of the previous line and the passage each one belongs to.
                var previous = new List<Row>();
                var current = new List<Row>();
                var passages = new List<List<Row>>();
                for (int line = 0; line < lines; line++)
                {
                    current.Clear();
                    int p = 0;
                    while (p < length)
                    {
                        if (Walkable(cells, layer, alongX, line, p) < 0) { p++; continue; }
                        int p0 = p;
                        while (p + 1 < length && Walkable(cells, layer, alongX, line, p + 1) >= 0) p++;
                        int p1 = p++;
                        int width = p1 - p0 + 1;
                        if (width > MaxNarrowWidth) continue;
                        int first = Walkable(cells, layer, alongX, line, p0), last = Walkable(cells, layer, alongX, line, p1);
                        if (cells.AnyAcross(first, -ux, -uz) || cells.AnyAcross(last, ux, uz)) continue;
                        bool front = false, back = false;
                        for (int q = p0; q <= p1; q++)
                        {
                            int node = Walkable(cells, layer, alongX, line, q);
                            front |= cells.AnyAcross(node, -vx, -vz);
                            back |= cells.AnyAcross(node, vx, vz);
                        }
                        if (!front || !back) continue;
                        var row = new Row(line, p0, p1);
                        foreach (Row before in previous)
                            if (before.P0 <= p1 && p0 <= before.P1 && !before.Continued)
                            {
                                row.Passage = before.Passage;
                                before.Continued = true;
                                break;
                            }
                        if (row.Passage < 0) { row.Passage = passages.Count; passages.Add(new List<Row>()); }
                        passages[row.Passage].Add(row);
                        current.Add(row);
                    }
                    var swap = previous; previous = current; current = swap;
                }
                foreach (List<Row> rows in passages)
                {
                    int n = rows.Count;
                    int pass = alongX ? 0 : 1;
                    if (n <= MaxDoorRows)
                    {
                        var ownNodes = new List<int>();
                        int width = int.MaxValue;
                        foreach (Row r in rows) { AddRow(cells, layer, alongX, r, ownNodes); int w = r.P1 - r.P0 + 1; if (w < width) width = w; }
                        int[] a = Outside(cells, layer, alongX, rows[0], -vx, -vz, across), b = Outside(cells, layer, alongX, rows[n - 1], vx, vz, across);
                        found.Add(new Candidate(EntryKind.Narrow, width, Sorted(ownNodes), a, b, pass));
                    }
                    else
                    {
                        found.Add(new Candidate(EntryKind.Narrow, rows[0].P1 - rows[0].P0 + 1, RowNodes(cells, layer, alongX, rows[0]),
                            Outside(cells, layer, alongX, rows[0], -vx, -vz, across), RowNodes(cells, layer, alongX, rows[1]), pass));
                        found.Add(new Candidate(EntryKind.Narrow, rows[n - 1].P1 - rows[n - 1].P0 + 1, RowNodes(cells, layer, alongX, rows[n - 1]),
                            RowNodes(cells, layer, alongX, rows[n - 2]), Outside(cells, layer, alongX, rows[n - 1], vx, vz, across), pass));
                    }
                }
            }
        }

        // Seam entries: every seam once, from its lower layer, grouped by (lower layer, upper layer, way across, line) and run
        // across the way.
        private static void Seams(LayeredNav nav, List<Candidate> found)
        {
            var seams = new List<(int la, int lb, int way, int line, int across, int a, int b)>();
            for (int a = 0; a < nav.Count; a++)
                for (int s = nav.SeamStart[a]; s < nav.SeamStart[a + 1]; s++)
                {
                    int b = nav.Seams[s];
                    int la = nav.NodeLayer[a], lb = nav.NodeLayer[b];
                    if (la >= lb) continue;
                    int dx = nav.NodeX[b] - nav.NodeX[a], dz = nav.NodeZ[b] - nav.NodeZ[a];
                    // N, E, S, W.
                    int way = dz == 1 ? 0 : dx == 1 ? 1 : dz == -1 ? 2 : 3;
                    bool alongZ = dx != 0;
                    seams.Add((la, lb, way, alongZ ? nav.NodeX[a] : nav.NodeZ[a], alongZ ? nav.NodeZ[a] : nav.NodeX[a], a, b));
                }
            seams.Sort();
            int start = 0;
            for (int k = 1; k <= seams.Count; k++)
            {
                bool joined = k < seams.Count && seams[k].la == seams[k - 1].la && seams[k].lb == seams[k - 1].lb && seams[k].way == seams[k - 1].way
                    && seams[k].line == seams[k - 1].line && seams[k].across == seams[k - 1].across + 1;
                if (joined) continue;
                var lower = new List<int>();
                var upper = new List<int>();
                for (int m = start; m < k; m++) { lower.Add(seams[m].a); upper.Add(seams[m].b); }
                var all = new List<int>(lower); all.AddRange(upper);
                found.Add(new Candidate(EntryKind.Seam, k - start, Sorted(all), Sorted(lower), Sorted(upper), -1));
                start = k;
            }
        }

        private static int Walkable(IEntryCells cells, int layer, bool alongX, int line, int p) =>
            alongX ? cells.Walkable(layer, p, line) : cells.Walkable(layer, line, p);

        private static void AddRow(IEntryCells cells, int layer, bool alongX, Row r, List<int> into)
        {
            for (int q = r.P0; q <= r.P1; q++) into.Add(Walkable(cells, layer, alongX, r.Line, q));
        }

        private static int[] RowNodes(IEntryCells cells, int layer, bool alongX, Row r)
        {
            var list = new List<int>();
            AddRow(cells, layer, alongX, r, list);
            return Sorted(list);
        }

        // The walkable nodes one step from the row's cells the given way.
        private static int[] Outside(IEntryCells cells, int layer, bool alongX, Row r, int dx, int dz, List<int> across)
        {
            var list = new List<int>();
            for (int q = r.P0; q <= r.P1; q++)
            {
                across.Clear();
                cells.Across(Walkable(cells, layer, alongX, r.Line, q), dx, dz, across);
                list.AddRange(across);
            }
            return Sorted(list);
        }

        private static int[] Sorted(List<int> list)
        {
            list.Sort();
            var distinct = new List<int>(list.Count);
            foreach (int v in list)
                if (distinct.Count == 0 || distinct[distinct.Count - 1] != v) distinct.Add(v);
            return distinct.ToArray();
        }

        private sealed class Row
        {
            internal readonly int Line, P0, P1;
            internal int Passage = -1;
            internal bool Continued;
            internal Row(int line, int p0, int p1) { Line = line; P0 = p0; P1 = p1; }
        }

        internal sealed class Candidate
        {
            internal readonly EntryKind Kind;
            internal readonly int Width, Pass;
            internal readonly int[] Own, SideA, SideB;
            internal readonly int RepA, RepB;

            // Pass: 0 rows along x, 1 along z, -1 seams.
            internal Candidate(EntryKind kind, int width, int[] own, int[] one, int[] other, int pass)
            {
                Kind = kind; Width = width; Own = own; Pass = pass;
                if (one.Length == 0 || other.Length == 0) throw new InvalidOperationException("An entry side is empty.");
                bool swap = other[0] < one[0];
                SideA = swap ? other : one; SideB = swap ? one : other;
                RepA = SideA[0]; RepB = SideB[0];
            }

            internal static int Priority(Candidate x, Candidate y)
            {
                if (x.Kind != y.Kind) return x.Kind == EntryKind.Seam ? -1 : 1;
                int c = x.RepA.CompareTo(y.RepA);
                if (c == 0) c = x.RepB.CompareTo(y.RepB);
                if (c == 0) c = x.Pass.CompareTo(y.Pass);
                if (c == 0) c = x.Own[0].CompareTo(y.Own[0]);
                return c;
            }

            internal static int Order(Candidate x, Candidate y)
            {
                int c = x.RepA.CompareTo(y.RepA);
                if (c == 0) c = x.RepB.CompareTo(y.RepB);
                if (c == 0) c = ((int)x.Kind).CompareTo((int)y.Kind);
                if (c == 0) c = x.Own[0].CompareTo(y.Own[0]);
                if (c == 0) c = x.Pass.CompareTo(y.Pass);
                return c;
            }
        }

        private sealed class SetComparer : IEqualityComparer<int[]>
        {
            public bool Equals(int[] x, int[] y)
            {
                if (x.Length != y.Length) return false;
                for (int i = 0; i < x.Length; i++)
                    if (x[i] != y[i]) return false;
                return true;
            }

            public int GetHashCode(int[] set)
            {
                unchecked
                {
                    int h = set.Length;
                    foreach (int v in set) h = h * 31 + v;
                    return h;
                }
            }
        }

        // The layered graph as the narrow search reads it.
        private sealed class NavCells : IEntryCells
        {
            private readonly LayeredNav nav;
            internal NavCells(LayeredNav nav) { this.nav = nav; }
            public int Width => nav.Width;
            public int Depth => nav.Depth;
            public int LayerCount => nav.LayerCount;

            public int Walkable(int layer, int x, int z)
            {
                int node = nav.Node(layer, x, z);
                return node >= 0 && nav.Cost[node] != 0 ? node : -1;
            }

            public void Across(int node, int dx, int dz, List<int> into)
            {
                int x = nav.NodeX[node] + dx, z = nav.NodeZ[node] + dz;
                int same = Walkable(nav.NodeLayer[node], x, z);
                if (same >= 0) into.Add(same);
                for (int s = nav.SeamStart[node]; s < nav.SeamStart[node + 1]; s++)
                {
                    int other = nav.Seams[s];
                    if (nav.NodeX[other] == x && nav.NodeZ[other] == z) into.Add(other);
                }
            }

            public bool AnyAcross(int node, int dx, int dz)
            {
                int x = nav.NodeX[node] + dx, z = nav.NodeZ[node] + dz;
                if (Walkable(nav.NodeLayer[node], x, z) >= 0) return true;
                for (int s = nav.SeamStart[node]; s < nav.SeamStart[node + 1]; s++)
                {
                    int other = nav.Seams[s];
                    if (nav.NodeX[other] == x && nav.NodeZ[other] == z) return true;
                }
                return false;
            }
        }
    }

    // What the narrow search needs of a map: the walkable node of a layer's cell (or -1), and the walkable nodes one orthogonal
    // step from a node (its own layer's cell there, and any node a seam joins it to there). Seams are walkable graph edges; a map
    // without plates has one layer and no seams (TRV-7c).
    internal interface IEntryCells
    {
        int Width { get; }
        int Depth { get; }
        int LayerCount { get; }
        int Walkable(int layer, int x, int z);
        void Across(int node, int dx, int dz, List<int> into);
        bool AnyAcross(int node, int dx, int dz);
    }
}
