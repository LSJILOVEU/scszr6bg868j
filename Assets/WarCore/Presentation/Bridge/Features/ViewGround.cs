using System;
using WarCore.Sim;

namespace WarCore.Presentation
{
    // Ground height of a map's height field for renderers. Sampling is Sim's own HeightField built from the view's point
    // heights, so a raw query answers exactly what Sim answers at the same point (floor toward negative infinity).
    public sealed class ViewGround
    {
        private readonly HeightField field;

        private ViewGround(HeightField field) { this.field = field; }

        // Null when the map has no height field (flat maps and surface-only terrain maps).
        public static ViewGround For(ViewMap map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            return map.HasHeightField ? new ViewGround(Field(map)) : null;
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

        // Ground height in metres at (x, z) metres; fallback outside the height field.
        public float HeightAt(float x, float z, float fallback)
        {
            long xRaw = (long)Math.Floor((double)x * 65536), zRaw = (long)Math.Floor((double)z * 65536);
            return TryHeightRaw(xRaw, zRaw, out long yRaw) ? (float)yRaw / 65536f : fallback;
        }
    }
}
