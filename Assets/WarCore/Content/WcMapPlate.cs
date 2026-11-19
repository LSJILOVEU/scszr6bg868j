using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using WarCore.Sim;

namespace WarCore.Content
{
    public enum PlateAxis : byte { Flat = 0, X = 1, Z = 2 }

    // One floor plate of a .wcmap (version 3, section 12): a whole-cell rectangle whose top is a small height table with its
    // origin at the plate's minimum corner. A flat plate has the same top everywhere; a sloped plate rises along one axis
    // from FromRaw on the minimum edge to ToRaw on the maximum edge, so the four corners of every cell are coplanar. Top
    // heights are absolute raw Y, as the height points of section 3; the bottom is the top minus ThickRaw. Solid holds the
    // cells left after cutouts, Walkable the solid cells no wall blocks (both row-major inside the plate, 1 = set).
    public sealed class WcMapPlate
    {
        public const byte NoSurface = 255;
        public const long MinThickRaw = SimFix.Scale / 2;

        public ulong Id { get; }
        public int X { get; }
        public int Z { get; }
        public int Width { get; }
        public int Depth { get; }
        public PlateAxis Axis { get; }
        public int FromRaw { get; }
        public int ToRaw { get; }
        public int ThickRaw { get; }
        public byte Surface { get; }
        public IReadOnlyList<byte> Solid { get; }
        public IReadOnlyList<byte> Walkable { get; }

        public WcMapPlate(ulong id, int x, int z, int width, int depth, PlateAxis axis, int fromRaw, int toRaw, int thickRaw,
            byte surface, byte[] solid, byte[] walkable)
        {
            Id = id; X = x; Z = z; Width = width; Depth = depth; Axis = axis; FromRaw = fromRaw; ToRaw = toRaw;
            ThickRaw = thickRaw; Surface = surface;
            Solid = new ReadOnlyCollection<byte>((byte[])(solid ?? throw new ArgumentNullException(nameof(solid))).Clone());
            Walkable = new ReadOnlyCollection<byte>((byte[])(walkable ?? throw new ArgumentNullException(nameof(walkable))).Clone());
        }

        public int CellCount => Width * Depth;
        public int MaskBytes => (Width * Depth + 7) / 8;

        public bool IsSolid(int localX, int localZ) => Bit(Solid, localX, localZ);
        public bool IsWalkable(int localX, int localZ) => Bit(Walkable, localX, localZ);

        private bool Bit(IReadOnlyList<byte> mask, int localX, int localZ)
        {
            if (localX < 0 || localZ < 0 || localX >= Width || localZ >= Depth) throw new ArgumentOutOfRangeException(nameof(localX));
            int index = localZ * Width + localX;
            return (mask[index >> 3] & (1 << (index & 7))) != 0;
        }

        // Absolute raw top of plate point (i, j), i in [0, Width], j in [0, Depth].
        public long TopRaw(int i, int j) => PlateSpan.TopPoint(Axis, FromRaw, ToRaw, Width, Depth, i, j);

        // The plate's top as a height field placed on the map (worldMinX, worldMinZ are the map's Min.X and Min.Z), so
        // sampling and slope checks are the ground's own (HeightField). Only for a plate whose fields passed validation.
        public HeightField TopField(SimFix worldMinX, SimFix worldMinZ)
        {
            var points = new int[(Width + 1) * (Depth + 1)];
            for (int j = 0; j <= Depth; j++)
                for (int i = 0; i <= Width; i++) points[j * (Width + 1) + i] = (int)TopRaw(i, j);
            return new HeightField(Width, Depth, SimFix.FromRaw(worldMinX.Raw + X * SimFix.Scale),
                SimFix.FromRaw(worldMinZ.Raw + Z * SimFix.Scale), points);
        }
    }
}
