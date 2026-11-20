using System;
using System.Collections.Generic;
using System.IO;
using WarCore.Sim;

namespace WarCore.Content.MapTools
{
    // ED-2 terrain brushes. Each brush reads one description and returns the writes it would make, without changing anything:
    // height points ordered by j then i, surface cells by z then x, and only those whose final value differs from the current
    // one (an unchanged point is not written, so it keeps its owner). The caller applies them in one MapEdit.Batch, so they
    // are hand edits. All arithmetic is integer.
    //
    // Height brushes then go through Legalize: only the brushed points move, each towards its wanted value as far as the
    // format's 2/1 slope rule (HeightField.CellIsLegal) and the height range ([floorRaw, ceilingRaw], |h| < 2^29) allow, so a
    // legal map stays legal and bakes without HEIGHT_SLOPE or HEIGHT_RANGE.
    public static class MapBrush
    {
        public const int MaxRadius = 64;
        // Weight of the brush centre; a weight w moves a point by w / Full of the brush's change.
        public const int Full = 1024;
        public const int MaxPasses = 256;
        // Line brushes take end points in [-MaxOutside, Size + MaxOutside]; with Size <= 512 every |dx|, |dz| <= 2560, so
        // L^2 < 2^24, (h1 - h0) t < 2^54 and 1024 (hw + skirt)^2 L^2 < 2^48.
        public const int MaxOutside = 1024;
        private const long S = SimFix.Scale;
        // Passes the last Legalize on this thread ran (for the performance record; MaxPasses + 1 when it hit the cap).
        [ThreadStatic] internal static int LastPasses;
        // a^2 + b^2 bound of a triangle no steeper than 2/1 (HeightField.MaxSlopeNumerator / MaxSlopeDenominator).
        private const long SlopeLimit = (long)HeightField.MaxSlopeNumerator * HeightField.MaxSlopeNumerator * S * S;

        // Falloff: 1024 (r^2 - d^2)^2 / r^4 for d^2 = di^2 + dj^2 <= r^2 (integer division), else 0.
        public static int Weight(int radius, int di, int dj)
        {
            CheckRadius(radius, 1);
            long r2 = (long)radius * radius, d2 = (long)di * di + (long)dj * dj;
            if (d2 > r2) return 0;
            long k = r2 - d2;
            return (int)(Full * k * k / (r2 * r2));
        }

        // Raise (amount > 0) or lower (amount < 0): wanted = h + amount * w / 1024, rounded toward zero.
        public static MapHeightWrite[] Raise(MapDesc d, int i, int j, int radius, int amount)
        {
            var work = new HeightWork(d);
            CheckRadius(radius, 1);
            if (amount <= -MapDescReader.HeightLimit - 1 || amount >= MapDescReader.HeightLimit + 1) throw new ArgumentOutOfRangeException(nameof(amount));
            work.Disc(i, j, radius, (h, w) => h + (long)amount * w / Full);
            return work.Legalize();
        }

        // Flatten towards target: wanted = h + (target - h) * w / 1024, rounded toward zero.
        public static MapHeightWrite[] Flatten(MapDesc d, int i, int j, int radius, int target)
        {
            var work = new HeightWork(d);
            CheckRadius(radius, 1);
            CheckHeight(target, nameof(target));
            work.Disc(i, j, radius, (h, w) => h + (target - h) * w / Full);
            return work.Legalize();
        }

        // Smooth: m = floor(mean of the point and its on-map 4-neighbours), read from the heights before the brush;
        // wanted = h + (m - h) * w / 1024, rounded toward zero.
        public static MapHeightWrite[] Smooth(MapDesc d, int i, int j, int radius)
        {
            var work = new HeightWork(d);
            CheckRadius(radius, 1);
            work.DiscAt(i, j, radius, (pi, pj, h, w) => h + (work.Mean(pi, pj) - h) * w / Full);
            return work.Legalize();
        }

        // Ramp from point (i0, j0) at h0 to (i1, j1) at h1. A point is on it when 0 <= t <= L^2 and c^2 <= halfWidth^2 L^2
        // (t = (i - i0) dx + (j - j0) dz, c = (i - i0) dz - (j - j0) dx, L^2 = dx^2 + dz^2); wanted = ramp(t) = h0 +
        // floor((h1 - h0) t / L^2). Every cell with all four corners on the ramp must be no steeper than walkNum / walkDen at
        // the wanted heights, else ArgumentException. Around it a skirt k = min(64, ceil(max |wanted - h| / 1 m)) points wide
        // (squared distance to the segment <= (halfWidth + k)^2, off the ramp) wants ramp(t clamped to [0, L^2]), so
        // Legalize has room to slope down to the ground. Every ramp point must end at its wanted height, else
        // ArgumentException ("does not fit"): a ramp is written exactly or not at all.
        public static MapHeightWrite[] Ramp(MapDesc d, int i0, int j0, int h0, int i1, int j1, int h1, int halfWidth, int walkNum, int walkDen)
        {
            var work = new HeightWork(d);
            CheckLine(d, i0, j0, i1, j1);
            CheckRadius(halfWidth, 0);
            CheckHeight(h0, nameof(h0));
            CheckHeight(h1, nameof(h1));
            if (h0 < d.FloorRaw || h0 > d.CeilingRaw) throw new ArgumentOutOfRangeException(nameof(h0));
            if (h1 < d.FloorRaw || h1 > d.CeilingRaw) throw new ArgumentOutOfRangeException(nameof(h1));
            if (walkDen < 1 || walkDen > HeightField.MaxWalkableDenominator || walkNum < 0 || walkNum > HeightField.MaxSlopeNumerator * walkDen)
                throw new ArgumentOutOfRangeException(nameof(walkNum));
            long dx = (long)i1 - i0, dz = (long)j1 - j0, l2 = dx * dx + dz * dz;
            long band = (long)halfWidth * halfWidth * l2;
            Func<long, long> ramp = t => h0 + FloorDiv(((long)h1 - h0) * Math.Max(0, Math.Min(l2, t)), l2);
            work.Line(i0, j0, i1, j1, halfWidth, (i, j, t, c) => t >= 0 && t <= l2 && c * c <= band, (t, c, h) => ramp(t));
            int core = work.Count;
            work.CheckWalkable(walkNum, walkDen);
            long skirt = Math.Min(MaxRadius, (work.MaxChange() + S - 1) / S);
            if (skirt > 0)
            {
                long r = halfWidth + skirt, r2 = r * r;
                work.Line(i0, j0, i1, j1, (int)r, (i, j, t, c) =>
                {
                    if (t >= 0 && t <= l2 && c * c <= band) return false;
                    if (t < 0) return ((long)i - i0) * (i - i0) + ((long)j - j0) * (j - j0) <= r2;
                    if (t > l2) return ((long)i - i1) * (i - i1) + ((long)j - j1) * (j - j1) <= r2;
                    return c * c <= r2 * l2;
                }, (t, c, h) => ramp(t));
            }
            MapHeightWrite[] writes = work.Legalize();
            if (!work.Reached(core))
                throw new ArgumentException(LastPasses > MaxPasses
                    ? "the ramp does not fit within " + MaxPasses + " passes: build it in lower pieces"
                    : "the ramp does not fit: the ground around it is too steep or a cell in the way is illegal");
            return writes;
        }

        // Bank: a one-sided step along the segment. Points with 0 <= t <= L^2, c > 0 and c^2 <= B (B = halfWidth^2 L^2; c > 0
        // is the right of P0 -> P1 when i points right and j forward) get weight w = 1024 (B - c^2) / B (integer division)
        // and wanted = h + amount w / 1024 (toward zero): full next to the segment, 0 at the far edge. Points with c <= 0 do
        // not move, so the step at the segment is the steep side and Legalize makes it the steepest legal slope.
        public static MapHeightWrite[] Bank(MapDesc d, int i0, int j0, int i1, int j1, int halfWidth, int amount)
        {
            var work = new HeightWork(d);
            CheckLine(d, i0, j0, i1, j1);
            CheckRadius(halfWidth, 1);
            if (amount <= -MapDescReader.HeightLimit - 1 || amount >= MapDescReader.HeightLimit + 1) throw new ArgumentOutOfRangeException(nameof(amount));
            long dx = (long)i1 - i0, dz = (long)j1 - j0, l2 = dx * dx + dz * dz;
            long band = (long)halfWidth * halfWidth * l2;
            work.Line(i0, j0, i1, j1, halfWidth, (i, j, t, c) => t >= 0 && t <= l2 && c > 0 && c * c <= band,
                (t, c, h) => h + (long)amount * (Full * (band - c * c) / band) / Full);
            return work.Legalize();
        }

        // Surface disc: cells (x', z') with (x' - x)^2 + (z' - z)^2 <= r^2.
        public static MapSurfaceWrite[] SurfaceCircle(MapDesc d, int x, int z, int radius, string name) =>
            Surface(d, x, z, radius, name, true);

        // Surface square: cells with |x' - x| <= r and |z' - z| <= r.
        public static MapSurfaceWrite[] SurfaceSquare(MapDesc d, int x, int z, int radius, string name) =>
            Surface(d, x, z, radius, name, false);

        private static MapSurfaceWrite[] Surface(MapDesc d, int x, int z, int radius, string name, bool circle)
        {
            if (d == null) throw new ArgumentNullException(nameof(d));
            if (d.Surfaces == null) throw new ArgumentException("the description has no surfaces");
            CheckRadius(radius, 0);
            try { WcMapValidation.TextBytes(name); }
            catch (InvalidDataException) { throw new ArgumentException("bad surface name", nameof(name)); }
            var writes = new List<MapSurfaceWrite>();
            long r2 = (long)radius * radius;
            int z0 = Clamp((long)z - radius, 0, d.Size.Z - 1), z1 = Clamp((long)z + radius, 0, d.Size.Z - 1);
            int x0 = Clamp((long)x - radius, 0, d.Size.X - 1), x1 = Clamp((long)x + radius, 0, d.Size.X - 1);
            for (int cz = z0; cz <= z1; cz++)
                for (int cx = x0; cx <= x1; cx++)
                {
                    long ax = (long)cx - x, az = (long)cz - z;
                    if (Math.Abs(ax) > radius || Math.Abs(az) > radius) continue;
                    if (circle && ax * ax + az * az > r2) continue;
                    string now = d.Surfaces.Names[MapEditDraft.OwnerIndex(d.Surfaces.Rows[cz][cx])];
                    if (!string.Equals(now, name, StringComparison.Ordinal)) writes.Add(new MapSurfaceWrite(cx, cz, name));
                }
            return writes.ToArray();
        }

        // ---- the 2/1 rule as intervals ----

        // The heights h of one point that keep every triangle around it no steeper than 2/1, given the other points: the
        // intersection over its triangles, as [lo, hi] (lo > hi when empty). heights is row-major with (width + 1) per row.
        internal static void Allowed(long[] heights, int width, int depth, int i, int j, ref long lo, ref long hi)
        {
            int row = width + 1;
            for (int cz = j - 1; cz <= j; cz++)
                for (int cx = i - 1; cx <= i; cx++)
                {
                    if (cx < 0 || cz < 0 || cx >= width || cz >= depth) continue;
                    long h00 = heights[cz * row + cx], h10 = heights[cz * row + cx + 1];
                    long h01 = heights[(cz + 1) * row + cx], h11 = heights[(cz + 1) * row + cx + 1];
                    int ri = i - cx, rj = j - cz;
                    // Triangle A (a = h10 - h00, b = h11 - h10) and B (a = h11 - h01, b = h01 - h00).
                    if (ri == 0 && rj == 0)
                    {
                        OneEdge(h10, h11 - h10, ref lo, ref hi);
                        OneEdge(h01, h11 - h01, ref lo, ref hi);
                    }
                    else if (ri == 1 && rj == 0) TwoEdges(h00, h11, ref lo, ref hi);
                    else if (ri == 0 && rj == 1) TwoEdges(h00, h11, ref lo, ref hi);
                    else
                    {
                        OneEdge(h10, h10 - h00, ref lo, ref hi);
                        OneEdge(h01, h01 - h00, ref lo, ref hi);
                    }
                }
        }

        // (h - c)^2 + f^2 <= SlopeLimit.
        private static void OneEdge(long c, long f, ref long lo, ref long hi)
        {
            long m = SlopeLimit - f * f;
            if (m < 0) { lo = 1; hi = 0; return; }
            long r = ISqrt(m);
            lo = Math.Max(lo, c - r);
            hi = Math.Min(hi, c + r);
        }

        // (h - p)^2 + (q - h)^2 <= SlopeLimit, that is (2h - p - q)^2 <= 2 SlopeLimit - (p - q)^2.
        private static void TwoEdges(long p, long q, ref long lo, ref long hi)
        {
            long m = 2 * SlopeLimit - (p - q) * (p - q);
            if (m < 0) { lo = 1; hi = 0; return; }
            long s = ISqrt(m);
            lo = Math.Max(lo, -((s - p - q) >> 1));
            hi = Math.Min(hi, (p + q + s) >> 1);
        }

        internal static long ISqrt(long m)
        {
            long r = (long)Math.Sqrt(m);
            while (r * r > m) r--;
            while ((r + 1) * (r + 1) <= m) r++;
            return r;
        }

        // a^2 + b^2 <= num^2 S^2 / den^2 for both triangles of the cell (HeightField.CellWithinSlope).
        internal static bool CellWithin(long h00, long h10, long h01, long h11, long num, long den)
        {
            long limit = num * num * S * S, d2 = den * den;
            return Within(h10 - h00, h11 - h10, limit, d2) && Within(h11 - h01, h01 - h00, limit, d2);
        }

        private static bool Within(long a, long b, long limit, long d2)
        {
            long m = a * a + b * b;
            return m <= long.MaxValue / d2 && m * d2 <= limit;
        }

        // ---- shared ----

        private static long FloorDiv(long a, long b)
        {
            long q = a / b;
            if (a % b != 0 && (a < 0) != (b < 0)) q--;
            return q;
        }

        private static int Clamp(long v, int lo, int hi) => (int)Math.Max(lo, Math.Min(hi, v));

        private static void CheckRadius(int radius, int min)
        {
            if (radius < min || radius > MaxRadius) throw new ArgumentOutOfRangeException(nameof(radius));
        }

        private static void CheckHeight(int raw, string name)
        {
            if (raw <= -MapDescReader.HeightLimit - 1 || raw >= MapDescReader.HeightLimit + 1) throw new ArgumentOutOfRangeException(name);
        }

        private static void CheckLine(MapDesc d, int i0, int j0, int i1, int j1)
        {
            if (i0 < -MaxOutside || i1 < -MaxOutside || i0 > d.Size.X + MaxOutside || i1 > d.Size.X + MaxOutside) throw new ArgumentOutOfRangeException(nameof(i0));
            if (j0 < -MaxOutside || j1 < -MaxOutside || j0 > d.Size.Z + MaxOutside || j1 > d.Size.Z + MaxOutside) throw new ArgumentOutOfRangeException(nameof(j0));
            if (i0 == i1 && j0 == j1) throw new ArgumentException("the segment has no length");
        }

        // The heights of one brush stroke: the current table, the brushed points and their wanted values. Internal so that
        // later stamps (ED-3) can reuse Legalize.
        internal sealed class HeightWork
        {
            private readonly MapDesc d;
            private readonly int width, depth;
            private readonly long[] start, now;
            private readonly List<int> points = new List<int>();
            private readonly List<long> wanted = new List<long>();

            internal HeightWork(MapDesc desc)
            {
                d = desc ?? throw new ArgumentNullException(nameof(desc));
                if (d.Heights == null) throw new ArgumentException("the description has no heights");
                width = d.Size.X; depth = d.Size.Z;
                start = new long[(width + 1) * (depth + 1)];
                for (int j = 0; j <= depth; j++)
                    for (int i = 0; i <= width; i++) start[j * (width + 1) + i] = d.Heights[j][i];
                now = (long[])start.Clone();
            }

            private long H(int i, int j) => start[j * (width + 1) + i];

            internal int Count => points.Count;

            // The largest |wanted - current| over the brushed points.
            internal long MaxChange()
            {
                long m = 0;
                for (int n = 0; n < points.Count; n++) m = Math.Max(m, Math.Abs(wanted[n] - start[points[n]]));
                return m;
            }

            // After Legalize: did the first count brushed points reach their wanted heights?
            internal bool Reached(int count)
            {
                for (int n = 0; n < count; n++)
                    if (now[points[n]] != wanted[n]) return false;
                return true;
            }

            internal long Mean(int i, int j)
            {
                long sum = H(i, j), count = 1;
                if (i > 0) { sum += H(i - 1, j); count++; }
                if (i < width) { sum += H(i + 1, j); count++; }
                if (j > 0) { sum += H(i, j - 1); count++; }
                if (j < depth) { sum += H(i, j + 1); count++; }
                return FloorDiv(sum, count);
            }

            internal void Disc(int i, int j, int radius, Func<long, int, long> want) =>
                DiscAt(i, j, radius, (pi, pj, h, w) => want(h, w));

            // Points with a weight above 0 around (i, j), clipped to the map.
            internal void DiscAt(int i, int j, int radius, Func<int, int, long, int, long> want)
            {
                int j0 = Clamp((long)j - radius, 0, depth), j1 = Clamp((long)j + radius, 0, depth);
                int i0 = Clamp((long)i - radius, 0, width), i1 = Clamp((long)i + radius, 0, width);
                for (int pj = j0; pj <= j1; pj++)
                    for (int pi = i0; pi <= i1; pi++)
                    {
                        long di = (long)pi - i, dj = (long)pj - j;
                        if (Math.Abs(di) > radius || Math.Abs(dj) > radius) continue;
                        int w = Weight(radius, (int)di, (int)dj);
                        if (w == 0) continue;
                        Add(pi, pj, want(pi, pj, H(pi, pj), w));
                    }
            }

            // Points near the segment (within reach of it in i and j) that inside(i, j, t, c) accepts, clipped to the map.
            internal void Line(int i0, int j0, int i1, int j1, int halfWidth, Func<int, int, long, long, bool> inside, Func<long, long, long, long> want)
            {
                long dx = (long)i1 - i0, dz = (long)j1 - j0;
                int pj0 = Clamp((long)Math.Min(j0, j1) - halfWidth, 0, depth), pj1 = Clamp((long)Math.Max(j0, j1) + halfWidth, 0, depth);
                int pi0 = Clamp((long)Math.Min(i0, i1) - halfWidth, 0, width), pi1 = Clamp((long)Math.Max(i0, i1) + halfWidth, 0, width);
                for (int pj = pj0; pj <= pj1; pj++)
                    for (int pi = pi0; pi <= pi1; pi++)
                    {
                        long t = (pi - (long)i0) * dx + (pj - (long)j0) * dz;
                        long c = (pi - (long)i0) * dz - (pj - (long)j0) * dx;
                        if (inside(pi, pj, t, c)) Add(pi, pj, want(t, c, H(pi, pj)));
                    }
            }

            internal void Add(int i, int j, long want)
            {
                points.Add(j * (width + 1) + i);
                wanted.Add(want);
            }

            // Every cell whose four corners are brushed points, at the wanted values, within walkNum / walkDen.
            internal void CheckWalkable(int walkNum, int walkDen)
            {
                var want = new Dictionary<int, long>();
                for (int n = 0; n < points.Count; n++) want[points[n]] = wanted[n];
                int row = width + 1;
                foreach (int k in points)
                {
                    int i = k % row, j = k / row;
                    if (i >= width || j >= depth) continue;
                    if (!want.TryGetValue(k + 1, out long h10) || !want.TryGetValue(k + row, out long h01)
                        || !want.TryGetValue(k + row + 1, out long h11)) continue;
                    if (!CellWithin(want[k], h10, h01, h11, walkNum, walkDen))
                        throw new ArgumentException("the ramp is steeper than the walkable slope at cell " + i + ", " + j);
                }
            }

            // Moves each brushed point towards its wanted value (first clamped to the height range) as far as the 2/1 rule
            // allows given the current heights of the others: to the wanted value clamped into the intersection of the
            // range and its triangles' intervals (Allowed). Every pass goes in the order the points were added (alternating
            // the order measured slower: a 100 m ramp across 512 m needs 67 passes this way and does not finish in 256
            // alternating) and passes stop when one moves nothing or after MaxPasses (the state then is legal too). A point whose
            // current height is outside that intersection, that is a corner of an illegal triangle or a point outside the
            // range, is frozen: it never moves, so an illegal triangle stays as it was and no legal one becomes illegal.
            internal MapHeightWrite[] Legalize()
            {
                long floor = Math.Max(d.FloorRaw, -MapDescReader.HeightLimit), ceiling = Math.Min(d.CeilingRaw, MapDescReader.HeightLimit);
                var want = new long[points.Count];
                for (int n = 0; n < want.Length; n++) want[n] = Math.Max(floor, Math.Min(ceiling, wanted[n]));
                int row = width + 1;
                LastPasses = MaxPasses + 1;
                for (int pass = 0; pass < MaxPasses; pass++)
                {
                    bool moved = false;
                    for (int s = 0; s < points.Count; s++)
                    {
                        int n = s;
                        int k = points[n];
                        long h = now[k];
                        if (h == want[n]) continue;
                        long lo = floor, hi = ceiling;
                        Allowed(now, width, depth, k % row, k / row, ref lo, ref hi);
                        if (h < lo || h > hi) continue;
                        long target = Math.Max(lo, Math.Min(hi, want[n]));
                        if (target == h) continue;
                        now[k] = target;
                        moved = true;
                    }
                    if (!moved) { LastPasses = pass + 1; break; }
                }
                var writes = new List<MapHeightWrite>();
                var seen = new HashSet<int>();
                var order = new List<int>(points);
                order.Sort();
                foreach (int k in order)
                    if (seen.Add(k) && now[k] != start[k]) writes.Add(new MapHeightWrite(k % row, k / row, (int)now[k]));
                return writes.ToArray();
            }
        }
    }
}
