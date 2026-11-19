using System;
using System.Collections.Generic;
using WarCore.Sim;

namespace WarCore.Content
{
    // A plate as the shared rules see it: whole cells [X, X + W) x [Z, Z + D) of the map, top heights local (raw Y above
    // the map's floor), and the solid cells left after cutouts (row-major inside the plate).
    internal sealed class PlateGeom
    {
        internal readonly ulong Id;
        internal readonly int X, Z, W, D;
        internal readonly PlateAxis Axis;
        internal readonly long From, To, Thick;
        internal readonly bool[] Solid;

        internal PlateGeom(ulong id, int x, int z, int w, int d, PlateAxis axis, long from, long to, long thick, bool[] solid)
        {
            Id = id; X = x; Z = z; W = w; D = d; Axis = axis; From = from; To = to; Thick = thick; Solid = solid;
        }

        internal long Top(int i, int j) => PlateSpan.TopPoint(Axis, From, To, W, D, i, j);

        // True when map cell (x, z) is a solid cell of this plate.
        internal bool SolidAt(int x, int z) => x >= X && z >= Z && x < X + W && z < Z + D && Solid[(z - Z) * W + (x - X)];

        // Lowest and highest top corner over map cell (x, z) of the plate.
        internal void CellTop(int x, int z, out long min, out long max)
        {
            int i = x - X, j = z - Z;
            long a = Top(i, j), b = Top(i + 1, j), c = Top(i, j + 1), e = Top(i + 1, j + 1);
            min = Math.Min(Math.Min(a, b), Math.Min(c, e));
            max = Math.Max(Math.Max(a, b), Math.Max(c, e));
        }
    }

    // A wall as the shared rules see it: footprint cells [X0, X1) x [Z0, Z1), vertical range [Bottom, Top], local raw Y.
    internal readonly struct WallGeom
    {
        internal readonly ulong Id;
        internal readonly int X0, Z0, X1, Z1;
        internal readonly long Bottom, Top;
        internal WallGeom(ulong id, int x0, int z0, int x1, int z1, long bottom, long top)
        {
            Id = id; X0 = x0; Z0 = z0; X1 = x1; Z1 = z1; Bottom = bottom; Top = top;
        }
    }

    // One rule set for floor plates, used by the bake, the .wcmap validation and the editor gate. Notation for a solid cell
    // c of plate p: topMin, topMax = lowest, highest top corner; bottomMin = topMin - thickness; the plate box over c is
    // [bottomMin, topMax]. The walk band is WallSpan's.
    //   1. Ground headroom (soft): ground cell c is not walkable when a plate solid there has WallSpan.BlocksWalk(bottomMin,
    //      topMax, gmin(c), gmax(c)).
    //   2. Walkable on a plate: a solid cell is walkable unless a wall covering it has BlocksWalk(bottom, top, topMin, topMax).
    //      Other plates are not looked at: rule 4 keeps them out of the band.
    //   3. Buried (hard): every top corner of every solid cell is at or above the ground height point at the same place.
    //   4. Stack (hard): two plates solid in the same cell keep bottomMin(upper) - topMax(lower) >= ClearanceRaw. Per cell the
    //      plates are sorted by (topMax, id) and only neighbours are checked; at most MaxPerCell plates per cell.
    //   5. Wall through a plate (hard): a wall over a solid cell must not meet the open box (bottomMin, topMax): legal when
    //      top <= bottomMin or bottom >= topMax. Cutout cells are not checked.
    // "First cell" always means the first in z-then-x order.
    internal static class PlateSpan
    {
        internal const int MaxPlates = 4096;
        internal const int MaxPerCell = 64;

        // Floor division toward negative infinity.
        internal static long FloorDiv(long a, long b)
        {
            long q = a / b;
            return (a % b != 0 && (a < 0) != (b < 0)) ? q - 1 : q;
        }

        // Top of plate point (i, j): a flat plate is From everywhere; a sloped one is From + floor((To - From) * k / L) on the
        // k-th grid line of its axis (L = its side along the axis), the same across the other axis.
        internal static long TopPoint(PlateAxis axis, long from, long to, int width, int depth, int i, int j)
        {
            if (axis == PlateAxis.X) return from + FloorDiv((to - from) * i, width);
            if (axis == PlateAxis.Z) return from + FloorDiv((to - from) * j, depth);
            return from;
        }

        // Rule 1: map cells whose ground walk band a plate box overlaps.
        internal static bool[] GroundBlocks(int width, int depth, Func<int, int, long> ground, IReadOnlyList<PlateGeom> plates)
        {
            var blocked = new bool[width * depth];
            foreach (PlateGeom p in plates)
                for (int z = p.Z; z < p.Z + p.D; z++)
                    for (int x = p.X; x < p.X + p.W; x++)
                    {
                        if (!p.SolidAt(x, z)) continue;
                        p.CellTop(x, z, out long topMin, out long topMax);
                        WallSpan.CellGround(ground, x, z, out long low, out long high);
                        if (WallSpan.BlocksWalk(topMin - p.Thick, topMax, low, high)) blocked[z * width + x] = true;
                    }
            return blocked;
        }

        // Rule 2: the walkable cells of one plate (row-major inside the plate).
        internal static bool[] Walkable(PlateGeom p, IReadOnlyList<WallGeom> walls)
        {
            var walk = (bool[])p.Solid.Clone();
            foreach (WallGeom w in walls)
            {
                int x0 = Math.Max(w.X0, p.X), x1 = Math.Min(w.X1, p.X + p.W), z0 = Math.Max(w.Z0, p.Z), z1 = Math.Min(w.Z1, p.Z + p.D);
                for (int z = z0; z < z1; z++)
                    for (int x = x0; x < x1; x++)
                    {
                        int index = (z - p.Z) * p.W + (x - p.X);
                        if (!walk[index]) continue;
                        p.CellTop(x, z, out long topMin, out long topMax);
                        if (WallSpan.BlocksWalk(w.Bottom, w.Top, topMin, topMax)) walk[index] = false;
                    }
            }
            return walk;
        }

        // Rule 3: false with the first buried cell.
        internal static bool AboveGround(PlateGeom p, Func<int, int, long> ground, out int cellX, out int cellZ)
        {
            for (int z = p.Z; z < p.Z + p.D; z++)
                for (int x = p.X; x < p.X + p.W; x++)
                {
                    if (!p.SolidAt(x, z)) continue;
                    int i = x - p.X, j = z - p.Z;
                    if (p.Top(i, j) < ground(x, z) || p.Top(i + 1, j) < ground(x + 1, z)
                        || p.Top(i, j + 1) < ground(x, z + 1) || p.Top(i + 1, j + 1) < ground(x + 1, z + 1))
                    {
                        cellX = x; cellZ = z;
                        return false;
                    }
                }
            cellX = cellZ = -1;
            return true;
        }

        internal readonly struct Conflict
        {
            internal readonly int A, B, CellX, CellZ; // indices into the plate list (A < B by id) or wall, plate
            internal Conflict(int a, int b, int cellX, int cellZ) { A = a; B = b; CellX = cellX; CellZ = cellZ; }
        }

        // Rule 4: every conflicting pair once, at its first conflicting cell, in the order found (cells in z-then-x order);
        // crowded is the first cell with more than MaxPerCell solid plates, or (-1, -1).
        internal static List<Conflict> Stacks(int width, int depth, IReadOnlyList<PlateGeom> plates, out int crowdedX, out int crowdedZ)
        {
            var cells = new List<int>[width * depth];
            for (int n = 0; n < plates.Count; n++)
            {
                PlateGeom p = plates[n];
                for (int z = p.Z; z < p.Z + p.D; z++)
                    for (int x = p.X; x < p.X + p.W; x++)
                        if (p.SolidAt(x, z)) (cells[z * width + x] ??= new List<int>()).Add(n);
            }
            var found = new List<Conflict>();
            var seen = new HashSet<(int, int)>();
            crowdedX = crowdedZ = -1;
            var tops = new List<(long Top, ulong Id, int Index, long BottomMin)>();
            for (int z = 0; z < depth; z++)
                for (int x = 0; x < width; x++)
                {
                    List<int> here = cells[z * width + x];
                    if (here == null || here.Count < 2) continue;
                    if (here.Count > MaxPerCell && crowdedX < 0) { crowdedX = x; crowdedZ = z; }
                    tops.Clear();
                    foreach (int n in here)
                    {
                        plates[n].CellTop(x, z, out long topMin, out long topMax);
                        tops.Add((topMax, plates[n].Id, n, topMin - plates[n].Thick));
                    }
                    tops.Sort((a, b) => a.Top != b.Top ? a.Top.CompareTo(b.Top) : a.Id.CompareTo(b.Id));
                    for (int k = 0; k + 1 < tops.Count; k++)
                    {
                        if (tops[k + 1].BottomMin - tops[k].Top >= WallSpan.ClearanceRaw) continue;
                        int a = tops[k].Index, b = tops[k + 1].Index;
                        if (plates[a].Id > plates[b].Id) (a, b) = (b, a);
                        if (seen.Add((a, b))) found.Add(new Conflict(a, b, x, z));
                    }
                }
            return found;
        }

        // Rule 5: every (wall, plate) pair once, at the first conflicting cell of the wall's footprint; walls in list order,
        // then plates in list order.
        internal static List<Conflict> WallsThrough(IReadOnlyList<WallGeom> walls, IReadOnlyList<PlateGeom> plates)
        {
            var found = new List<Conflict>();
            for (int wi = 0; wi < walls.Count; wi++)
            {
                WallGeom w = walls[wi];
                for (int pi = 0; pi < plates.Count; pi++)
                {
                    PlateGeom p = plates[pi];
                    int x0 = Math.Max(w.X0, p.X), x1 = Math.Min(w.X1, p.X + p.W), z0 = Math.Max(w.Z0, p.Z), z1 = Math.Min(w.Z1, p.Z + p.D);
                    bool hit = false;
                    for (int z = z0; z < z1 && !hit; z++)
                        for (int x = x0; x < x1 && !hit; x++)
                        {
                            if (!p.SolidAt(x, z)) continue;
                            p.CellTop(x, z, out long topMin, out long topMax);
                            if (w.Top > topMin - p.Thick && w.Bottom < topMax) { found.Add(new Conflict(wi, pi, x, z)); hit = true; }
                        }
                }
            }
            return found;
        }

        // Rule 6 helper: true with the first cell (every cell, solid or not) of the plate's top steeper than num/den.
        internal static bool Steep(HeightField top, PlateGeom p, long num, long den, out int cellX, out int cellZ)
        {
            for (int j = 0; j < p.D; j++)
                for (int i = 0; i < p.W; i++)
                    if (!top.CellWithinSlope(i, j, num, den)) { cellX = p.X + i; cellZ = p.Z + j; return true; }
            cellX = cellZ = -1;
            return false;
        }

        internal static bool[] Bits(IReadOnlyList<byte> mask, int count)
        {
            var bits = new bool[count];
            for (int i = 0; i < count; i++) bits[i] = (mask[i >> 3] & (1 << (i & 7))) != 0;
            return bits;
        }

        internal static byte[] Pack(bool[] bits)
        {
            var bytes = new byte[(bits.Length + 7) / 8];
            for (int i = 0; i < bits.Length; i++)
                if (bits[i]) bytes[i >> 3] |= (byte)(1 << (i & 7));
            return bytes;
        }
    }
}
