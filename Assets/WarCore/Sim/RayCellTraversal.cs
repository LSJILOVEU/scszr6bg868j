using System;
using System.Diagnostics;

namespace WarCore.Sim
{
    internal readonly struct RayCellEvent
    {
        internal readonly long X;
        internal readonly long Y;
        internal readonly long Z;
        internal readonly ulong Numerator;
        internal readonly ulong Denominator;

        internal RayCellEvent(long x, long y, long z, ulong numerator, ulong denominator)
        {
            X = x;
            Y = y;
            Z = z;
            Numerator = numerator;
            Denominator = denominator;
        }
    }

    // Algorithm reference: Amanatides-Woo (1987), https://www.eecs.yorku.ca/~amana/research/grid.pdf.
    // Protocol reference: https://github.com/mmp/pbrt-v2/blob/master/src/accelerators/grid.cpp (BSD-2-Clause).
    // Independently written: exact fractions and closed-cell supercover replace floating-point stepping.
    // Scratch stays in this per-query value, so traversal itself does not change the index.
    internal struct RayCellTraversal
    {
        private struct Axis
        {
            internal long Cell;
            internal ulong Numerator;
            internal ulong Denominator;
            internal int Step;
            internal bool ParallelBoundary;
            internal bool OriginBoundary;

            internal Axis(long origin, long direction, long size, long range)
            {
                long remainder = origin % size;
                Cell = origin / size - (remainder < 0 ? 1 : 0);
                OriginBoundary = remainder == 0;
                ParallelBoundary = OriginBoundary && direction == 0;
                Step = direction > 0 ? 1 : direction < 0 ? -1 : 0;
                Numerator = 0;
                Denominator = (ulong)(direction < 0 ? -direction : direction);
                if (Step < 0 && OriginBoundary)
                    Cell--;
                if (Step == 0)
                    return;
                long towards = Step > 0 ? -remainder : remainder;
                // A face's physical distance is at least its axial displacement. Reject before adding
                // an arbitrary long-sized cell: range - towards is bounded by 2*WorldLimitRaw.
                if (towards <= 0 && size > range - towards)
                {
                    Denominator = 0;
                    return;
                }
                Numerator = (ulong)(towards > 0 ? towards : size + towards);
                if (Numerator > (ulong)range)
                    Denominator = 0;
            }

            internal void Advance(ulong size, ulong range)
            {
                Cell += Step;
                // Only live axes reach here, with Numerator <= range; no unchecked terminal step.
                if (size > range - Numerator)
                    Denominator = 0;
                else
                    Numerator += size;
            }
        }

        private Axis x;
        private Axis y;
        private Axis z;
        private readonly ulong size;
        private readonly ulong range;
        private readonly ulong rangeSquared;
        private readonly ulong directionSquared;
        private long lowX;
        private long lowY;
        private long lowZ;
        private long oldX;
        private long oldY;
        private long oldZ;
        private int varied;
        private int crossing;
        private int cursor;
        private ulong eventN;
        private ulong eventD;

        internal RayCellTraversal(in SpatialRequest request, long cellSize)
        {
            if (cellSize <= 0 || request.Kind != SpatialQueryKind.Ray)
                throw new ArgumentException("Ray traversal requires a positive cell size and a ray request.");
            AssertLimits(in request);
            x = new Axis(request.Origin.X.Raw, request.Direction.X.Raw, cellSize, request.Range.Raw);
            y = new Axis(request.Origin.Y.Raw, request.Direction.Y.Raw, cellSize, request.Range.Raw);
            z = new Axis(request.Origin.Z.Raw, request.Direction.Z.Raw, cellSize, request.Range.Raw);
            size = (ulong)cellSize;
            range = (ulong)request.Range.Raw;
            rangeSquared = range * range;
            directionSquared = Square(request.Direction.X.Raw) + Square(request.Direction.Y.Raw) + Square(request.Direction.Z.Raw);
            oldX = x.Cell;
            oldY = y.Cell;
            oldZ = z.Cell;
            // The forward interior cell differs from floor(origin) on a negative boundary crossing.
            lowX = x.Cell - (x.OriginBoundary && x.Step >= 0 ? 1 : 0);
            lowY = y.Cell - (y.OriginBoundary && y.Step >= 0 ? 1 : 0);
            lowZ = z.Cell - (z.OriginBoundary && z.Step >= 0 ? 1 : 0);
            varied = (x.OriginBoundary ? 4 : 0) | (y.OriginBoundary ? 2 : 0) | (z.OriginBoundary ? 1 : 0);
            crossing = 0;
            cursor = 0;
            eventN = 0;
            eventD = 1;
        }

        private static ulong Square(long value)
        {
            ulong magnitude = (ulong)(value < 0 ? -value : value);
            return magnitude * magnitude;
        }

        [Conditional("DEBUG")]
        private static void AssertLimits(in SpatialRequest request)
        {
            SpatialWidth.AssertWorldLimit(SimVec3.WorldLimitMetres);
            if (request.Range.Raw < 0 || request.Range.Raw > 1L << 30)
                throw new SimBoundaryException("DDA requires a validated range.");
        }

        internal bool MoveNext(out RayCellEvent cell)
        {
            do
            {
                while (cursor < 8)
                {
                    int choice = cursor++;
                    if ((choice & ~varied) != 0)
                        continue;
                    long cx = lowX + ((choice >> 2) & 1);
                    long cy = lowY + ((choice >> 1) & 1);
                    long cz = lowZ + (choice & 1);
                    // Prior interior cells were already emitted; parallel-face copies are all skipped too.
                    if (crossing != 0 && ((crossing & 4) == 0 || cx == oldX)
                        && ((crossing & 2) == 0 || cy == oldY) && ((crossing & 1) == 0 || cz == oldZ))
                        continue;
                    cell = new RayCellEvent(cx, cy, cz, eventN, eventD);
                    return true;
                }
            }
            while (NextEvent());
            cell = default;
            return false;
        }

        private static int Compare(ulong an, ulong ad, ulong bn, ulong bd)
            => FixedUInt128.Multiply(an, bd).CompareTo(FixedUInt128.Multiply(bn, ad));

        private static void Select(in Axis axis, ref ulong numerator, ref ulong denominator)
        {
            if (axis.Denominator != 0 && (denominator == 0 || Compare(axis.Numerator, axis.Denominator, numerator, denominator) < 0))
            {
                numerator = axis.Numerator;
                denominator = axis.Denominator;
            }
        }

        private static bool Tied(in Axis axis, ulong numerator, ulong denominator)
            => axis.Denominator != 0 && Compare(axis.Numerator, axis.Denominator, numerator, denominator) == 0;

        private bool NextEvent()
        {
            ulong numerator = 0;
            ulong denominator = 0;
            Select(in x, ref numerator, ref denominator);
            Select(in y, ref numerator, ref denominator);
            Select(in z, ref numerator, ref denominator);
            if (denominator == 0)
                return false;
            // n <= R <= 2^30, D^2 < 2^62: the left product uses at most 122 of 128 bits.
            if (FixedUInt128.Multiply(numerator * numerator, directionSquared)
                .CompareTo(FixedUInt128.Multiply(rangeSquared, denominator * denominator)) > 0)
                return false;
            bool tx = Tied(in x, numerator, denominator);
            bool ty = Tied(in y, numerator, denominator);
            bool tz = Tied(in z, numerator, denominator);
            oldX = x.Cell;
            oldY = y.Cell;
            oldZ = z.Cell;
            lowX = x.Cell - (x.ParallelBoundary || (tx && x.Step < 0) ? 1 : 0);
            lowY = y.Cell - (y.ParallelBoundary || (ty && y.Step < 0) ? 1 : 0);
            lowZ = z.Cell - (z.ParallelBoundary || (tz && z.Step < 0) ? 1 : 0);
            crossing = (tx ? 4 : 0) | (ty ? 2 : 0) | (tz ? 1 : 0);
            varied = crossing | (x.ParallelBoundary ? 4 : 0) | (y.ParallelBoundary ? 2 : 0) | (z.ParallelBoundary ? 1 : 0);
            if (tx)
                x.Advance(size, range);
            if (ty)
                y.Advance(size, range);
            if (tz)
                z.Advance(size, range);
            eventN = numerator;
            eventD = denominator;
            cursor = 0;
            return true;
        }
    }
}
