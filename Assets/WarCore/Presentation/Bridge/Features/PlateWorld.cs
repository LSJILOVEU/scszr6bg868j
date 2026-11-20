using System;
using WarCore.Content;
using WarCore.Sim;

namespace WarCore.Presentation
{
    // The floor plates of a map as the bridge reads them: the layered graph (layer 0 = the ground, 1..N = the plates in baked
    // order) and each plate's floored top surface (K11). A point (x, z) falls in the cell whose minimum edges contain it.
    internal sealed class PlateWorld
    {
        private readonly WcMap map;
        private readonly HeightField[] tops;
        internal LayeredNav Nav { get; }
        internal int LayerCount => Nav.LayerCount;

        internal PlateWorld(WcMap map, LayeredNav nav)
        {
            this.map = map ?? throw new ArgumentNullException(nameof(map));
            Nav = nav ?? throw new ArgumentNullException(nameof(nav));
            tops = new HeightField[map.Plates.Count];
            for (int k = 0; k < tops.Length; k++) tops[k] = map.Plates[k].TopField(map.Min.X, map.Min.Z);
        }

        // The plate surface under (x, z) on a layer 1..N: false off the map, off the plate, on a cutout cell, or (walkable) on a
        // cell that is not walkable.
        internal bool TryFloor(int layer, SimFix x, SimFix z, bool walkable, out SimFix y)
        {
            y = default;
            if (layer < 1 || layer >= Nav.LayerCount) return false;
            long cx = (x.Raw - map.Min.X.Raw) >> 16, cz = (z.Raw - map.Min.Z.Raw) >> 16;
            if (cx < 0 || cz < 0 || cx >= Nav.Width || cz >= Nav.Depth) return false;
            if (!Nav.TryNode(layer, (int)cx, (int)cz, out int node) || walkable && !Nav.IsPassable(node)) return false;
            return tops[layer - 1].TryHeightAt(x, z, out y);
        }
    }
}
