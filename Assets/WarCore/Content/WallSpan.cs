using System;
using WarCore.Sim;

namespace WarCore.Content
{
    // Does a wall with a raised bottom block walking in one cell? One rule for the bake, the .wcmap validation and the
    // editor gate. The walk band of a cell is [lowest corner - BelowRaw, highest corner + ClearanceRaw): the bottom is the
    // mover's wall-probe sphere (radius 0.25 m around the foot point), inclusive, so a touching sphere counts as a hit; the
    // top is the headroom, exclusive, so a lintel whose underside is exactly ClearanceRaw above the highest corner leaves
    // the cell walkable. A wall blocks the cell when its vertical range [bottom, top] overlaps the band.
    // Walls without a raised bottom do not use this rule: they block their whole footprint, as before.
    // All heights share one origin (absolute raw Y in a description, raw Y above Min.Y in a .wcmap).
    public static class WallSpan
    {
        public const long ClearanceRaw = 2 * SimFix.Scale;
        // ClearanceRaw in metres, for the picture's layer cut (which may not name the simulation).
        public static readonly float ClearanceMetres = (float)ClearanceRaw / SimFix.Scale;
        public const long BelowRaw = SimFix.Scale / 4;

        public static bool BlocksWalk(long bottomRaw, long topRaw, long groundMinRaw, long groundMaxRaw) =>
            topRaw >= groundMinRaw - BelowRaw && bottomRaw < groundMaxRaw + ClearanceRaw;

        // Lowest and highest of the four corner points of cell (x, z); point(i, j) gives a height point.
        public static void CellGround(Func<int, int, long> point, int x, int z, out long minRaw, out long maxRaw)
        {
            long a = point(x, z), b = point(x + 1, z), c = point(x, z + 1), d = point(x + 1, z + 1);
            minRaw = Math.Min(Math.Min(a, b), Math.Min(c, d));
            maxRaw = Math.Max(Math.Max(a, b), Math.Max(c, d));
        }

        // Highest height point under a footprint of whole cells [x0, x1) x [z0, z1), both far edges included.
        public static long HighestPoint(Func<int, int, long> point, int x0, int z0, int x1, int z1)
        {
            long highest = long.MinValue;
            for (int iz = z0; iz <= z1; iz++)
                for (int ix = x0; ix <= x1; ix++) highest = Math.Max(highest, point(ix, iz));
            return highest;
        }
    }
}
