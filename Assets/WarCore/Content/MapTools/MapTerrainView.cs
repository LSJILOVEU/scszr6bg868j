using System;
using WarCore.Sim;

namespace WarCore.Content.MapTools
{
    // Per-cell terrain figures of a description for a flat picture (the map preview). Cells run row by row (z first, then x).
    // Presentation may not name the simulation namespace, so the height arithmetic lives here.
    public static class MapTerrainView
    {
        public const int ShadeFlat = 160, ShadeMin = 40, ShadeMax = 240;
        private const int ShadeStep = 48;

        // Light comes from -x -z. With h(a, b) the height point one step a along x and b along z from the cell's corner (Heights
        // runs z first, then x): gx = (h(1,0) + h(1,1) - h(0,0) - h(0,1)) / 2, gz = (h(0,1) + h(1,1) - h(0,0) - h(1,0)) / 2 and
        // shade = clamp(160 - (gx + gz) * 48 / 65536, 40, 240); divisions truncate toward zero. Without heights every cell is 160.
        public static int[] Shade(MapDesc desc)
        {
            if (desc == null) throw new ArgumentNullException(nameof(desc));
            int w = desc.Size.X, d = desc.Size.Z;
            var shade = new int[w * d];
            for (int z = 0; z < d; z++)
                for (int x = 0; x < w; x++)
                {
                    if (desc.Heights == null) { shade[z * w + x] = ShadeFlat; continue; }
                    long h00 = desc.Heights[z][x], h10 = desc.Heights[z][x + 1], h01 = desc.Heights[z + 1][x], h11 = desc.Heights[z + 1][x + 1];
                    long gx = (h10 + h11 - (h00 + h01)) / 2, gz = (h01 + h11 - (h00 + h10)) / 2;
                    long value = ShadeFlat - (gx + gz) * ShadeStep / SimFix.Scale;
                    shade[z * w + x] = (int)Math.Max(ShadeMin, Math.Min(ShadeMax, value));
                }
            return shade;
        }

        // True where a cell is steeper than the pack's walkable slope, by the same test TerrainDerivation applies (a pack without
        // terrain rules uses the legal-surface bound). No heights: all false.
        public static bool[] Steep(MapDesc desc, ContentPack pack)
        {
            if (desc == null) throw new ArgumentNullException(nameof(desc));
            if (pack == null) throw new ArgumentNullException(nameof(pack));
            int w = desc.Size.X, d = desc.Size.Z;
            var steep = new bool[w * d];
            if (desc.Heights == null) return steep;
            long num = pack.Terrain?.MaxWalkableNum ?? HeightField.MaxSlopeNumerator, den = pack.Terrain?.MaxWalkableDen ?? HeightField.MaxSlopeDenominator;
            // The slope of a cell does not depend on where the map stands, so the field is built at the origin.
            var points = new int[(w + 1) * (d + 1)];
            for (int j = 0; j <= d; j++)
                for (int i = 0; i <= w; i++) points[j * (w + 1) + i] = desc.Heights[j][i];
            var field = new HeightField(w, d, SimFix.FromInt(0), SimFix.FromInt(0), points);
            for (int z = 0; z < d; z++)
                for (int x = 0; x < w; x++) steep[z * w + x] = !field.CellWithinSlope(x, z, num, den);
            return steep;
        }
    }
}
