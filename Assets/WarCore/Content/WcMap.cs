using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using WarCore.Sim;

namespace WarCore.Content
{
    public readonly struct WcMapLayer
    {
        public string Name { get; }
        public byte Bit { get; }
        public uint Mask => 1u << Bit;
        public WcMapLayer(string name, byte bit) { Name = name; Bit = bit; }
    }

    public readonly struct WcMapWall
    {
        public ulong Chunk { get; }
        public ulong Placement { get; }
        public string KindId { get; }
        public string LayerName { get; }
        // Relative to the single chunk's (Min.X, FlatHeight, Min.Z) origin.
        public SimVec3 LocalCenter { get; }
        public SimFix HalfX { get; }
        public SimFix Height { get; }
        public SimFix HalfZ { get; }
        public StableEntityKey Key => StableEntityKey.Placed(Chunk, Placement);

        public WcMapWall(ulong chunk, ulong placement, string kindId, string layerName,
            SimVec3 localCenter, SimFix halfX, SimFix height, SimFix halfZ)
        {
            Chunk = chunk; Placement = placement; KindId = kindId; LayerName = layerName;
            LocalCenter = localCenter; HalfX = halfX; Height = height; HalfZ = halfZ;
        }
    }

    // Immutable input/output model. Reader and writer both enforce the M2 profile.
    public sealed class WcMap
    {
        public string MapId { get; }
        public string ContentId { get; }
        public SimVec3 Min { get; }
        public SimVec3 Max { get; }
        public SimFix ChunkSize { get; }
        public SimFix FlatHeight { get; }
        public uint Width { get; }
        public uint Depth { get; }
        public IReadOnlyList<WcMapLayer> Layers { get; }
        public IReadOnlyList<WcMapWall> Walls { get; }
        public IReadOnlyList<byte> Passability { get; }
        public SimFix CellSize => SimFix.FromInt(1);
        public byte SurfaceId => 0;
        public byte Cost => 1;
        public byte NavigationLayer => 0;
        // Optional terrain. Height points are (Width+1) x (Depth+1) raw Y values, row-major (Z rows, then X).
        // The surface table is ordinal-ascending names; SurfaceCells holds one table index per navigation cell.
        public bool HasHeightField => heightPoints != null;
        public bool HasSurfaceTable => surfaceNames != null;
        public bool HasTerrain => HasHeightField || HasSurfaceTable;
        public IReadOnlyList<int> HeightPoints => heightPoints ?? (IReadOnlyList<int>)Array.Empty<int>();
        public IReadOnlyList<string> SurfaceNames => surfaceNames ?? (IReadOnlyList<string>)Array.Empty<string>();
        public IReadOnlyList<byte> SurfaceCells => surfaceCells ?? (IReadOnlyList<byte>)Array.Empty<byte>();
        private readonly ReadOnlyCollection<int> heightPoints;
        private readonly ReadOnlyCollection<string> surfaceNames;
        private readonly ReadOnlyCollection<byte> surfaceCells;

        public WcMap(string mapId, string contentId, SimVec3 min, SimVec3 max, SimFix chunkSize,
            SimFix flatHeight, uint width, uint depth, WcMapLayer[] layers, WcMapWall[] walls, byte[] passability)
            : this(mapId, contentId, min, max, chunkSize, flatHeight, width, depth, layers, walls, passability, null, null, null)
        {
        }

        // Terrain constructor: heightPoints null = flat; surfaceNames and surfaceCells both null = no surface table.
        public WcMap(string mapId, string contentId, SimVec3 min, SimVec3 max, SimFix chunkSize,
            SimFix flatHeight, uint width, uint depth, WcMapLayer[] layers, WcMapWall[] walls, byte[] passability,
            int[] heightPoints, string[] surfaceNames, byte[] surfaceCells)
        {
            if ((surfaceNames == null) != (surfaceCells == null)) throw new ArgumentException("Surface names and cells go together.");
            if (heightPoints != null) this.heightPoints = new ReadOnlyCollection<int>((int[])heightPoints.Clone());
            if (surfaceNames != null)
            {
                this.surfaceNames = new ReadOnlyCollection<string>((string[])surfaceNames.Clone());
                this.surfaceCells = new ReadOnlyCollection<byte>((byte[])surfaceCells.Clone());
            }
            MapId = mapId; ContentId = contentId; Min = min; Max = max; ChunkSize = chunkSize;
            FlatHeight = flatHeight; Width = width; Depth = depth;
            Layers = new ReadOnlyCollection<WcMapLayer>((WcMapLayer[])(layers ?? throw new ArgumentNullException(nameof(layers))).Clone());
            Walls = new ReadOnlyCollection<WcMapWall>((WcMapWall[])(walls ?? throw new ArgumentNullException(nameof(walls))).Clone());
            Passability = new ReadOnlyCollection<byte>((byte[])(passability ?? throw new ArgumentNullException(nameof(passability))).Clone());
        }

        public uint LayerMask(string name)
        {
            foreach (WcMapLayer layer in Layers)
                if (string.Equals(layer.Name, name, StringComparison.Ordinal)) return layer.Mask;
            throw new ArgumentException("Unknown collision layer.", nameof(name));
        }

        // Raw Y of height point (i, j), i in [0, Width], j in [0, Depth]; only on maps with a height field.
        public int VertexRaw(int i, int j)
        {
            if (heightPoints == null) throw new InvalidOperationException("Map has no height field.");
            if (i < 0 || j < 0 || i > Width || j > Depth) throw new ArgumentOutOfRangeException(nameof(i));
            return heightPoints[j * ((int)Width + 1) + i];
        }

        public bool IsPassable(int x, int z)
        {
            if (x < 0 || z < 0 || x >= Width || z >= Depth) throw new ArgumentOutOfRangeException(nameof(x));
            int index = checked(z * (int)Width + x);
            return (Passability[index >> 3] & (1 << (index & 7))) != 0;
        }

        public bool TryCell(SimFix worldX, SimFix worldZ, out int x, out int z)
        {
            // Compare before subtraction, including adversarial raw values.
            x = z = -1;
            if (worldX < Min.X || worldX >= Max.X || worldZ < Min.Z || worldZ >= Max.Z) return false;
            x = (int)((worldX.Raw - Min.X.Raw) / SimFix.Scale);
            z = (int)((worldZ.Raw - Min.Z.Raw) / SimFix.Scale);
            return true;
        }
    }
}
