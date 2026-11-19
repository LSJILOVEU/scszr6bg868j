using System;
using System.Collections.Generic;

namespace WarCore.Sim
{
    // One floor plate as the crossing test sees it: a whole-cell rectangle on the map, its top height points (absolute raw,
    // (Width+1) x (Depth+1), Z rows then X) and thickness, and which of its cells are solid (cutouts already removed).
    public sealed class PlateSlab
    {
        public ulong Id { get; }
        public int X { get; }
        public int Z { get; }
        public int Width { get; }
        public int Depth { get; }
        public long ThickRaw { get; }
        internal readonly int[] Points;
        internal readonly bool[] Solid;

        public PlateSlab(ulong id, int x, int z, int width, int depth, int[] topPointsRaw, long thickRaw, bool[] solid)
        {
            if (topPointsRaw == null) throw new ArgumentNullException(nameof(topPointsRaw));
            if (solid == null) throw new ArgumentNullException(nameof(solid));
            if (width < 1 || depth < 1 || width > HeightField.MaxSide || depth > HeightField.MaxSide)
                throw new ArgumentOutOfRangeException(nameof(width));
            if (topPointsRaw.Length != (width + 1) * (depth + 1)) throw new ArgumentException("Point count differs from the plate.", nameof(topPointsRaw));
            if (solid.Length != width * depth) throw new ArgumentException("Solid count differs from the plate.", nameof(solid));
            Id = id; X = x; Z = z; Width = width; Depth = depth; ThickRaw = thickRaw;
            Points = (int[])topPointsRaw.Clone();
            Solid = (bool[])solid.Clone();
        }

        public long TopRaw(int i, int j) => Points[j * (Width + 1) + i];
        public bool IsSolid(int localX, int localZ) => Solid[localZ * Width + localX];
    }

    // Where a segment first enters a plate: At is the exact point P(t*) floored per coordinate (toward negative infinity);
    // t* = TNum / TDen (TDen > 0, 0 <= TNum <= TDen, not reduced: compare by cross multiplication). Nothing hit: At = default,
    // PlateId = 0, TNum = 0, TDen = 1.
    public readonly struct PlateHit
    {
        public SimVec3 At { get; }
        public ulong PlateId { get; }
        public long TNum { get; }
        public long TDen { get; }
        internal PlateHit(SimVec3 at, ulong plateId, long tNum, long tDen) { At = at; PlateId = plateId; TNum = tNum; TDen = tDen; }
        internal static PlateHit None => new PlateHit(default, 0, 0, 1);
    }

    // ML-5: does a 3D segment pass through the body of a floor plate? Exact integer geometry, no sampling, no allocation per
    // query.
    //
    // A point Q is blocked by plate p when its XZ lies in the half-open square [X0 + xS, X0 + (x+1)S) x [Z0 + zS, Z0 + (z+1)S)
    // of a solid cell (x, z) of p (the minimum edge belongs to the cell, as in NavGrid.TryCell and HeightField.TryHeightAt), and
    // top(Q) - thick < Q.y < top(Q), both strict (touching is clear). top is the continuous plane through the cell's four top
    // points (they are coplanar, checked here), not the floored HeightField sample (K11): the two differ by less than 1 raw.
    // The segment from -> to is blocked when some P(t) = from + t (to - from), t in [0, 1], is blocked; t* is the infimum of
    // the blocked t (it need not be attained: entering through the top face, the face itself is clear); ties go to the
    // smaller plate id.
    //
    // Widths (raw units, S = 2^16). The constructor keeps the map inside +-2^30 (HeightField's origin rule), heights below
    // 2^29, thickness below 2^29 and every cell no steeper than 2/1 (|a|, |b| <= 2S, a and b the cell's two height steps).
    // Query coordinates are within +-2^32 (checked first), so each displacement is <= 2^33 and a coordinate's distance to a
    // cell corner is <= 2^32 + 2^30 < 2^32.33. In a cell with corner (x0, z0), S f(t) = C + K t where f = top - y:
    //   C = S h00 + a (fx - x0) + b (fz - z0) - S fy: |C| <= 2^45 + 2 * 2^17 * 2^32.33 + 2^48 < 2^51,
    //   K = a dx + b dz - S dy: |K| <= 2 * 2^17 * 2^33 + 2^49 < 2^52, and S thick < 2^45; all in a long.
    // The open solution interval of 0 < C + K t < S thick has ends num / |K| with |num| < 2^52. Cell crossings are
    // t = n / d with 0 <= n <= 2^34 and 0 < d <= 2^33. Every comparison cross-multiplies two such fractions: at most
    // 2^52 * 2^52 = 2^104, as FixedInt128.Product of two longs. The reported point is from + floor(N delta / D) with
    // 0 <= N <= D < 2^52 and |delta| <= 2^33: the product is < 2^85 and the quotient <= 2^33.
    public sealed partial class PlateField
    {
        public const int MaxPlates = 4096, MaxPerCell = 64;
        private const long S = SimFix.Scale, LineBound = 1L << 32;

        public int Width { get; }
        public int Depth { get; }
        public SimFix OriginX { get; }
        public SimFix OriginZ { get; }
        public int Count => slabs.Length;

        private readonly PlateSlab[] slabs;
        private readonly int[] cellStart; // cell key -> first index in cellSlabs; cellStart[count] = cellSlabs.Length
        private readonly int[] cellSlabs; // slab indices solid in the cell, ascending id

        public PlateField(int width, int depth, SimFix originX, SimFix originZ, IReadOnlyList<PlateSlab> plates)
        {
            if (plates == null) throw new ArgumentNullException(nameof(plates));
            if (width < 1 || width > HeightField.MaxSide || depth < 1 || depth > HeightField.MaxSide) throw new ArgumentOutOfRangeException(nameof(width));
            long limit = SimVec3.WorldLimitMetres * S;
            if (originX.Raw % S != 0 || originZ.Raw % S != 0 || originX.Raw < -limit || originZ.Raw < -limit
                || originX.Raw > limit - width * S || originZ.Raw > limit - depth * S)
                throw new ArgumentOutOfRangeException(nameof(originX));
            if (plates.Count > MaxPlates) throw new ArgumentException("More than " + MaxPlates + " plates.", nameof(plates));
            Width = width; Depth = depth; OriginX = originX; OriginZ = originZ;
            slabs = new PlateSlab[plates.Count];
            var perCell = new int[width * depth];
            for (int k = 0; k < slabs.Length; k++)
            {
                PlateSlab p = plates[k] ?? throw new ArgumentNullException(nameof(plates));
                if (p.Id == 0 || (k > 0 && p.Id <= slabs[k - 1].Id)) throw new ArgumentException("Plate ids must rise from 1.", nameof(plates));
                if (p.X < 0 || p.Z < 0 || p.X + p.Width > width || p.Z + p.Depth > depth) throw new ArgumentException("Plate " + p.Id + " leaves the map.", nameof(plates));
                if (p.ThickRaw < S / 2 || p.ThickRaw >= HeightField.HeightLimitRaw) throw new ArgumentException("Plate " + p.Id + " thickness.", nameof(plates));
                foreach (int h in p.Points)
                    if (h <= -HeightField.HeightLimitRaw || h >= HeightField.HeightLimitRaw) throw new ArgumentException("Plate " + p.Id + " height.", nameof(plates));
                for (int j = 0; j < p.Depth; j++)
                    for (int i = 0; i < p.Width; i++)
                    {
                        long h00 = p.TopRaw(i, j), h10 = p.TopRaw(i + 1, j), h01 = p.TopRaw(i, j + 1), h11 = p.TopRaw(i + 1, j + 1);
                        if (h00 + h11 != h10 + h01) throw new ArgumentException("Plate " + p.Id + " cell is not flat.", nameof(plates));
                        if (Steeper(h10 - h00) || Steeper(h01 - h00)) throw new ArgumentException("Plate " + p.Id + " is steeper than 2/1.", nameof(plates));
                        if (p.IsSolid(i, j) && ++perCell[(p.Z + j) * width + p.X + i] > MaxPerCell)
                            throw new ArgumentException("More than " + MaxPerCell + " plates in one cell.", nameof(plates));
                    }
                slabs[k] = p;
            }
            cellStart = new int[width * depth + 1];
            for (int c = 0; c < perCell.Length; c++) cellStart[c + 1] = cellStart[c] + perCell[c];
            cellSlabs = new int[cellStart[perCell.Length]];
            var fill = (int[])cellStart.Clone();
            for (int k = 0; k < slabs.Length; k++)
            {
                PlateSlab p = slabs[k];
                for (int j = 0; j < p.Depth; j++)
                    for (int i = 0; i < p.Width; i++)
                        if (p.IsSolid(i, j)) cellSlabs[fill[(p.Z + j) * width + p.X + i]++] = k;
            }
        }

        public bool Blocks(SimVec3 from, SimVec3 to) => TryFirstThrough(from, to, out _);

        public bool TryFirstThrough(SimVec3 from, SimVec3 to, out PlateHit hit)
        {
            if (Outside(from.X.Raw) || Outside(from.Y.Raw) || Outside(from.Z.Raw)
                || Outside(to.X.Raw) || Outside(to.Y.Raw) || Outside(to.Z.Raw)) throw new ArgumentOutOfRangeException(nameof(to));
            var q = new Query(from.X.Raw, from.Y.Raw, from.Z.Raw, to.X.Raw - from.X.Raw, to.Y.Raw - from.Y.Raw, to.Z.Raw - from.Z.Raw);
            hit = PlateHit.None;
            if (slabs.Length == 0) return false;
            Walk(ref q);
            if (!q.Found) return false;
            hit = new PlateHit(new SimVec3(SimFix.FromRaw(q.Fx + FloorMulDiv(q.BestN, q.Dx, q.BestD)), SimFix.FromRaw(q.Fy + FloorMulDiv(q.BestN, q.Dy, q.BestD)),
                SimFix.FromRaw(q.Fz + FloorMulDiv(q.BestN, q.Dz, q.BestD))), slabs[q.BestSlab].Id, q.BestN, q.BestD);
            return true;
        }

        private static bool Outside(long c) => c > LineBound || c < -LineBound;
        private static bool Steeper(long step) => step > 2 * S || step < -2 * S;

        private struct Query
        {
            internal readonly long Fx, Fy, Fz, Dx, Dy, Dz;
            internal bool Found;
            internal long BestN, BestD;
            internal int BestSlab;
            internal Query(long fx, long fy, long fz, long dx, long dy, long dz)
            { Fx = fx; Fy = fy; Fz = fz; Dx = dx; Dy = dy; Dz = dz; Found = false; BestN = 0; BestD = 1; BestSlab = -1; }
        }
    }
}
