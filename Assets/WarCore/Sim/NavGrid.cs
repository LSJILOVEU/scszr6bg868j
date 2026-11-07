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
            if (width < 1 || width > HeightField.MaxSide || depth < 1 || depth > HeightField.MaxSide)
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

        // Is every point of the horizontal segment from -> to at least `radius` away from every blocked cell and from the map
        // edge? Touching is clear: a cell blocks only when its nearest point is strictly closer than `radius`. Exact integer
        // geometry, no sampling. Y is ignored. A start that is itself too close answers false (there is no escape rule).
        //
        // Domain: 0 < radius <= 16384 raw (0.25 m) and each axis of the displacement <= 32768 raw (0.5 m); anything else
        // throws ArgumentOutOfRangeException, before any subtraction that could wrap.
        //
        // Width of every product (raw units, coordinates relative to `from`): the displacement is <= 2^15 per axis, so
        // L2 = dx^2 + dz^2 <= 2^31. Once both ends are inside the map (checked first) a candidate cell corner is at most
        // 32768 + 16384 + 65536 = 114688 < 2^17 from `from`. Dot and cross products are <= 2 * 2^15 * 2^17 = 2^33, inside a long.
        // cross^2 can reach 2^66, so it is squared only when |cross| < 2^30 (then cross^2 < 2^60). For the other branch:
        // R^2 * L2 <= 2^28 * 2^31 = 2^59 < 2^60 <= cross^2, so that corner does not block. This skips only that one corner;
        // the query goes on with the remaining corners and edges. Interval bounds are compared as fractions n/d with
        // |n| <= 2^18 and d <= 2^15, so cross products of fractions are <= 2^33.
        public bool SegmentClear(SimVec3 from, SimVec3 to, SimFix radius)
        {
            long r = radius.Raw;
            if (r <= 0 || r > MaxClearRadiusRaw) throw new ArgumentOutOfRangeException(nameof(radius));
            long fx = from.X.Raw, fz = from.Z.Raw, tx = to.X.Raw, tz = to.Z.Raw;
            if (AbsDiff(tx, fx) > MaxClearStepRaw || AbsDiff(tz, fz) > MaxClearStepRaw)
                throw new ArgumentOutOfRangeException(nameof(to));
            // The map edge: closed interval [min + r, max - r] on each axis; the segment is convex, so its ends are enough.
            long minX = OriginX.Raw + r, maxX = OriginX.Raw + Width * SimFix.Scale - r;
            long minZ = OriginZ.Raw + r, maxZ = OriginZ.Raw + Depth * SimFix.Scale - r;
            if (fx < minX || fx > maxX || tx < minX || tx > maxX || fz < minZ || fz > maxZ || tz < minZ || tz > maxZ) return false;
            long dx = tx - fx, dz = tz - fz;
            // Everything is inside the map now, so relative coordinates are non-negative and `/` is floor division.
            int cx0 = (int)((Least(fx, tx) - r - OriginX.Raw) / SimFix.Scale);
            int cx1 = (int)((Greatest(fx, tx) + r - OriginX.Raw) / SimFix.Scale);
            int cz0 = (int)((Least(fz, tz) - r - OriginZ.Raw) / SimFix.Scale);
            int cz1 = (int)((Greatest(fz, tz) + r - OriginZ.Raw) / SimFix.Scale);
            if (cx0 < 0) cx0 = 0;
            if (cz0 < 0) cz0 = 0;
            if (cx1 >= Width) cx1 = Width - 1;
            if (cz1 >= Depth) cz1 = Depth - 1;
            for (int z = cz0; z <= cz1; z++)
                for (int x = cx0; x <= cx1; x++)
                {
                    if (IsPassable(x, z)) continue;
                    long x0 = OriginX.Raw + x * SimFix.Scale - fx, z0 = OriginZ.Raw + z * SimFix.Scale - fz;
                    long x1 = x0 + SimFix.Scale, z1 = z0 + SimFix.Scale;
                    if (MeetsBox(dx, dz, x0 - r, true, x1 + r, true, z0, false, z1, false)
                        || MeetsBox(dx, dz, x0, false, x1, false, z0 - r, true, z1 + r, true)
                        || NearCorner(dx, dz, x0, z0, r) || NearCorner(dx, dz, x1, z0, r)
                        || NearCorner(dx, dz, x0, z1, r) || NearCorner(dx, dz, x1, z1, r))
                        return false;
                }
            return true;
        }

        public const long MaxClearRadiusRaw = 16384, MaxClearStepRaw = 32768;

        private static long Least(long a, long b) => a < b ? a : b;
        private static long Greatest(long a, long b) => a > b ? a : b;

        private static long AbsDiff(long a, long b)
        {
            // The true difference of two longs can exceed a long; do it in ulong, where it cannot wrap.
            ulong d = a >= b ? (ulong)a - (ulong)b : (ulong)b - (ulong)a;
            return d > long.MaxValue ? long.MaxValue : (long)d;
        }

        // Does the segment (0,0) -> (dx,dz) come strictly closer than r to the corner (cx,cz)?
        private static bool NearCorner(long dx, long dz, long cx, long cz, long r)
        {
            long length2 = dx * dx + dz * dz;
            long dot = cx * dx + cz * dz;
            long r2 = r * r;
            if (length2 == 0 || dot <= 0) return cx * cx + cz * cz < r2;
            if (dot >= length2)
            {
                long ex = cx - dx, ez = cz - dz;
                return ex * ex + ez * ez < r2;
            }
            long cross = cx * dz - cz * dx;
            if (cross < 0) cross = -cross;
            if (cross >= 1L << 30) return false;
            return cross * cross < r2 * length2;
        }

        // Does the segment (0,0) -> (dx,dz) have a point inside the box whose X and Z ranges have the given ends (open or
        // closed)? Each axis restricts the parameter t in [0,1] to an interval; the segment meets the box when they overlap.
        private static bool MeetsBox(long dx, long dz, long xLo, bool xLoOpen, long xHi, bool xHiOpen,
            long zLo, bool zLoOpen, long zHi, bool zHiOpen)
        {
            long loN = 0, loD = 1, hiN = 1, hiD = 1;
            bool loOpen = false, hiOpen = false;
            if (!Clip(dx, xLo, xLoOpen, xHi, xHiOpen, ref loN, ref loD, ref loOpen, ref hiN, ref hiD, ref hiOpen)) return false;
            if (!Clip(dz, zLo, zLoOpen, zHi, zHiOpen, ref loN, ref loD, ref loOpen, ref hiN, ref hiD, ref hiOpen)) return false;
            long order = loN * hiD - hiN * loD; // sign of lo - hi
            return order < 0 || (order == 0 && !loOpen && !hiOpen);
        }

        private static bool Clip(long d, long lo, bool loOpen, long hi, bool hiOpen,
            ref long loN, ref long loD, ref bool curLoOpen, ref long hiN, ref long hiD, ref bool curHiOpen)
        {
            if (d == 0) return (loOpen ? 0 > lo : 0 >= lo) && (hiOpen ? 0 < hi : 0 <= hi);
            long tLoN, tHiN, den; bool tLoOpen, tHiOpen;
            if (d > 0) { tLoN = lo; tHiN = hi; den = d; tLoOpen = loOpen; tHiOpen = hiOpen; }
            else { tLoN = -hi; tHiN = -lo; den = -d; tLoOpen = hiOpen; tHiOpen = loOpen; }
            long c = tLoN * loD - loN * den; // compare new low with the current low
            if (c > 0) { loN = tLoN; loD = den; curLoOpen = tLoOpen; }
            else if (c == 0 && tLoOpen) curLoOpen = true;
            c = tHiN * hiD - hiN * den;
            if (c < 0) { hiN = tHiN; hiD = den; curHiOpen = tHiOpen; }
            else if (c == 0 && tHiOpen) curHiOpen = true;
            return true;
        }
    }
}
