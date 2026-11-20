using System;
using WarCore.Content;
using WarCore.Sim;

namespace WarCore.Presentation
{
    // ML-10: one floor plate as the picture reads it. Layer is the plate's layer (1..N, baked order); cells are the map's 1 m
    // cells, X and Z the plate's minimum cell. Top(i, j) is plate point (i, j), i in [0, Width], j in [0, Depth], in metres
    // (converted from the int raw once); the bottom is the top minus Thick. Solid holds the cells left after cutouts,
    // Walkable the solid cells no wall blocks, as in the file.
    public sealed class ViewPlate
    {
        private readonly int[] topRaw;
        private readonly bool[] solid, walkable;

        public int Layer { get; }
        public ulong Id { get; }
        public int X { get; }
        public int Z { get; }
        public int Width { get; }
        public int Depth { get; }
        // World position of the plate's minimum corner, metres.
        public float MinX { get; }
        public float MinZ { get; }
        public float Thick { get; }
        internal long ThickRaw { get; }
        // The top as Sim samples it (the plate's own HeightField at its place on the map).
        internal HeightField Field { get; }

        internal ViewPlate(int layer, WcMapPlate plate, long mapMinXRaw, long mapMinZRaw)
        {
            Layer = layer; Id = plate.Id; X = plate.X; Z = plate.Z; Width = plate.Width; Depth = plate.Depth;
            MinX = Metres(mapMinXRaw + (long)plate.X * SimFix.Scale);
            MinZ = Metres(mapMinZRaw + (long)plate.Z * SimFix.Scale);
            ThickRaw = plate.ThickRaw;
            Thick = Metres(plate.ThickRaw);
            topRaw = new int[(Width + 1) * (Depth + 1)];
            for (int j = 0; j <= Depth; j++)
                for (int i = 0; i <= Width; i++) topRaw[j * (Width + 1) + i] = (int)plate.TopRaw(i, j);
            solid = new bool[Width * Depth];
            walkable = new bool[Width * Depth];
            for (int j = 0; j < Depth; j++)
                for (int i = 0; i < Width; i++)
                {
                    solid[j * Width + i] = plate.IsSolid(i, j);
                    walkable[j * Width + i] = plate.IsWalkable(i, j);
                }
            Field = new HeightField(Width, Depth, SimFix.FromRaw(mapMinXRaw + (long)plate.X * SimFix.Scale),
                SimFix.FromRaw(mapMinZRaw + (long)plate.Z * SimFix.Scale), topRaw);
        }

        internal int TopRaw(int i, int j)
        {
            if (i < 0 || j < 0 || i > Width || j > Depth) throw new ArgumentOutOfRangeException(nameof(i));
            return topRaw[j * (Width + 1) + i];
        }

        public float Top(int i, int j) => (float)TopRaw(i, j) / SimFix.Scale;
        public float Bottom(int i, int j) => (float)(TopRaw(i, j) - ThickRaw) / SimFix.Scale;

        public bool IsSolid(int localX, int localZ) => solid[Index(localX, localZ)];
        public bool IsWalkable(int localX, int localZ) => walkable[Index(localX, localZ)];

        // The lowest bottom over the plate's solid cells (the plate's rectangle when none is solid).
        public float LowestBottom
        {
            get
            {
                long low = long.MaxValue;
                for (int j = 0; j < Depth; j++)
                    for (int i = 0; i < Width; i++)
                        if (solid[j * Width + i])
                            low = Math.Min(low, Math.Min(Math.Min(TopRaw(i, j), TopRaw(i + 1, j)), Math.Min(TopRaw(i, j + 1), TopRaw(i + 1, j + 1))));
                if (low == long.MaxValue)
                    for (int j = 0; j <= Depth; j++)
                        for (int i = 0; i <= Width; i++) low = Math.Min(low, TopRaw(i, j));
                return (float)(low - ThickRaw) / SimFix.Scale;
            }
        }

        // The top plane over the plate's rectangle at (x, z) in metres (cutouts included); false outside the rectangle.
        // Each cell's four corners are coplanar, so the cell's plane gives the height anywhere in the cell.
        public bool TryPlaneTop(float x, float z, out float y)
        {
            y = 0;
            double u = x - MinX, v = z - MinZ;
            if (!(u >= 0 && u < Width && v >= 0 && v < Depth)) return false;
            int i = (int)Math.Floor(u), j = (int)Math.Floor(v);
            double fu = u - i, fv = v - j;
            double h00 = Top(i, j), h10 = Top(i + 1, j), h01 = Top(i, j + 1);
            y = (float)(h00 + (h10 - h00) * fu + (h01 - h00) * fv);
            return true;
        }

        private int Index(int localX, int localZ)
        {
            if (localX < 0 || localZ < 0 || localX >= Width || localZ >= Depth) throw new ArgumentOutOfRangeException(nameof(localX));
            return localZ * Width + localX;
        }

        private static float Metres(long raw) => (float)raw / SimFix.Scale;
    }

    // ML-10: the floor plates of a map for the picture, picking and the self-check; ViewMap.Plates (null without plates).
    // Index k holds layer k + 1.
    public sealed class ViewPlates
    {
        private readonly ViewPlate[] plates;
        internal long MinXRaw { get; }
        internal long MinZRaw { get; }
        internal int MapWidth { get; }
        internal int MapDepth { get; }

        private ViewPlates(WcMap map)
        {
            MinXRaw = map.Min.X.Raw; MinZRaw = map.Min.Z.Raw; MapWidth = (int)map.Width; MapDepth = (int)map.Depth;
            plates = new ViewPlate[map.Plates.Count];
            for (int k = 0; k < plates.Length; k++) plates[k] = new ViewPlate(k + 1, map.Plates[k], MinXRaw, MinZRaw);
        }

        // Null when the map has no plates.
        internal static ViewPlates From(WcMap map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            return map.Plates.Count == 0 ? null : new ViewPlates(map);
        }

        public int Count => plates.Length;
        public ViewPlate this[int index] => plates[index];
        public ViewPlate OfLayer(int layer)
        {
            if (layer < 1 || layer > plates.Length) throw new ArgumentOutOfRangeException(nameof(layer));
            return plates[layer - 1];
        }

        // The plate top under the raw point (x, z) on layer 1..N, the rule of PlateWorld.TryFloor without the walkable test:
        // false off the map, off the plate or on a cutout cell. The cell is MapCells.TryCell's.
        internal bool TryTopRaw(int layer, long xRaw, long zRaw, out long yRaw)
        {
            yRaw = 0;
            if (layer < 1 || layer > plates.Length) return false;
            long rx = xRaw - MinXRaw, rz = zRaw - MinZRaw;
            if (rx < 0 || rz < 0) return false;
            long cx = rx >> 16, cz = rz >> 16;
            if (cx >= MapWidth || cz >= MapDepth) return false;
            ViewPlate p = plates[layer - 1];
            int i = (int)cx - p.X, j = (int)cz - p.Z;
            if (i < 0 || j < 0 || i >= p.Width || j >= p.Depth || !p.IsSolid(i, j)) return false;
            if (!p.Field.TryHeightAt(SimFix.FromRaw(xRaw), SimFix.FromRaw(zRaw), out SimFix y)) return false;
            yRaw = y.Raw;
            return true;
        }
    }
}
