namespace WarCore.Tests.Oracle
{
    // This test-only reference checks the Q32 contract without reading product tables
    // or using expected vector outputs, so generation remains independent of Sim.
    internal sealed class Q32TrigReference
    {
        private const decimal Pi = 3.1415926535897932384626433833m;
        private const long Scale = 65536;
        private const long Precision = 1L << 32;
        private readonly long[] sine = new long[1025];
        private readonly long[] arctangent = new long[1025];

        internal Q32TrigReference()
        {
            // Decimal series avoid platform libm rounding when constructing specification tables.
            for (int i = 0; i <= 1024; i++)
            {
                sine[i] = (long)(SinSeries(i * Pi / 2048) * Scale * Precision + 0.5m);
                arctangent[i] = (long)(AtanSeries((decimal)i / 1024) / (2 * Pi) * Scale * Precision + 0.5m);
            }
        }

        private static decimal SinSeries(decimal value)
        {
            decimal term = value;
            decimal sum = value;
            for (int n = 1; n < 64; n++)
            {
                term = -term * value * value / ((2 * n) * (2 * n + 1));
                decimal next = sum + term;
                if (next == sum)
                    return sum;
                sum = next;
            }

            throw new System.InvalidOperationException("Sine reference did not converge.");
        }

        private static decimal AtanSeries(decimal value)
        {
            // Reducing to |x| <= 1/2 makes the alternating series converge within decimal precision.
            if (value > 0.5m)
                return Pi / 4 + AtanSeries((value - 1) / (value + 1));
            decimal power = value;
            decimal sum = value;
            for (int n = 1; n < 128; n++)
            {
                power = -power * value * value;
                decimal next = sum + power / (2 * n + 1);
                if (next == sum)
                    return sum;
                sum = next;
            }

            throw new System.InvalidOperationException("Arctangent reference did not converge.");
        }

        internal long Sin(long raw)
        {
            long phase = raw % Scale;
            if (phase < 0)
                phase += Scale;
            int quadrant = (int)(phase / 16384);
            int offset = (int)(phase % 16384);
            if ((quadrant & 1) != 0)
                offset = 16384 - offset;
            int index = offset / 16;
            long interpolated = sine[index] * 16;
            if (index < 1024)
                interpolated += (sine[index + 1] - sine[index]) * (offset % 16);
            long result = (interpolated + (1L << 35)) >> 36;
            return quadrant >= 2 ? -result : result;
        }

        internal long Cos(long raw) => Sin(raw % Scale + 16384);
        private static ulong Magnitude(long value) => value < 0 ? unchecked((ulong)(-(value + 1)) + 1) : (ulong)value;

        internal long Atan2(long y, long x)
        {
            if (x == 0 && y == 0)
                return 0;
            ulong ax = Magnitude(x);
            ulong ay = Magnitude(y);
            bool invert = ay > ax;
            ulong low = invert ? ax : ay;
            ulong high = invert ? ay : ax;
            while (high >= (1UL << 40))
            {
                low >>= 1;
                high >>= 1;
            }

            ulong ratio = (low << 22) / high;
            int index = (int)(ratio >> 12);
            long interpolated = arctangent[index] * 4096;
            if (index < 1024)
                interpolated += (arctangent[index + 1] - arctangent[index]) * (long)(ratio & 4095);
            long angle = (interpolated + (1L << 43)) >> 44;
            if (invert)
                angle = 16384 - angle;
            if (x < 0)
                angle = 32768 - angle;
            return y < 0 ? -angle : angle;
        }
    }
}
