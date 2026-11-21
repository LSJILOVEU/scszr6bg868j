using System;
using System.Collections.Generic;
using WarCore.Sim;

namespace WarCore.Content
{
    // TRV-2: a rectangle of whole cells [X, X + Width) x [Z, Z + Depth) where no traversal link may start, pass or end.
    public readonly struct NoTraverseZone
    {
        public readonly int X, Z, Width, Depth;

        public NoTraverseZone(int x, int z, int width, int depth)
        {
            if (width < 1 || depth < 1) throw new ArgumentException("A no-traverse zone covers at least one cell.", nameof(width));
            X = x; Z = z; Width = width; Depth = depth;
        }

        public bool Contains(int x, int z) => x >= X && (long)x < (long)X + Width && z >= Z && (long)z < (long)Z + Depth;
    }

    // One measured link: from node to node of the LayeredNav, the kind (index into TraversalRules), its shape, the measures
    // (raw) and the time it takes. Cross is the height the body passes over: the fixed point c for Over, and
    // max(hi(from), hi(to)) for Edge. Gap is LinkDerive.NoGap when nothing solid is above.
    public readonly struct TraversalLink : IEquatable<TraversalLink>
    {
        public readonly int From, To, Kind;
        public readonly TraversalShape Shape;
        public readonly long Rise, Fall, Gap, Cross;
        public readonly int Ticks;

        public TraversalLink(int from, int to, int kind, TraversalShape shape, long rise, long fall, long gap, long cross, int ticks)
        {
            From = from; To = to; Kind = kind; Shape = shape; Rise = rise; Fall = fall; Gap = gap; Cross = cross; Ticks = ticks;
        }

        public bool Equals(TraversalLink o) => From == o.From && To == o.To && Kind == o.Kind && Shape == o.Shape
            && Rise == o.Rise && Fall == o.Fall && Gap == o.Gap && Cross == o.Cross && Ticks == o.Ticks;
        public override bool Equals(object obj) => obj is TraversalLink o && Equals(o);
        public override int GetHashCode() => From * 397 ^ To;
        public override string ToString() => From + "->" + To + " kind " + Kind + " " + Shape + " rise " + Rise + " fall " + Fall
            + " gap " + (Gap == LinkDerive.NoGap ? "none" : Gap.ToString()) + " cross " + Cross + " ticks " + Ticks;
    }

    // TRV-2: measures the traversal links of a map with floor plates (rule K15). Pure: reads the map, its graph, the rules
    // and the zones and changes none of them. Not wired into anything yet (TRV-3 adds the links to the graph).
    //
    // Notation: for node n, lo(n), hi(n) are the lowest and highest corner of its cell on its own layer. A solid span of a
    // cell is a wall over it ([Bottom, Top]) or a plate solid there ([lowest top corner - thickness, highest top corner]).
    // The ground is not a span; it only enters through the ground layer's heights. All heights absolute raw, all long.
    //   Edge: a, b on different layers, cells sharing an edge, both walkable, no seam between them. rise, fall = the larger
    //     difference over the edge's two end points (each at least 0). top = max(hi(a), hi(b)); gap = the lowest bottom >= top
    //     of the spans of cells a and b, a's own plate in a and b's own plate in b left out, minus top.
    //   Over: a, m, b orthogonally adjacent in a line; a, b walkable; m walkable neither on a's nor on b's layer; a span of m
    //     with bottom <= c0 < top, c0 = max(hi(a), hi(b), highest ground corner of m). c = fixed point of "a span of m has
    //     bottom <= c < top: c = that top" from c0; rise = c - lo(a); fall = c - lo(b); gap = the lowest bottom > c over the
    //     spans of m, a and b, minus c.
    //   Kind: the first of the table whose shape matches and rise, fall, gap fall into its intervals; none, no link.
    //   No link when a, m or b lies in a zone. Links are sorted by (from, to); at most MaxLinks.
    public static class LinkDerive
    {
        public const int MaxLinks = 65535;
        public const long NoGap = long.MaxValue;

        // Empty when nav is null (a map without plates).
        public static TraversalLink[] Derive(WcMap map, LayeredNav nav, TraversalRules rules, IReadOnlyList<NoTraverseZone> zones) =>
            Derive(map, nav, rules, zones, MaxLinks, out _);

        // spanReads: how many spans were looked at (the step count the size test bounds).
        internal static TraversalLink[] Derive(WcMap map, LayeredNav nav, TraversalRules rules, IReadOnlyList<NoTraverseZone> zones,
            int maxLinks, out long spanReads)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            spanReads = 0;
            if (nav == null) return Array.Empty<TraversalLink>();
            if (nav.Width != map.Width || nav.Depth != map.Depth || nav.LayerCount != map.Plates.Count + 1)
                throw new ArgumentException("The graph is not the map's.", nameof(nav));
            var run = new Run(map, nav, rules, zones);
            List<TraversalLink> links = run.All();
            spanReads = run.Reads;
            links.Sort((p, q) => p.From != q.From ? p.From.CompareTo(q.From) : p.To.CompareTo(q.To));
            if (links.Count > maxLinks) throw new ArgumentException("More than " + maxLinks + " traversal links.", nameof(map));
            return links.ToArray();
        }

        private sealed class Run
        {
            private readonly LayeredNav nav;
            private readonly TraversalRules rules;
            private readonly int width, depth;
            private readonly bool[] zoned;
            // Per cell: plate layers solid there (ascending), and spans sorted by (bottom, top, layer); layer -1 for a wall.
            private readonly int[] layerStart, layers, spanStart, spanLayer;
            private readonly long[] spanBottom, spanTop;
            internal long Reads;

            internal Run(WcMap map, LayeredNav nav, TraversalRules rules, IReadOnlyList<NoTraverseZone> zones)
            {
                this.nav = nav; this.rules = rules;
                width = nav.Width; depth = nav.Depth;
                int cells = width * depth;
                zoned = new bool[cells];
                if (zones != null)
                    for (int z = 0; z < depth; z++)
                        for (int x = 0; x < width; x++)
                            foreach (NoTraverseZone zone in zones)
                                if (zone.Contains(x, z)) zoned[z * width + x] = true;

                // Plate layers per cell, ascending (nodes are numbered layer by layer).
                layerStart = new int[cells + 1];
                for (int n = nav.GroundCount; n < nav.Count; n++) layerStart[nav.ZOf(n) * width + nav.XOf(n) + 1]++;
                for (int c = 0; c < cells; c++) layerStart[c + 1] += layerStart[c];
                layers = new int[layerStart[cells]];
                var fill = (int[])layerStart.Clone();
                for (int n = nav.GroundCount; n < nav.Count; n++) layers[fill[nav.ZOf(n) * width + nav.XOf(n)]++] = nav.LayerOf(n);

                // Spans: every wall over its footprint, every plate over its solid cells.
                var spans = new List<(int Cell, long Bottom, long Top, int Layer)>();
                long floor = map.Min.Y.Raw;
                foreach (WallGeom w in WcMapValidation.Walls(map))
                    for (int z = w.Z0; z < w.Z1; z++)
                        for (int x = w.X0; x < w.X1; x++) spans.Add((z * width + x, floor + w.Bottom, floor + w.Top, -1));
                for (int n = nav.GroundCount; n < nav.Count; n++)
                {
                    int layer = nav.LayerOf(n), x = nav.XOf(n), z = nav.ZOf(n);
                    Corners(layer, x, z, out long low, out long high);
                    spans.Add((z * width + x, low - map.Plates[layer - 1].ThickRaw, high, layer));
                }
                spans.Sort((p, q) => p.Cell != q.Cell ? p.Cell.CompareTo(q.Cell) : p.Bottom != q.Bottom ? p.Bottom.CompareTo(q.Bottom)
                    : p.Top != q.Top ? p.Top.CompareTo(q.Top) : p.Layer.CompareTo(q.Layer));
                spanStart = new int[cells + 1];
                spanBottom = new long[spans.Count]; spanTop = new long[spans.Count]; spanLayer = new int[spans.Count];
                for (int s = 0; s < spans.Count; s++)
                {
                    spanStart[spans[s].Cell + 1]++;
                    spanBottom[s] = spans[s].Bottom; spanTop[s] = spans[s].Top; spanLayer[s] = spans[s].Layer;
                }
                for (int c = 0; c < cells; c++) spanStart[c + 1] += spanStart[c];
            }

            internal List<TraversalLink> All()
            {
                var links = new List<TraversalLink>();
                for (int a = 0; a < nav.Count; a++)
                {
                    if (!nav.IsPassable(a)) continue;
                    int x = nav.XOf(a), z = nav.ZOf(a), la = nav.LayerOf(a);
                    if (zoned[z * width + x]) continue;
                    for (int direction = 0; direction < 4; direction++)
                    {
                        // N, E, S, W.
                        int dx = direction == 1 ? 1 : direction == 3 ? -1 : 0;
                        int dz = direction == 0 ? 1 : direction == 2 ? -1 : 0;
                        int nx = x + dx, nz = z + dz;
                        if (!OnMap(nx, nz) || zoned[nz * width + nx]) continue;
                        // Edge: every other layer of the neighbouring cell.
                        for (int k = -1; k < LayersAt(nx, nz); k++)
                        {
                            int lb = LayerAt(nx, nz, k);
                            if (lb == la || !nav.TryNode(lb, nx, nz, out int b) || !nav.IsPassable(b) || HasSeam(a, b)) continue;
                            Edge(links, a, b, x, z, dx, dz, la, lb, nx, nz);
                        }
                        // Over: m is the neighbour, b the cell after it.
                        int bx = nx + dx, bz = nz + dz;
                        if (!OnMap(bx, bz) || zoned[bz * width + bx]) continue;
                        for (int k = -1; k < LayersAt(bx, bz); k++)
                        {
                            int lb = LayerAt(bx, bz, k);
                            if (!nav.TryNode(lb, bx, bz, out int b) || !nav.IsPassable(b)) continue;
                            if (Walkable(la, nx, nz) || Walkable(lb, nx, nz)) continue;
                            Over(links, a, b, x, z, nx, nz, bx, bz, la, lb);
                        }
                    }
                }
                return links;
            }

            private void Edge(List<TraversalLink> links, int a, int b, int x, int z, int dx, int dz, int la, int lb, int nx, int nz)
            {
                // The shared edge's two end points (height point coordinates), as LayeredNav's seams.
                int i0 = dx == 1 ? x + 1 : x, j0 = dz == 1 ? z + 1 : z;
                int i1 = dx == 0 ? x + 1 : i0, j1 = dz == 0 ? z + 1 : j0;
                long d0 = Height(lb, i0, j0) - Height(la, i0, j0), d1 = Height(lb, i1, j1) - Height(la, i1, j1);
                long rise = Math.Max(0, Math.Max(d0, d1)), fall = Math.Max(0, Math.Max(-d0, -d1));
                Corners(la, x, z, out _, out long hiA);
                Corners(lb, nx, nz, out _, out long hiB);
                long top = Math.Max(hiA, hiB);
                long lowest = Math.Min(LowestFrom(x, z, top, la), LowestFrom(nx, nz, top, lb));
                long gap = lowest == NoGap ? NoGap : lowest - top;
                Add(links, a, b, TraversalShape.Edge, rise, fall, gap, top);
            }

            private void Over(List<TraversalLink> links, int a, int b, int x, int z, int mx, int mz, int bx, int bz, int la, int lb)
            {
                Corners(la, x, z, out long loA, out long hiA);
                Corners(lb, bx, bz, out long loB, out long hiB);
                Corners(0, mx, mz, out _, out long groundM);
                long c0 = Math.Max(Math.Max(hiA, hiB), groundM);
                int m = mz * width + mx;
                bool blocked = false;
                for (int s = spanStart[m]; s < spanStart[m + 1]; s++)
                {
                    Reads++;
                    if (spanBottom[s] <= c0 && c0 < spanTop[s]) blocked = true;
                }
                if (!blocked) return;
                // The spans are sorted by bottom and c only rises, so one pass reaches the fixed point: a span skipped because
                // its bottom is above c leaves every later span above c too.
                long c = c0;
                for (int s = spanStart[m]; s < spanStart[m + 1]; s++)
                {
                    Reads++;
                    if (spanBottom[s] <= c && spanTop[s] > c) c = spanTop[s];
                }
                long lowest = Math.Min(LowestAbove(mx, mz, c), Math.Min(LowestAbove(x, z, c), LowestAbove(bx, bz, c)));
                long gap = lowest == NoGap ? NoGap : lowest - c;
                Add(links, a, b, TraversalShape.Over, c - loA, c - loB, gap, c);
            }

            private void Add(List<TraversalLink> links, int a, int b, TraversalShape shape, long rise, long fall, long gap, long cross)
            {
                int kind = rules.FirstMatch(shape, rise, fall, gap);
                if (kind < 0) return;
                links.Add(new TraversalLink(a, b, kind, shape, rise, fall, gap, cross, rules[kind].Ticks(rise, fall)));
            }

            // The lowest bottom >= top of the cell's spans, the given plate layer left out; NoGap when there is none.
            private long LowestFrom(int x, int z, long top, int ownLayer)
            {
                int cell = z * width + x;
                long best = NoGap;
                for (int s = spanStart[cell]; s < spanStart[cell + 1]; s++)
                {
                    Reads++;
                    if (spanLayer[s] != ownLayer && spanBottom[s] >= top && spanBottom[s] < best) best = spanBottom[s];
                }
                return best;
            }

            // The lowest bottom > c of the cell's spans; NoGap when there is none.
            private long LowestAbove(int x, int z, long c)
            {
                int cell = z * width + x;
                long best = NoGap;
                for (int s = spanStart[cell]; s < spanStart[cell + 1]; s++)
                {
                    Reads++;
                    if (spanBottom[s] > c && spanBottom[s] < best) best = spanBottom[s];
                }
                return best;
            }

            private bool OnMap(int x, int z) => x >= 0 && z >= 0 && x < width && z < depth;

            // k = -1 is the ground; 0.. the plates solid in the cell, ascending.
            private int LayersAt(int x, int z) { int c = z * width + x; return layerStart[c + 1] - layerStart[c]; }
            private int LayerAt(int x, int z, int k) => k < 0 ? 0 : layers[layerStart[z * width + x] + k];

            private bool Walkable(int layer, int x, int z) => nav.TryNode(layer, x, z, out int node) && nav.IsPassable(node);

            private bool HasSeam(int a, int b)
            {
                for (int k = 0; k < nav.SeamCount(a); k++)
                    if (nav.Seam(a, k) == b) return true;
                return false;
            }

            private long Height(int layer, int i, int j)
            {
                if (!nav.TryLayerHeight(layer, i, j, out long h)) throw new InvalidOperationException("Height point off its layer.");
                return h;
            }

            private void Corners(int layer, int x, int z, out long low, out long high)
            {
                long p = Height(layer, x, z), q = Height(layer, x + 1, z), r = Height(layer, x, z + 1), s = Height(layer, x + 1, z + 1);
                low = Math.Min(Math.Min(p, q), Math.Min(r, s));
                high = Math.Max(Math.Max(p, q), Math.Max(r, s));
            }
        }
    }
}
