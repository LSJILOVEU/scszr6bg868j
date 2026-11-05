using System;

namespace WarCore.Sim
{
    // Immutable one-metre cells. Bit one is passable; node keys increase along X, then Z.
    public sealed class NavGrid
    {
        private readonly byte[] bitmap;
        public int Width { get; }
        public int Depth { get; }
        public int Count => Width * Depth;
        public SimFix OriginX { get; }
        public SimFix OriginZ { get; }
        public ulong Digest { get; }

        public NavGrid(int width, int depth, SimFix originX, SimFix originZ, ReadOnlySpan<byte> passability)
        {
            if (width < 1 || width > 256 || depth < 1 || depth > 256)
                throw new ArgumentOutOfRangeException(nameof(width));
            long limit = SimVec3.WorldLimitMetres * SimFix.Scale;
            if (originX.Raw % SimFix.Scale != 0 || originZ.Raw % SimFix.Scale != 0
                || originX.Raw < -limit || originZ.Raw < -limit
                || originX.Raw > limit - width * SimFix.Scale || originZ.Raw > limit - depth * SimFix.Scale)
                throw new ArgumentOutOfRangeException(nameof(originX));
            if (passability.Length != (width * depth + 7) / 8)
                throw new ArgumentException("Bitmap length differs from the grid.", nameof(passability));
            int used = width * depth % 8;
            if (used != 0 && (passability[passability.Length - 1] >> used) != 0)
                throw new ArgumentException("Unused bitmap bits must be zero.", nameof(passability));
            Width = width; Depth = depth; OriginX = originX; OriginZ = originZ;
            bitmap = passability.ToArray();
            // FNV-1a over four little-endian 64-bit fields, then the bitmap bytes.
            var hash = new StateHash();
            hash.Add(width); hash.Add(depth); hash.Add(originX.Raw); hash.Add(originZ.Raw);
            foreach (byte value in bitmap) hash.Add(value);
            Digest = hash.Value;
        }

        public bool IsPassable(int x, int z)
        {
            if (x < 0 || z < 0 || x >= Width || z >= Depth) return false;
            int key = z * Width + x;
            return (bitmap[key >> 3] & (1 << (key & 7))) != 0;
        }

        public bool TryCell(SimVec3 point, out int key)
        {
            key = -1;
            // Reject before subtracting. Relative coordinates are then nonnegative, so division is floor.
            if (point.X.Raw < OriginX.Raw || point.X.Raw >= OriginX.Raw + Width * SimFix.Scale
                || point.Z.Raw < OriginZ.Raw || point.Z.Raw >= OriginZ.Raw + Depth * SimFix.Scale) return false;
            int x = (int)((point.X.Raw - OriginX.Raw) / SimFix.Scale);
            int z = (int)((point.Z.Raw - OriginZ.Raw) / SimFix.Scale);
            key = z * Width + x;
            return true;
        }

        public SimVec3 Center(int key, SimFix height)
        {
            if (key < 0 || key >= Count) throw new ArgumentOutOfRangeException(nameof(key));
            return new SimVec3(SimFix.FromRaw(OriginX.Raw + key % Width * SimFix.Scale + SimFix.Scale / 2),
                height, SimFix.FromRaw(OriginZ.Raw + key / Width * SimFix.Scale + SimFix.Scale / 2));
        }
    }
}
