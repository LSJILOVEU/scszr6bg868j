using System;

namespace WarCore.Presentation
{
    // ML-10: float geometry of floor plates for the picture (picking and fading), never for Sim. A line is o + t d. Cells are
    // the plate's own: (i, j) local, 1 m, the minimum corner at (MinX + i, MinZ + j).
    internal static class PlateRay
    {
        // The first t in [tMin, tMax] where the line meets the top of a solid cell of the plate from above, the cell's local
        // (i, j) and the point; false when it misses. take(i, j) filters cells (null takes every solid cell).
        internal static bool TryTop(ViewPlate p, double ox, double oy, double oz, double dx, double dy, double dz, double tMin,
            double tMax, out double t, out int ci, out int cj)
        {
            t = 0; ci = -1; cj = -1;
            if (!Clip(p, ox, oy, oz, dx, dy, dz, ref tMin, ref tMax)) return false;
            var walk = new Walk(p, ox, oz, dx, dz, tMin, tMax);
            while (walk.Next(out int i, out int j, out double a, out double b))
            {
                if (!p.IsSolid(i, j)) continue;
                double h00 = p.Top(i, j), sx = p.Top(i + 1, j) - h00, sz = p.Top(i, j + 1) - h00;
                double x0 = p.MinX + i, z0 = p.MinZ + j;
                double den = dy - sx * dx - sz * dz;
                if (!(den < 0)) continue; // parallel, or meeting the top from below
                double hit = (h00 + sx * (ox - x0) + sz * (oz - z0) - oy) / den;
                const double slack = 1e-9;
                if (hit >= a - slack && hit <= b + slack)
                {
                    t = Math.Max(hit, a); ci = i; cj = j;
                    return true;
                }
            }
            return false;
        }

        // True when the segment t in [0, 1] passes through the slab of a solid cell (between its lowest bottom and highest top).
        internal static bool Crosses(ViewPlate p, double ox, double oy, double oz, double dx, double dy, double dz)
        {
            double tMin = 0, tMax = 1;
            if (!Clip(p, ox, oy, oz, dx, dy, dz, ref tMin, ref tMax)) return false;
            var walk = new Walk(p, ox, oz, dx, dz, tMin, tMax);
            while (walk.Next(out int i, out int j, out double a, out double b))
            {
                if (!p.IsSolid(i, j)) continue;
                double top = Math.Max(Math.Max(p.Top(i, j), p.Top(i + 1, j)), Math.Max(p.Top(i, j + 1), p.Top(i + 1, j + 1)));
                double bottom = Math.Min(Math.Min(p.Top(i, j), p.Top(i + 1, j)), Math.Min(p.Top(i, j + 1), p.Top(i + 1, j + 1))) - p.Thick;
                double lo = a, hi = b;
                if (Slab(oy, dy, bottom, top, ref lo, ref hi)) return true;
            }
            return false;
        }

        // True when the segment t in [0, 1] passes through the box (centre and full size).
        internal static bool CrossesBox(double cx, double cy, double cz, double sx, double sy, double sz,
            double ox, double oy, double oz, double dx, double dy, double dz)
        {
            double lo = 0, hi = 1;
            return Slab(ox, dx, cx - sx / 2, cx + sx / 2, ref lo, ref hi) && Slab(oy, dy, cy - sy / 2, cy + sy / 2, ref lo, ref hi)
                && Slab(oz, dz, cz - sz / 2, cz + sz / 2, ref lo, ref hi);
        }

        // Narrow [lo, hi] to the t where o + t d lies in [min, max]; false when that leaves nothing.
        internal static bool Slab(double o, double d, double min, double max, ref double lo, ref double hi)
        {
            if (d == 0) return o >= min && o <= max && lo <= hi;
            double t0 = (min - o) / d, t1 = (max - o) / d;
            if (t0 > t1) { double s = t0; t0 = t1; t1 = s; }
            if (t0 > lo) lo = t0;
            if (t1 < hi) hi = t1;
            return lo <= hi;
        }

        // The plate's box: its rectangle, from its lowest bottom to its highest top.
        private static bool Clip(ViewPlate p, double ox, double oy, double oz, double dx, double dy, double dz, ref double lo, ref double hi)
        {
            double top = double.NegativeInfinity, bottom = double.PositiveInfinity;
            for (int j = 0; j <= p.Depth; j++)
                for (int i = 0; i <= p.Width; i++)
                {
                    top = Math.Max(top, p.Top(i, j));
                    bottom = Math.Min(bottom, p.Top(i, j));
                }
            return Slab(ox, dx, p.MinX, p.MinX + p.Width, ref lo, ref hi) && Slab(oz, dz, p.MinZ, p.MinZ + p.Depth, ref lo, ref hi)
                && Slab(oy, dy, bottom - p.Thick, top, ref lo, ref hi);
        }

        // The plate's cells the line crosses in order of t (2D grid walk), each with its [a, b] stretch of t.
        private struct Walk
        {
            private readonly ViewPlate p;
            private readonly double dx, dz, tEnd, deltaX, deltaZ;
            private readonly int stepX, stepZ;
            private int i, j;
            private double t, nextX, nextZ;
            private bool done;

            internal Walk(ViewPlate plate, double ox, double oz, double dx, double dz, double tStart, double tEnd)
            {
                p = plate; this.dx = dx; this.dz = dz; this.tEnd = tEnd; t = tStart; done = false;
                double u = ox + dx * tStart - plate.MinX, v = oz + dz * tStart - plate.MinZ;
                i = Math.Clamp((int)Math.Floor(u), 0, plate.Width - 1);
                j = Math.Clamp((int)Math.Floor(v), 0, plate.Depth - 1);
                stepX = dx > 0 ? 1 : dx < 0 ? -1 : 0;
                stepZ = dz > 0 ? 1 : dz < 0 ? -1 : 0;
                deltaX = dx != 0 ? 1 / Math.Abs(dx) : double.PositiveInfinity;
                deltaZ = dz != 0 ? 1 / Math.Abs(dz) : double.PositiveInfinity;
                nextX = dx > 0 ? (plate.MinX + i + 1 - ox) / dx : dx < 0 ? (plate.MinX + i - ox) / dx : double.PositiveInfinity;
                nextZ = dz > 0 ? (plate.MinZ + j + 1 - oz) / dz : dz < 0 ? (plate.MinZ + j - oz) / dz : double.PositiveInfinity;
            }

            internal bool Next(out int ci, out int cj, out double a, out double b)
            {
                ci = i; cj = j; a = t; b = Math.Min(Math.Min(nextX, nextZ), tEnd);
                if (done || i < 0 || j < 0 || i >= p.Width || j >= p.Depth || t > tEnd) return false;
                if (nextX < nextZ) { i += stepX; t = nextX; nextX += deltaX; }
                else { j += stepZ; t = nextZ; nextZ += deltaZ; }
                if (b >= tEnd) done = true;
                return true;
            }
        }
    }
}
