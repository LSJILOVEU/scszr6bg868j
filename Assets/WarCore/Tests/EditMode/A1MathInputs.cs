using System.Collections.Generic;

namespace WarCore.Tests
{
    // A test-owned bit stream keeps randomized checks reproducible across Mono and Unity.
    internal struct A1MathInputs
    {
        private ulong state;

        internal A1MathInputs(ulong seed)
        {
            state = seed;
        }

        internal ulong Next()
        {
            unchecked
            {
                state += 0x9E3779B97F4A7C15UL;
                ulong value = state;
                value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
                value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
                return value ^ (value >> 31);
            }
        }

        internal long Long() => unchecked((long)Next());

        internal long Bits(int count)
        {
            long magnitude = (long)(Next() & ((1UL << count) - 1));
            return (Next() & 1) == 0 ? magnitude : -magnitude;
        }

        internal long Sample(long[] edges)
        {
            switch (Next() % 5)
            {
                case 0:
                    return Long();
                case 1:
                    return Bits((int)(Next() % 64));
                case 2:
                    return edges[Next() % (ulong)edges.Length];
                case 3:
                    return long.MaxValue - (long)(Next() & 65535);
                default:
                    return long.MinValue + (long)(Next() & 65535);
            }
        }

        internal static long[] Edges()
        {
            var values = new List<long>{long.MinValue, long.MinValue + 1, long.MinValue + 2, long.MaxValue, long.MaxValue - 1, long.MaxValue - 2, 0, 1, -1};
            for (int bit = 0; bit <= 62; bit++)
            {
                for (int delta = -1; delta <= 1; delta++)
                {
                    long value = (1L << bit) + delta;
                    values.Add(value);
                    values.Add(-value);
                }
            }

            return values.ToArray();
        }
    }
}
