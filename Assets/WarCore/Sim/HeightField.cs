using System;

namespace WarCore.Sim
{
    // Immutable height field on one-metre cells. Points are (Width+1) x (Depth+1), row-major (Z rows, then X), raw SimFix Y.
    // Each cell is cut along the fixed diagonal (x,z) -> (x+1,z+1) into triangle A (local u >= v: points (0,0) (1,0) (1,1))
    // and triangle B (u < v: points (0,0) (0,1) (1,1)). HeightAt is the integer sample of that surface (floor, toward
    // negative infinity); it is not the continuous surface, which future exact intersection code reads from the same
    // triangles. All arithmetic is long: |h| < 2^29 keeps every product below 2^63.
    public sealed class HeightField
    {
        // Height limit of the provider (3b r4 4.4): every point satisfies |h| < HeightLimitRaw.
        public const long HeightLimitRaw = 1L << 29;
        // Legal-surface bound: any triangle steeper than MaxSlopeNumerator / MaxSlopeDenominator is not a legal height field.
        public const int MaxSlopeNumerator = 2, MaxSlopeDenominator = 1;
        // Bounds for the walkable-slope ratio num/den: den in [1, MaxWalkableDenominator], num in [0, MaxSlopeNumerator * den].
        public const int MaxWalkableDenominator = 4096;
        public const int MaxSide = 512;

        private readonly int[] points;
        public int Width { get; }
        public int Depth { get; }
        public int PointsX => Width + 1;
        public int PointsZ => Depth + 1;
        public SimFix OriginX { get; }
        public SimFix OriginZ { get; }
        public ulong Digest { get; }

        public HeightField(int width, int depth, SimFix originX, SimFix originZ, ReadOnlySpan<int> pointsRaw)
        {
            if (width < 1 || width > MaxSide || depth < 1 || depth > MaxSide) throw new ArgumentOutOfRangeException(nameof(width));
            long limit = SimVec3.WorldLimitMetres * SimFix.Scale;
            if (originX.Raw % SimFix.Scale != 0 || originZ.Raw % SimFix.Scale != 0
                || originX.Raw < -limit || originZ.Raw < -limit
                || originX.Raw > limit - width * SimFix.Scale || originZ.Raw > limit - depth * SimFix.Scale)
                throw new ArgumentOutOfRangeException(nameof(originX));
            if (pointsRaw.Length != (width + 1) * (depth + 1)) throw new ArgumentException("Point count differs from the grid.", nameof(pointsRaw));
            foreach (int h in pointsRaw)
                if (h <= -HeightLimitRaw || h >= HeightLimitRaw) throw new ArgumentOutOfRangeException(nameof(pointsRaw), "Height outside the provider limit.");
            Width = width; Depth = depth; OriginX = originX; OriginZ = originZ;
            points = pointsRaw.ToArray();
            var hash = new StateHash();
            hash.Add(width); hash.Add(depth); hash.Add(originX.Raw); hash.Add(originZ.Raw);
            foreach (int h in points) hash.Add(h);
            Digest = hash.Value;
        }

        // Every grid point, including the outer ring: i in [0, Width], j in [0, Depth].
        public int VertexRaw(int i, int j)
        {
            if (i < 0 || j < 0 || i > Width || j > Depth) throw new ArgumentOutOfRangeException(nameof(i));
            return points[j * (Width + 1) + i];
        }

        // Half-open domain like NavGrid.TryCell: the minimum edge is inside, the maximum edge is not.
        public bool TryHeightAt(SimFix x, SimFix z, out SimFix height)
        {
            height = default;
            if (x.Raw < OriginX.Raw || x.Raw >= OriginX.Raw + Width * SimFix.Scale
                || z.Raw < OriginZ.Raw || z.Raw >= OriginZ.Raw + Depth * SimFix.Scale) return false;
            long rx = x.Raw - OriginX.Raw, rz = z.Raw - OriginZ.Raw;
            int cx = (int)(rx / SimFix.Scale), cz = (int)(rz / SimFix.Scale);
            long u = rx % SimFix.Scale, v = rz % SimFix.Scale;
            int row = cz * (Width + 1) + cx;
            long h00 = points[row], h10 = points[row + 1], h01 = points[row + Width + 1], h11 = points[row + Width + 2];
            long numerator = u >= v
                ? h00 * SimFix.Scale + (h10 - h00) * u + (h11 - h10) * v
                : h00 * SimFix.Scale + (h11 - h01) * u + (h01 - h00) * v;
            long quotient = numerator / SimFix.Scale;
            if (numerator % SimFix.Scale < 0) quotient--;
            height = SimFix.FromRaw(quotient);
            return true;
        }

        // True when both triangles of cell (cx, cz) are no steeper than num/den: (a^2 + b^2) * den^2 <= num^2 * S^2, where
        // a and b are the two orthogonal height differences of the triangle's plane over one metre (raw units).
        // den in [1, MaxWalkableDenominator], num in [0, MaxSlopeNumerator * den]; the products stay below 2^63 (see
        // docs/wp22-handoff.md for the bound).
        public bool CellWithinSlope(int cx, int cz, long num, long den)
        {
            if (cx < 0 || cz < 0 || cx >= Width || cz >= Depth) throw new ArgumentOutOfRangeException(nameof(cx));
            if (den < 1 || den > MaxWalkableDenominator || num < 0 || num > MaxSlopeNumerator * den) throw new ArgumentOutOfRangeException(nameof(num));
            int row = cz * (Width + 1) + cx;
            long h00 = points[row], h10 = points[row + 1], h01 = points[row + Width + 1], h11 = points[row + Width + 2];
            long limit = num * num * SimFix.Scale * SimFix.Scale, d2 = den * den;
            return Within(h10 - h00, h11 - h10, limit, d2) && Within(h11 - h01, h01 - h00, limit, d2);
        }

        private static bool Within(long a, long b, long limit, long d2)
        {
            long m = a * a + b * b;
            // m * d2 would overflow only when it is far above any legal limit (< 2^58).
            return m <= long.MaxValue / d2 && m * d2 <= limit;
        }

        // The provider's legal-surface rule for one cell.
        public bool CellIsLegal(int cx, int cz) => CellWithinSlope(cx, cz, MaxSlopeNumerator, MaxSlopeDenominator);

        // L9: is some point of the segment from -> to strictly below the ground? at = the first such sample (smallest t).
        // Samples: both ends and every crossing of the XZ projection with the lines x = OriginX + i S, z = OriginZ + j S and
        // x - z = OriginX - OriginZ + k S (the cell diagonals). A crossing is t = n / d with 0 <= n <= d; its coordinates are
        // from + floor(n * delta / d) (toward negative infinity). A sample counts only if its exact point lies in the closed
        // field [Origin, Origin + Size] (decided before rounding: floor(q) < max, or floor(q) == max and the division is
        // exact), and it is blocked when the closed-domain height there is strictly above its Y. Each family stops at its
        // first blocked sample; the earliest t wins (n1 d2 vs n2 d1), ties go to start, x, z, diagonal, end in that order.
        // Between two neighbouring samples the projection stays in one triangle, where ground and segment are both linear,
        // so their difference has its minimum at a sample; the result can differ from the exact one only within a few raw.
        // Widths: |coordinate| <= 2^32 (else ArgumentOutOfRangeException; no world check, so rays may end off the map),
        // |d| <= 2^34, n * delta <= 2^67 (FixedInt128), quotient <= 2^33, t comparison <= 2^68 (FixedUInt128), closed-domain
        // height numerator < 2^48; at most 2053 samples (MaxSide 512); no allocation.
        public bool TryFirstBelow(SimVec3 from, SimVec3 to, out SimVec3 at)
        {
            if (Outside(from.X.Raw) || Outside(from.Y.Raw) || Outside(from.Z.Raw)
                || Outside(to.X.Raw) || Outside(to.Y.Raw) || Outside(to.Z.Raw)) throw new ArgumentOutOfRangeException(nameof(to));
            long fx = from.X.Raw, fy = from.Y.Raw, fz = from.Z.Raw;
            long dx = to.X.Raw - fx, dy = to.Y.Raw - fy, dz = to.Z.Raw - fz;
            var best = new Best();
            Sample(ref best, 0, 1, fx, fy, fz, dx, dy, dz);
            Family(ref best, fx, dx, OriginX.Raw, 0, Width, fx, fy, fz, dx, dy, dz);
            Family(ref best, fz, dz, OriginZ.Raw, 0, Depth, fx, fy, fz, dx, dy, dz);
            Family(ref best, fx - fz, dx - dz, OriginX.Raw - OriginZ.Raw, -Depth, Width, fx, fy, fz, dx, dy, dz);
            Sample(ref best, 1, 1, fx, fy, fz, dx, dy, dz);
            at = best.At;
            return best.Found;
        }

        private struct Best { internal bool Found; internal long N, D; internal SimVec3 At; }

        private void Family(ref Best best, long s0, long ds, long origin, int kLo, int kHi,
            long fx, long fy, long fz, long dx, long dy, long dz)
        {
            if (ds == 0) return;
            long S = SimFix.Scale;
            if (ds > 0)
            {
                long k0 = CeilDiv(s0 - origin, S), k1 = FloorDiv(s0 + ds - origin, S);
                if (k0 < kLo) k0 = kLo; if (k1 > kHi) k1 = kHi;
                for (long k = k0; k <= k1; k++)
                    if (Sample(ref best, origin + k * S - s0, ds, fx, fy, fz, dx, dy, dz)) return;
            }
            else
            {
                long k0 = FloorDiv(s0 - origin, S), k1 = CeilDiv(s0 + ds - origin, S);
                if (k0 > kHi) k0 = kHi; if (k1 < kLo) k1 = kLo;
                for (long k = k0; k >= k1; k--)
                    if (Sample(ref best, s0 - (origin + k * S), -ds, fx, fy, fz, dx, dy, dz)) return;
            }
        }

        private bool Sample(ref Best best, long n, long d, long fx, long fy, long fz, long dx, long dy, long dz)
        {
            if (n < 0 || n > d) return false;
            long x = fx + FloorMulDiv(n, dx, d, out bool xExact), y = fy + FloorMulDiv(n, dy, d, out _), z = fz + FloorMulDiv(n, dz, d, out bool zExact);
            if (!InClosedDomain(x, xExact, z, zExact) || !ClosedHeight(x, z, out long h) || h <= y) return false;
            if (!best.Found || FixedUInt128.Multiply((ulong)n, (ulong)best.D).CompareTo(FixedUInt128.Multiply((ulong)best.N, (ulong)d)) < 0)
            { best.Found = true; best.N = n; best.D = d; best.At = new SimVec3(SimFix.FromRaw(x), SimFix.FromRaw(y), SimFix.FromRaw(z)); }
            return true;
        }

        private const long LineBound = 1L << 32;
        private static bool Outside(long c) => c > LineBound || c < -LineBound;

        // The exact point is n/d along the line; x and z are its floors. Floor(q) >= origin iff q >= origin (integer bound);
        // q <= max iff floor(q) < max, or floor(q) == max and q is exact.
        private bool InClosedDomain(long x, bool xExact, long z, bool zExact)
        {
            long rx = x - OriginX.Raw, rz = z - OriginZ.Raw, mx = Width * SimFix.Scale, mz = Depth * SimFix.Scale;
            if (rx < 0 || rz < 0 || rx > mx || rz > mz) return false;
            return (rx < mx || xExact) && (rz < mz || zExact);
        }

        // HeightAt on the closed field: the far edges belong to the last cell (u or v may reach S); same triangles and floor.
        private bool ClosedHeight(long x, long z, out long height)
        {
            height = 0;
            long rx = x - OriginX.Raw, rz = z - OriginZ.Raw;
            if (rx < 0 || rz < 0 || rx > Width * SimFix.Scale || rz > Depth * SimFix.Scale) return false;
            long cx = rx / SimFix.Scale, cz = rz / SimFix.Scale;
            if (cx == Width) cx = Width - 1;
            if (cz == Depth) cz = Depth - 1;
            long u = rx - cx * SimFix.Scale, v = rz - cz * SimFix.Scale;
            long row = cz * (Width + 1) + cx;
            long h00 = points[row], h10 = points[row + 1], h01 = points[row + Width + 1], h11 = points[row + Width + 2];
            long numerator = u >= v
                ? h00 * SimFix.Scale + (h10 - h00) * u + (h11 - h10) * v
                : h00 * SimFix.Scale + (h11 - h01) * u + (h01 - h00) * v;
            height = FloorDiv(numerator, SimFix.Scale);
            return true;
        }

        private static long FloorDiv(long a, long b) { long q = a / b; if ((a % b != 0) && ((a < 0) != (b < 0))) q--; return q; }
        private static long CeilDiv(long a, long b) => -FloorDiv(-a, b);

        private static long FloorMulDiv(long a, long b, long d, out bool exact)
        {
            FixedInt128 p = FixedInt128.Product(a, b);
            ulong q = p.Magnitude.DivideToUInt64((ulong)d);
            exact = FixedUInt128.Multiply(q, (ulong)d).CompareTo(p.Magnitude) == 0;
            long r = (long)q;
            return p.Negative ? (exact ? -r : -r - 1) : r;
        }
    }
}
