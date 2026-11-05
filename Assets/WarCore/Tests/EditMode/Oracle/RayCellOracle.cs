using System;
using System.Collections.Generic;
using System.Numerics;

namespace WarCore.Tests.Oracle
{
    // Enumerating closed boxes and intersecting each independently avoids sharing DDA state or tie logic.
    internal static class RayCellOracle
    {
        internal readonly struct Entry
        {
            internal readonly long X;
            internal readonly long Y;
            internal readonly long Z;
            internal readonly BigInteger Numerator;
            internal readonly BigInteger Denominator;

            internal Entry(long x, long y, long z, BigInteger numerator, BigInteger denominator)
            {
                X = x;
                Y = y;
                Z = z;
                Numerator = numerator;
                Denominator = denominator;
            }
        }

        private static long Floor(long value, long size)
        {
            BigInteger quotient = BigInteger.DivRem(value, size, out BigInteger remainder);
            return (long)(remainder.Sign < 0 ? quotient - 1 : quotient);
        }

        internal static List<Entry> Enumerate(long ox, long oy, long oz, long dx, long dy, long dz, long range, long size)
        {
            if (size <= 0 || range < 0 || (dx == 0 && dy == 0 && dz == 0))
                throw new ArgumentException("Invalid oracle ray.");
            long[] origin = { ox, oy, oz };
            long[] direction = { dx, dy, dz };
            var minimum = new long[3];
            var maximum = new long[3];
            for (int axis = 0; axis < 3; axis++)
            {
                minimum[axis] = Floor(origin[axis] - (direction[axis] < 0 ? range : 0), size) - 1;
                maximum[axis] = Floor(origin[axis] + (direction[axis] > 0 ? range : 0), size);
            }

            var result = new List<Entry>();
            BigInteger directionSquared = (BigInteger)dx * dx + (BigInteger)dy * dy + (BigInteger)dz * dz;
            for (long x = minimum[0]; x <= maximum[0]; x++)
                for (long y = minimum[1]; y <= maximum[1]; y++)
                    for (long z = minimum[2]; z <= maximum[2]; z++)
                    {
                        BigInteger numerator = 0;
                        BigInteger denominator = 1;
                        BigInteger exitN = 0;
                        BigInteger exitD = 0;
                        if (!Slab(ox, dx, (BigInteger)x * size, ((BigInteger)x + 1) * size, ref numerator, ref denominator, ref exitN, ref exitD)
                            || !Slab(oy, dy, (BigInteger)y * size, ((BigInteger)y + 1) * size, ref numerator, ref denominator, ref exitN, ref exitD)
                            || !Slab(oz, dz, (BigInteger)z * size, ((BigInteger)z + 1) * size, ref numerator, ref denominator, ref exitN, ref exitD))
                            continue;
                        if (numerator * numerator * directionSquared <= (BigInteger)range * range * denominator * denominator)
                            result.Add(new Entry(x, y, z, numerator, denominator));
                    }

            result.Sort(Compare);
            return result;
        }

        private static bool Slab(long origin, long direction, BigInteger low, BigInteger high,
            ref BigInteger enterN, ref BigInteger enterD, ref BigInteger exitN, ref BigInteger exitD)
        {
            if (direction == 0)
                return low <= origin && origin <= high;
            BigInteger near = direction > 0 ? low - origin : origin - high;
            BigInteger far = direction > 0 ? high - origin : origin - low;
            BigInteger denominator = BigInteger.Abs(direction);
            if (near * enterD > enterN * denominator)
            {
                enterN = near;
                enterD = denominator;
            }
            if (exitD.IsZero || far * exitD < exitN * denominator)
            {
                exitN = far;
                exitD = denominator;
            }
            return enterN * exitD <= exitN * enterD;
        }

        private static int Compare(Entry a, Entry b)
        {
            int time = (a.Numerator * b.Denominator).CompareTo(b.Numerator * a.Denominator);
            if (time != 0)
                return time;
            int x = a.X.CompareTo(b.X);
            if (x != 0)
                return x;
            int y = a.Y.CompareTo(b.Y);
            return y != 0 ? y : a.Z.CompareTo(b.Z);
        }
    }
}
