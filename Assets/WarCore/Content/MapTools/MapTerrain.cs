using System;
using System.Collections.Generic;
using WarCore.Sim;
using static WarCore.Content.MapTools.MapTerrainMath;

namespace WarCore.Content.MapTools
{
    // ED-3a terrain composer: one generation spec becomes one stamp plan for the "terrain" instance that writes every height
    // point (j then i) and every surface cell (z then x) and no entries. It reads only the description's size, floor, ceiling,
    // pack and whether it has heights and surfaces, never its current heights or surfaces: hand-owned points and cells are
    // left to MapEdit.Stamp, which skips them. So running spec A then spec B gives the same bytes as B alone.
    //
    // Heights, in this order: (1) base everywhere; (2) hill, hills, ridge, mesa, river in list order; (3) clamp to the
    // range; (4) slope fill (max envelope with step L, so every axis step is <= L and both triangles of every cell are within
    // 2/1); (5) roads in list order, each one flat across and no steeper than the walkable slope along, its band points
    // becoming fixed; (6) the others lowered to the fixed points' upper envelope, then one more fill; (7) checks.
    // Surfaces: ground, then patch, river, road in list order, a later one over an earlier one. Every failure is
    // ArgumentException and nothing is planned.
    public static class MapTerrain
    {
        public const long MaxStep = L;
        private const long S = SimFix.Scale;

        public static MapStampPlan Compose(MapDesc desc, MapGenSpec spec, TerrainRules rules)
        {
            if (desc == null) throw new ArgumentNullException(nameof(desc));
            if (spec == null) throw new ArgumentNullException(nameof(spec));
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            if (spec.Width != desc.Size.X) throw new ArgumentException("gen width " + spec.Width + " does not match the map width " + desc.Size.X);
            if (spec.Depth != desc.Size.Z) throw new ArgumentException("gen depth " + spec.Depth + " does not match the map depth " + desc.Size.Z);
            if (!string.Equals(spec.Pack, desc.Pack, StringComparison.Ordinal))
                throw new ArgumentException("gen pack " + spec.Pack + " does not match the map pack " + desc.Pack);
            bool hasHeights = desc.Heights != null, hasSurfaces = desc.Surfaces != null;
            if (!hasHeights && spec.BaseMm.HasValue) throw new ArgumentException("gen baseMm is given but the map has no heights");
            if (hasHeights && !spec.BaseMm.HasValue) throw new ArgumentException("the map has heights, so gen baseMm is required");
            if (!hasSurfaces && spec.Ground != null) throw new ArgumentException("gen ground is given but the map has no surfaces");
            if (hasSurfaces && spec.Ground == null) throw new ArgumentException("the map has surfaces, so gen ground is required");
            for (int k = 0; k < spec.Stamps.Count; k++)
            {
                MapGenStamp s = spec.Stamps[k];
                if (!hasHeights && s.ChangesHeights) throw new ArgumentException("stamp " + k + " (" + s.Kind + ") changes heights but the map has no heights");
                if (!hasSurfaces && s.Kind == "patch") throw new ArgumentException("stamp " + k + " (patch) needs surfaces but the map has none");
            }
            if (spec.Ground != null) Known(rules, spec.Ground, desc.Pack);
            foreach (MapGenStamp s in spec.Stamps)
                if (s.Surface != null) Known(rules, s.Surface, desc.Pack);

            MapHeightWrite[] heights = new MapHeightWrite[0];
            if (hasHeights)
            {
                long[] t = Heights(desc, spec, rules);
                int cols = spec.Width + 1;
                heights = new MapHeightWrite[t.Length];
                for (int p = 0; p < t.Length; p++) heights[p] = new MapHeightWrite(p % cols, p / cols, (int)t[p]);
            }
            MapSurfaceWrite[] surfaces = new MapSurfaceWrite[0];
            if (hasSurfaces)
            {
                string[] cells = Surfaces(spec);
                surfaces = new MapSurfaceWrite[cells.Length];
                for (int c = 0; c < cells.Length; c++) surfaces[c] = new MapSurfaceWrite(c % spec.Width, c / spec.Width, cells[c]);
            }
            return new MapStampPlan(MapNew.TerrainInstance, heights, surfaces, new MapStampEntry<MapWall>[0],
                new MapStampEntry<MapPoint>[0], new MapStampEntry<MapBase>[0], new MapStampEntry<MapUnit>[0]);
        }

        private static void Known(TerrainRules rules, string name, string layer)
        {
            if (!rules.TryFind(name, out _)) throw new ArgumentException("surface " + name + " is not in layer " + layer);
        }

        // ---- heights ----

        private static long[] Heights(MapDesc desc, MapGenSpec spec, TerrainRules rules)
        {
            int width = spec.Width, depth = spec.Depth, cols = width + 1, rows = depth + 1;
            long lo = Math.Max(desc.FloorRaw, -MapDescReader.HeightLimit), hi = Math.Min(desc.CeilingRaw, MapDescReader.HeightLimit);
            if (lo > hi) throw new ArgumentException("the map's height range is empty");
            var t = new long[cols * rows];
            long baseRaw = MapGenSpec.MmToRaw(spec.BaseMm.Value);
            for (int p = 0; p < t.Length; p++) t[p] = baseRaw;
            for (int k = 0; k < spec.Stamps.Count; k++)
            {
                MapGenStamp s = spec.Stamps[k];
                switch (s.Kind)
                {
                    case "hill": Hill(t, width, depth, s.X, s.Z, s.Radius, MapGenSpec.MmToRaw(s.HeightMm)); break;
                    case "hills":
                        for (int n = 0; n < s.Count; n++)
                        {
                            long x = MapNoise.Pick(spec.Seed, k, n, 0, s.X0, s.X1), z = MapNoise.Pick(spec.Seed, k, n, 1, s.Z0, s.Z1);
                            long r = MapNoise.Pick(spec.Seed, k, n, 2, s.RMin, s.RMax);
                            int mm = (int)MapNoise.Pick(spec.Seed, k, n, 3, s.HMinMm, s.HMaxMm);
                            Hill(t, width, depth, x, z, r, MapGenSpec.MmToRaw(mm));
                        }
                        break;
                    case "ridge": Band(t, width, depth, s.Points, s.HalfWidth, MapGenSpec.MmToRaw(s.HeightMm)); break;
                    case "mesa": Mesa(t, width, depth, s.X, s.Z, s.Radius, MapGenSpec.MmToRaw(s.HeightMm)); break;
                    case "river": Band(t, width, depth, s.Points, s.HalfWidth, -(long)MapGenSpec.MmToRaw(s.DepthMm)); break;
                }
            }
            for (int p = 0; p < t.Length; p++) t[p] = Math.Max(lo, Math.Min(hi, t[p]));
            Fill(t, cols, rows);
            var fixedBy = new int[t.Length];
            for (int p = 0; p < fixedBy.Length; p++) fixedBy[p] = -1;
            bool anyRoad = false;
            for (int k = 0; k < spec.Stamps.Count; k++)
                if (spec.Stamps[k].Kind == "road") { Road(t, fixedBy, width, depth, k, spec.Stamps[k], rules); anyRoad = true; }
            if (anyRoad) Settle(t, fixedBy, cols, rows);
            Check(t, fixedBy, width, depth, lo, hi, rules);
            return t;
        }

        // t += h W(r, d^2) / 1024 (toward zero) over the on-map points of the disc.
        private static void Hill(long[] t, int width, int depth, long cx, long cz, long r, long h)
        {
            int cols = width + 1;
            for (int j = Clamp(cz - r, 0, depth), j1 = Clamp(cz + r, 0, depth); j <= j1; j++)
                for (int i = Clamp(cx - r, 0, width), i1 = Clamp(cx + r, 0, width); i <= i1; i++)
                {
                    long di = i - cx, dj = j - cz;
                    t[j * cols + i] += h * Weight(r, di * di + dj * dj) / Full;
                }
        }

        // Flat top (or flat floor): every point with d^2 <= r^2 becomes c + h, c the height at the centre (the nearest on-map
        // point when the centre is off the map) before this stamp.
        private static void Mesa(long[] t, int width, int depth, long cx, long cz, long r, long h)
        {
            int cols = width + 1;
            long c = t[Clamp(cz, 0, depth) * cols + Clamp(cx, 0, width)];
            for (int j = Clamp(cz - r, 0, depth), j1 = Clamp(cz + r, 0, depth); j <= j1; j++)
                for (int i = Clamp(cx - r, 0, width), i1 = Clamp(cx + r, 0, width); i <= i1; i++)
                {
                    long di = i - cx, dj = j - cz;
                    if (di * di + dj * dj <= r * r) t[j * cols + i] = c + h;
                }
        }

        // t += h Wl(w) / 1024 (toward zero) around the polyline: ridge (h > 0), gully (h < 0), river bed (h = -depth).
        private static void Band(long[] t, int width, int depth, IReadOnlyList<MapXZ> points, int w, long h)
        {
            int cols = width + 1;
            Box(points, w, width, depth, out int i0, out int j0, out int i1, out int j1);
            for (int j = j0; j <= j1; j++)
                for (int i = i0; i <= i1; i++)
                {
                    long wl = LineWeight(Near(points, 1, i, j), w);
                    if (wl > 0) t[j * cols + i] += h * wl / Full;
                }
        }

        // On-map height points within w of the polyline's bounding box.
        private static void Box(IReadOnlyList<MapXZ> points, int w, int maxI, int maxJ, out int i0, out int j0, out int i1, out int j1)
        {
            long minX = long.MaxValue, minZ = long.MaxValue, maxX = long.MinValue, maxZ = long.MinValue;
            foreach (MapXZ p in points)
            {
                minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X);
                minZ = Math.Min(minZ, p.Z); maxZ = Math.Max(maxZ, p.Z);
            }
            i0 = Clamp(minX - w, 0, maxI); i1 = Clamp(maxX + w, 0, maxI);
            j0 = Clamp(minZ - w, 0, maxJ); j1 = Clamp(maxZ + w, 0, maxJ);
            if (maxX + w < 0 || minX - w > maxI) { i0 = 1; i1 = 0; }
            if (maxZ + w < 0 || minZ - w > maxJ) { j0 = 1; j1 = 0; }
        }

        // A road: vertex heights read from the current heights (nearest on-map point), limited to g per metre of segment
        // length (forward, then backward); every point within w takes the height of its nearest segment at its t, so points
        // with the same t are level, and becomes a fixed point of stamp k (a later road may take it over).
        private static void Road(long[] t, int[] fixedBy, int width, int depth, int k, MapGenStamp s, TerrainRules rules)
        {
            int cols = width + 1, m = s.Points.Count;
            var v = new long[m];
            var len = new long[m];
            for (int n = 0; n < m; n++)
            {
                v[n] = t[Clamp(s.Points[n].Z, 0, depth) * cols + Clamp(s.Points[n].X, 0, width)];
                if (n == 0) continue;
                long dx = (long)s.Points[n].X - s.Points[n - 1].X, dz = (long)s.Points[n].Z - s.Points[n - 1].Z;
                len[n] = ISqrt(dx * dx + dz * dz);
            }
            long g = Math.Max(0, (long)rules.MaxWalkableNum * S / rules.MaxWalkableDen - 4);
            for (int n = 1; n < m; n++) v[n] = Math.Max(v[n - 1] - g * len[n], Math.Min(v[n - 1] + g * len[n], v[n]));
            for (int n = m - 2; n >= 0; n--) v[n] = Math.Max(v[n + 1] - g * len[n + 1], Math.Min(v[n + 1] + g * len[n + 1], v[n]));
            Box(s.Points, s.HalfWidth, width, depth, out int i0, out int j0, out int i1, out int j1);
            for (int j = j0; j <= j1; j++)
                for (int i = i0; i <= i1; i++)
                {
                    MapLineNear near = Near(s.Points, 1, i, j);
                    if (!InBand(near, s.HalfWidth)) continue;
                    long va = v[near.Seg], vb = v[near.Seg + 1];
                    long h = near.L2 == 0 ? va : va + (vb - va) * Math.Max(0, Math.Min(near.L2, near.T)) / near.L2;
                    int p = j * cols + i;
                    t[p] = h;
                    fixedBy[p] = k;
                }
        }

        // Up = min envelope of the fixed points. Up(f) < F(f) at a fixed point f means another fixed point g has F(f) - F(g) >
        // L |f - g|_1: two roads (or two legs of one road) that cannot both be met, refused with both stamp numbers. Otherwise
        // every other point is lowered to Up and the whole field filled again. The fill leaves the fixed points as they are
        // (every value is <= F(f) + L d there), lifts every point to at least Lo = max over f of (F(f) - L d), and makes every
        // axis step <= L; so this equals the dispatch's "clamp into [Lo, Up], then fill the other points" (ED-3a C-b step 6).
        internal static void Settle(long[] t, int[] fixedBy, int cols, int rows)
        {
            int n = t.Length;
            var up = new long[n];
            var src = (int[])fixedBy.Clone();
            for (int p = 0; p < n; p++) up[p] = fixedBy[p] >= 0 ? t[p] : Inf;
            Upper(up, src, cols, rows);
            for (int p = 0; p < n; p++)
                if (fixedBy[p] >= 0 && up[p] < t[p]) throw Conflict(fixedBy[p], src[p], p % cols, p / cols);
            for (int p = 0; p < n; p++)
                if (fixedBy[p] < 0) t[p] = Math.Min(t[p], up[p]);
            Fill(t, cols, rows);
        }

        private static ArgumentException Conflict(int a, int b, int i, int j)
        {
            string at = " near point " + i + ", " + j;
            if (a == b) return new ArgumentException("road stamp " + a + " does not fit with itself" + at);
            return new ArgumentException("road stamps " + Math.Min(a, b) + " and " + Math.Max(a, b) + " do not fit together" + at);
        }

        // Every axis step <= L (both triangles within 2/1), every cell with all four corners fixed by the same road no steeper
        // than the walkable slope, every point in [lo, hi]. Steps (4) and (6) already ensure the first and the last (see the
        // dispatch's appendix 2); they are checked again so a broken plan is never returned.
        private static void Check(long[] t, int[] fixedBy, int width, int depth, long lo, long hi, TerrainRules rules)
        {
            int cols = width + 1;
            for (int j = 0; j <= depth; j++)
                for (int i = 0; i <= width; i++)
                {
                    int p = j * cols + i;
                    if (t[p] < lo || t[p] > hi) throw new ArgumentException("point " + i + ", " + j + " is outside the height range");
                    if (i < width && Math.Abs(t[p + 1] - t[p]) > L) throw Steep(fixedBy, p, p + 1, i, j);
                    if (j < depth && Math.Abs(t[p + cols] - t[p]) > L) throw Steep(fixedBy, p, p + cols, i, j);
                }
            for (int cz = 0; cz < depth; cz++)
                for (int cx = 0; cx < width; cx++)
                {
                    int p = cz * cols + cx, k = fixedBy[p];
                    if (k < 0 || fixedBy[p + 1] != k || fixedBy[p + cols] != k || fixedBy[p + cols + 1] != k) continue;
                    if (!MapBrush.CellWithin(t[p], t[p + 1], t[p + cols], t[p + cols + 1], rules.MaxWalkableNum, rules.MaxWalkableDen))
                        throw new ArgumentException("road stamp " + k + " is steeper than the walkable slope at cell " + cx + ", " + cz);
                }
        }

        private static ArgumentException Steep(int[] fixedBy, int a, int b, int i, int j)
        {
            if (fixedBy[a] >= 0 && fixedBy[b] >= 0) return Conflict(fixedBy[a], fixedBy[b], i, j);
            return new ArgumentException("the terrain is steeper than 2/1 at point " + i + ", " + j);
        }

        // ---- surfaces ----

        private static string[] Surfaces(MapGenSpec spec)
        {
            int width = spec.Width, depth = spec.Depth;
            var cells = new string[width * depth];
            for (int c = 0; c < cells.Length; c++) cells[c] = spec.Ground;
            for (int k = 0; k < spec.Stamps.Count; k++)
            {
                MapGenStamp s = spec.Stamps[k];
                if (s.Kind == "patch") Patch(cells, width, depth, spec.Seed, k, s);
                else if (s.Kind == "river" || s.Kind == "road")
                {
                    Box(s.Points, s.HalfWidth + 1, width - 1, depth - 1, out int x0, out int z0, out int x1, out int z1);
                    for (int z = z0; z <= z1; z++)
                        for (int x = x0; x <= x1; x++)
                            if (InBand(Near(s.Points, 2, 2L * x + 1, 2L * z + 1), 2L * s.HalfWidth)) cells[z * width + x] = s.Surface;
                }
            }
            return cells;
        }

        // Cell (x, z) is in when its centre's squared distance times 1024 <= r^2 (1024 + j), with j = 0 for rough 0 and
        // Pick(seed, k, x, z, -rough, rough) otherwise; in doubled coordinates: ((2x + 1 - 2cx)^2 + (2z + 1 - 2cz)^2) 1024
        // <= 4 r^2 (1024 + j). j >= -512, so the edge never reaches past 2r in doubled units.
        private static void Patch(string[] cells, int width, int depth, ulong seed, int k, MapGenStamp s)
        {
            long cx = s.X, cz = s.Z, r = s.Radius;
            for (int z = Clamp(cz - 2 * r, 0, depth - 1), z1 = Clamp(cz + 2 * r, 0, depth - 1); z <= z1; z++)
                for (int x = Clamp(cx - 2 * r, 0, width - 1), x1 = Clamp(cx + 2 * r, 0, width - 1); x <= x1; x++)
                {
                    long ax = 2L * x + 1 - 2 * cx, az = 2L * z + 1 - 2 * cz;
                    long j = s.Rough == 0 ? 0 : MapNoise.Pick(seed, k, x, z, -s.Rough, s.Rough);
                    if ((ax * ax + az * az) * Full <= 4 * r * r * (Full + j)) cells[z * width + x] = s.Surface;
                }
        }
    }
}
