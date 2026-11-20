using System;
using System.Collections.Generic;

namespace WarCore.Content.MapTools
{
    // The nearest place of a polyline to a point: squared distance Num / Den (Den = 1 when it is an end point of the segment,
    // Den = L2 when it is inside), the segment's index, t = (X - P) . (Q - P) and L2 = |Q - P|^2 of that segment.
    internal struct MapLineNear
    {
        internal int Seg;
        internal long Num, Den, T, L2;
    }

    // ED-3a integer pieces of the terrain composer (design: dispatch ED-3a C-b). All arithmetic is integer; bounds are noted
    // where a product could get large.
    internal static class MapTerrainMath
    {
        // Largest step between axis neighbours that keeps both triangles of every cell within 2/1:
        // 2 * 92681^2 = 17 179 535 522 <= 4 S^2 = 17 179 869 184 < 2 * 92682^2.
        internal const long L = 92681;
        internal const int Full = 1024;
        // Stand-in for "no fixed point": far beyond any height (|h| < 2^29) plus L times any distance (< 2^27).
        internal const long Inf = 1L << 60;

        // Disc falloff W(r, d2) = 1024 (r^2 - d2)^2 / r^4 for d2 < r^2, else 0. r <= 256: 1024 r^4 <= 2^42.
        internal static long Weight(long r, long d2)
        {
            long r2 = r * r;
            if (d2 >= r2) return 0;
            long k = r2 - d2;
            return Full * k * k / (r2 * r2);
        }

        // Nearest segment of the polyline (every point times scale) to (x, z); on a tie the earlier segment. With coordinates
        // in [-1024, 1536] (times 2 for cells) every |dx|, |dz| <= 5120, so cross^2 < 2^52 and L2 < 2^26.
        internal static MapLineNear Near(IReadOnlyList<MapXZ> points, long scale, long x, long z)
        {
            var best = new MapLineNear();
            for (int n = 0; n + 1 < points.Count; n++)
            {
                long px = points[n].X * scale, pz = points[n].Z * scale;
                long dx = points[n + 1].X * scale - px, dz = points[n + 1].Z * scale - pz;
                long ex = x - px, ez = z - pz;
                var c = new MapLineNear { Seg = n, L2 = dx * dx + dz * dz, T = ex * dx + ez * dz };
                if (c.T <= 0) { c.Num = ex * ex + ez * ez; c.Den = 1; }
                else if (c.T >= c.L2)
                {
                    long fx = ex - dx, fz = ez - dz;
                    c.Num = fx * fx + fz * fz; c.Den = 1;
                }
                else
                {
                    long cross = ex * dz - ez * dx;
                    c.Num = cross * cross; c.Den = c.L2;
                }
                if (n == 0 || Closer(c, best)) best = c;
            }
            return best;
        }

        // a.Num / a.Den < b.Num / b.Den, without the overflow of cross-multiplying: whole parts first, then the remainders
        // (each < its Den < 2^26, so the products stay below 2^52).
        internal static bool Closer(MapLineNear a, MapLineNear b)
        {
            long qa = a.Num / a.Den, qb = b.Num / b.Den;
            if (qa != qb) return qa < qb;
            return a.Num % a.Den * b.Den < b.Num % b.Den * a.Den;
        }

        // Squared distance <= w^2. w <= 512 (doubled), Den < 2^26: w^2 Den < 2^44.
        internal static bool InBand(MapLineNear near, long w) => near.Num <= w * w * near.Den;

        // Band falloff Wl(w) = (1024 q / B) q / B with B = w^2 Den and q = B - Num (q <= 0: 0). Two divisions: 1024 q < 2^50.
        internal static long LineWeight(MapLineNear near, long w)
        {
            long b = w * w * near.Den, q = b - near.Num;
            if (q <= 0) return 0;
            return Full * q / b * q / b;
        }

        // Max envelope with slope L, in place: t(p) = max over q of (t(q) - L |p - q|_1). Two passes (forward over left and
        // up, backward over right and down) give the exact L1 transform.
        internal static void Fill(long[] t, int cols, int rows)
        {
            for (int j = 0; j < rows; j++)
                for (int i = 0; i < cols; i++)
                {
                    int p = j * cols + i;
                    long v = t[p];
                    if (i > 0) v = Math.Max(v, t[p - 1] - L);
                    if (j > 0) v = Math.Max(v, t[p - cols] - L);
                    t[p] = v;
                }
            for (int j = rows - 1; j >= 0; j--)
                for (int i = cols - 1; i >= 0; i--)
                {
                    int p = j * cols + i;
                    long v = t[p];
                    if (i + 1 < cols) v = Math.Max(v, t[p + 1] - L);
                    if (j + 1 < rows) v = Math.Max(v, t[p + cols] - L);
                    t[p] = v;
                }
        }

        // Upper envelope of the fixed points, in place: Up(p) = min over fixed f of (F(f) + L |p - f|_1). v holds F at fixed
        // points and Inf elsewhere; src carries, for each point, the stamp number of the fixed point that gave its value.
        internal static void Upper(long[] v, int[] src, int cols, int rows)
        {
            for (int j = 0; j < rows; j++)
                for (int i = 0; i < cols; i++)
                {
                    int p = j * cols + i;
                    if (i > 0) Take(v, src, p, p - 1);
                    if (j > 0) Take(v, src, p, p - cols);
                }
            for (int j = rows - 1; j >= 0; j--)
                for (int i = cols - 1; i >= 0; i--)
                {
                    int p = j * cols + i;
                    if (i + 1 < cols) Take(v, src, p, p + 1);
                    if (j + 1 < rows) Take(v, src, p, p + cols);
                }
        }

        private static void Take(long[] v, int[] src, int p, int from)
        {
            long c = v[from] + L;
            if (c < v[p]) { v[p] = c; src[p] = src[from]; }
        }

        internal static long ISqrt(long m)
        {
            long r = (long)Math.Sqrt(m);
            while (r * r > m) r--;
            while ((r + 1) * (r + 1) <= m) r++;
            return r;
        }

        internal static int Clamp(long v, int lo, int hi) => (int)Math.Max(lo, Math.Min(hi, v));
    }
}
