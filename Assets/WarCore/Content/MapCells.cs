using WarCore.Sim;

namespace WarCore.Content
{
    // ML-7b: the one rule for the cell under a point, shared by the bake, the gate, the world build and deployment. A
    // millimetre coordinate becomes SimFix.Ratio(mm, 1000); the cell is (raw - map minimum raw) >> 16 on each axis, half-open
    // (a cell owns its minimum edges), the same as NavGrid.TryCell and LayeredNav.TryNode.
    public static class MapCells
    {
        public static SimFix Mm(long mm) => SimFix.Ratio(mm, 1000);

        // False off the map.
        public static bool TryCell(WcMap map, SimFix x, SimFix z, out int cx, out int cz)
        {
            long ix = (x.Raw - map.Min.X.Raw) >> 16, iz = (z.Raw - map.Min.Z.Raw) >> 16;
            bool inside = ix >= 0 && iz >= 0 && ix < map.Width && iz < map.Depth;
            cx = inside ? (int)ix : -1;
            cz = inside ? (int)iz : -1;
            return inside;
        }

        // True when (x, z) is on a solid cell of plate layer 1..N (baked order): on the map, inside the plate, not a cutout.
        public static bool OnSolidPlate(WcMap map, int layer, SimFix x, SimFix z)
        {
            if (layer < 1 || layer > map.Plates.Count || !TryCell(map, x, z, out int cx, out int cz)) return false;
            WcMapPlate p = map.Plates[layer - 1];
            int i = cx - p.X, j = cz - p.Z;
            return i >= 0 && j >= 0 && i < p.Width && j < p.Depth && p.IsSolid(i, j);
        }
    }
}
