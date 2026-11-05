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
        public const int MaxSide = 256;

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
    }
}
