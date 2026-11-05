using System;
using WarCore.Content;

namespace WarCore.Presentation
{
    // Float view of a .wcmap for renderers: centre and full extents of each wall, world coordinates in metres.
    public readonly struct ViewWall
    {
        public ulong Chunk { get; }
        public ulong Placement { get; }
        public float CenterX { get; }
        public float CenterY { get; }
        public float CenterZ { get; }
        public float SizeX { get; }
        public float SizeY { get; }
        public float SizeZ { get; }

        public ViewWall(ulong chunk, ulong placement, float cx, float cy, float cz, float sx, float sy, float sz)
        {
            Chunk = chunk; Placement = placement;
            CenterX = cx; CenterY = cy; CenterZ = cz;
            SizeX = sx; SizeY = sy; SizeZ = sz;
        }
    }

    public sealed class ViewMap
    {
        private readonly ViewWall[] walls;
        private readonly byte[] passability;
        private readonly int[] heightRaw;
        private readonly string[] surfaceNames;
        private readonly byte[] surfaceCells;

        public string MapId { get; }
        // Hex of the file's trailing whole-file SHA-256; the reader verifies it covers every earlier byte.
        public string FileHash { get; }
        public int Width { get; }
        public int Depth { get; }
        public float MinX { get; }
        public float MinZ { get; }
        public float FloorY { get; }
        public ReadOnlySpan<ViewWall> Walls => walls;

        private ViewMap(WcMap map, string fileHash)
        {
            MapId = map.MapId;
            FileHash = fileHash;
            Width = (int)map.Width;
            Depth = (int)map.Depth;
            MinX = Metres(map.Min.X.Raw);
            MinZ = Metres(map.Min.Z.Raw);
            FloorY = Metres(map.FlatHeight.Raw);
            if (map.HasHeightField)
            {
                heightRaw = new int[map.HeightPoints.Count];
                for (int i = 0; i < heightRaw.Length; i++) heightRaw[i] = map.HeightPoints[i];
            }
            if (map.HasSurfaceTable)
            {
                surfaceNames = new string[map.SurfaceNames.Count];
                for (int i = 0; i < surfaceNames.Length; i++) surfaceNames[i] = map.SurfaceNames[i];
                surfaceCells = new byte[map.SurfaceCells.Count];
                for (int i = 0; i < surfaceCells.Length; i++) surfaceCells[i] = map.SurfaceCells[i];
            }
            passability = new byte[map.Passability.Count];
            for (int i = 0; i < passability.Length; i++)
                passability[i] = map.Passability[i];
            walls = new ViewWall[map.Walls.Count];
            for (int i = 0; i < walls.Length; i++)
            {
                WcMapWall wall = map.Walls[i];
                walls[i] = new ViewWall(wall.Chunk, wall.Placement,
                    Metres(map.Min.X.Raw + wall.LocalCenter.X.Raw), Metres(map.FlatHeight.Raw + wall.LocalCenter.Y.Raw),
                    Metres(map.Min.Z.Raw + wall.LocalCenter.Z.Raw),
                    Metres(wall.HalfX.Raw * 2), Metres(wall.Height.Raw), Metres(wall.HalfZ.Raw * 2));
            }
        }

        public static ViewMap Parse(byte[] bytes)
        {
            WcMap map = WcMapReader.Read(bytes);
            return new ViewMap(map, BitConverter.ToString(bytes, bytes.Length - 32, 32).Replace("-", ""));
        }

        // Navigation view: 1 = passable.
        public bool IsPassable(int x, int z)
        {
            if (x < 0 || z < 0 || x >= Width || z >= Depth)
                throw new ArgumentOutOfRangeException(nameof(x));
            int index = z * Width + x;
            return (passability[index >> 3] & (1 << (index & 7))) != 0;
        }

        public bool HasHeightField => heightRaw != null;
        public bool HasSurfaceTable => surfaceNames != null;

        // Raw Y (SimFix raw, not a float) of height point (i, j), i in [0, Width], j in [0, Depth].
        public int VertexRaw(int i, int j)
        {
            if (heightRaw == null) throw new InvalidOperationException("Map has no height field.");
            if (i < 0 || j < 0 || i > Width || j > Depth) throw new ArgumentOutOfRangeException(nameof(i));
            return heightRaw[j * (Width + 1) + i];
        }

        public int SurfaceCount => surfaceNames == null ? 0 : surfaceNames.Length;
        public string SurfaceName(int index) => surfaceNames[index];

        // Index into the map's surface-name table for navigation cell (x, z).
        public int SurfaceIndex(int x, int z)
        {
            if (surfaceCells == null) throw new InvalidOperationException("Map has no surface table.");
            if (x < 0 || z < 0 || x >= Width || z >= Depth) throw new ArgumentOutOfRangeException(nameof(x));
            return surfaceCells[z * Width + x];
        }

        private static float Metres(long raw) => (float)raw / 65536f;
    }
}
