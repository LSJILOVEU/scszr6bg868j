using System;
using WarCore.Sim;

namespace WarCore.Presentation
{
    // Ground height of a map's height field for renderers. Sampling is Sim's own HeightField built from the view's point
    // heights, so a raw query answers exactly what Sim answers at the same point (floor toward negative infinity).
    public sealed class ViewGround
    {
        private readonly HeightField field;

        private ViewGround(HeightField field, ViewMap map)
        {
            this.field = field;
            MinXRaw = map.MinXRaw; MinZRaw = map.MinZRaw;
            MaxXRaw = map.MinXRaw + map.Width * SimFix.Scale; MaxZRaw = map.MinZRaw + map.Depth * SimFix.Scale;
            long low = long.MaxValue, high = long.MinValue;
            for (int j = 0; j <= map.Depth; j++)
                for (int i = 0; i <= map.Width; i++)
                {
                    long h = map.VertexRaw(i, j);
                    if (h < low) low = h;
                    if (h > high) high = h;
                }
            MinHeightRaw = low; MaxHeightRaw = high;
        }

        // The closed rectangle of the height field and its lowest and highest grid point (raw).
        public long MinXRaw { get; }
        public long MaxXRaw { get; }
        public long MinZRaw { get; }
        public long MaxZRaw { get; }
        public long MinHeightRaw { get; }
        public long MaxHeightRaw { get; }

        // Null when the map has no height field (flat maps and surface-only terrain maps).
        public static ViewGround For(ViewMap map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            return map.HasHeightField ? new ViewGround(Field(map), map) : null;
        }

        // Same construction as TerrainDerivation: absolute raw heights, origin at the map's Min.X, Min.Z.
        internal static HeightField Field(ViewMap map)
        {
            var points = new int[(map.Width + 1) * (map.Depth + 1)];
            for (int j = 0; j <= map.Depth; j++)
                for (int i = 0; i <= map.Width; i++) points[j * (map.Width + 1) + i] = map.VertexRaw(i, j);
            return new HeightField(map.Width, map.Depth, SimFix.FromRaw(map.MinXRaw), SimFix.FromRaw(map.MinZRaw), points);
        }

        // Raw ground height at raw (x, z); false outside the height field.
        public bool TryHeightRaw(long xRaw, long zRaw, out long yRaw)
        {
            bool inside = field.TryHeightAt(SimFix.FromRaw(xRaw), SimFix.FromRaw(zRaw), out SimFix y);
            yRaw = inside ? y.Raw : 0;
            return inside;
        }

        // L9's line test (HeightField.TryFirstBelow) on raw points: the first sample strictly below the ground on the segment.
        public bool TryFirstBelowRaw(long x0, long y0, long z0, long x1, long y1, long z1, out long x, out long y, out long z)
        {
            bool found = field.TryFirstBelow(Raw(x0, y0, z0), Raw(x1, y1, z1), out SimVec3 at);
            x = found ? at.X.Raw : 0; y = found ? at.Y.Raw : 0; z = found ? at.Z.Raw : 0;
            return found;
        }

        // Is the raw point strictly below the ground? A zero-length segment samples only its end points, so this is exactly
        // the test TryFirstBelow applies at every sample (closed domain; false outside it).
        public bool IsBelowRaw(long x, long y, long z) => field.TryFirstBelow(Raw(x, y, z), Raw(x, y, z), out _);

        // The ground height that IsBelowRaw tests against at a raw (x, z) in the closed rectangle: the lowest y that is not below.
        public long ClosedHeightRaw(long x, long z)
        {
            if (x < MinXRaw || x > MaxXRaw || z < MinZRaw || z > MaxZRaw) throw new ArgumentOutOfRangeException(nameof(x));
            long lo = MinHeightRaw - 1, hi = MaxHeightRaw; // below at lo + 1 .. ; never below at hi
            while (hi - lo > 1)
            {
                long mid = lo + (hi - lo) / 2;
                if (IsBelowRaw(x, mid, z)) lo = mid; else hi = mid;
            }
            return hi;
        }

        private static SimVec3 Raw(long x, long y, long z) => new SimVec3(SimFix.FromRaw(x), SimFix.FromRaw(y), SimFix.FromRaw(z));

        // Ground height in metres at (x, z) metres; fallback outside the height field.
        public float HeightAt(float x, float z, float fallback)
        {
            long xRaw = (long)Math.Floor((double)x * 65536), zRaw = (long)Math.Floor((double)z * 65536);
            return TryHeightRaw(xRaw, zRaw, out long yRaw) ? (float)yRaw / 65536f : fallback;
        }
    }
}
