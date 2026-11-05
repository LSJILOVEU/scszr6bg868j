using System.Numerics;

namespace WarCore.Tests
{
    // Test-owned distribution; it shares no arithmetic implementation with the product.
    internal struct BoundaryBiasedInputs
    {
        private A1MathInputs random;
        internal BoundaryBiasedInputs(ulong seed) => random = new A1MathInputs(seed);

        internal void Next(string operation, string distribution, int index, out long a, out long b)
        {
            if (distribution == "endpoints")
            {
                long offset = (long)(random.Next() & 65535);
                a = (index & 1) == 0 ? long.MaxValue - offset : long.MinValue + offset;
                b = operation == "mul" ? index % 5 - 2 : (index & 2) == 0 ? 65536 : -1;
                return;
            }
            if (distribution == "powers")
            {
                int bit = (int)(random.Next() % 63);
                a = (1L << bit) + (long)(random.Next() % 3) - 1;
                b = (1L << (62 - bit)) + (long)(random.Next() % 3) - 1;
                Signs(ref a, ref b);
                return;
            }
            if (distribution == "special")
            {
                a = Special(index % 5);
                b = Special((int)(random.Next() % 5));
                return;
            }
            BigInteger target = (BigInteger.One << 63) + (long)(random.Next() & ((1UL << 52) - 1))
                * ((index & 1) == 0 ? 1 : -1);
            switch (operation)
            {
                case "mul":
                    // Floor division misses the target by less than a <= 2^32, far inside the 1e-3 band.
                    a = 2 + (long)(random.Next() % uint.MaxValue);
                    b = (long)(target / a);
                    Signs(ref a, ref b);
                    break;
                case "div":
                    b = 1 + (long)(random.Next() % 65500);
                    a = (long)(target * b / 65536);
                    Signs(ref a, ref b);
                    break;
                case "sqrt":
                    BigInteger root = 1 + (random.Next() % 700000000000UL);
                    a = (long)((root * root + 65535) / 65536) + index % 3 - 1;
                    b = 0;
                    break;
                default:
                    long first = (long)(random.Next() & ((1UL << 50) - 1));
                    long second = (long)(random.Next() & ((1UL << 50) - 1));
                    a = (index & 1) == 0 ? long.MaxValue - first : long.MinValue + first;
                    b = (index & 1) == 0 ? long.MaxValue - second : long.MinValue + second;
                    break;
            }
        }

        private void Signs(ref long a, ref long b)
        {
            if ((random.Next() & 1) != 0)
                a = -a;
            if ((random.Next() & 1) != 0)
                b = -b;
        }

        private static long Special(int index) => index == 0 ? long.MinValue : index == 1 ? long.MaxValue
            : index == 2 ? 0 : index == 3 ? 1 : -1;
    }
}
