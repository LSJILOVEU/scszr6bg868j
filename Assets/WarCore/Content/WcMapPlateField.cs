using System;
using WarCore.Sim;

namespace WarCore.Content
{
    // ML-5: the plates of a validated .wcmap as the crossing test's PlateField (Sim). Top points come from the same table
    // as the bake and the rules (WcMapPlate.TopRaw); the solid masks already have the cutouts removed.
    public static class WcMapPlateField
    {
        // Null when the map has no plates.
        public static PlateField Build(WcMap map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (map.Plates.Count == 0) return null;
            var slabs = new PlateSlab[map.Plates.Count];
            for (int k = 0; k < slabs.Length; k++)
            {
                WcMapPlate p = map.Plates[k];
                var points = new int[(p.Width + 1) * (p.Depth + 1)];
                for (int j = 0; j <= p.Depth; j++)
                    for (int i = 0; i <= p.Width; i++) points[j * (p.Width + 1) + i] = (int)p.TopRaw(i, j);
                var solid = new bool[p.Width * p.Depth];
                for (int j = 0; j < p.Depth; j++)
                    for (int i = 0; i < p.Width; i++) solid[j * p.Width + i] = p.IsSolid(i, j);
                slabs[k] = new PlateSlab(p.Id, p.X, p.Z, p.Width, p.Depth, points, p.ThickRaw, solid);
            }
            return new PlateField((int)map.Width, (int)map.Depth, map.Min.X, map.Min.Z, slabs);
        }
    }
}
