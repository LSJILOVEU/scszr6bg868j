using System;

namespace WarCore.Sim
{
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class ImmutableTableAttribute : Attribute
    {
        public ulong Checksum
        {
            get;
        }

        public ImmutableTableAttribute(ulong checksum)
        {
            Checksum = checksum;
        }
    }

    public static class SimMath
    {
        public static SimFix Sqrt(SimFix value) => SimFix.FromRaw(new FixedInt128(FixedUInt128.Scale16(FixedInt128.Abs(value.Raw)), value.Raw < 0).SqrtToLong());
        // Angles use turns: this gives exact axes and exact periodic wrapping in binary fixed point.
        public static SimFix Sin(SimFix turns)
        {
            long phase = SimInteger.Remainder(turns.Raw, SimFix.Scale);
            if (phase < 0)
                phase += SimFix.Scale;
            int quadrant = (int)(phase / 16384);
            int offset = (int)(phase % 16384);
            if ((quadrant & 1) != 0)
                offset = 16384 - offset;
            int index = offset / 16;
            long start = TrigTables.SinAt(index);
            // Weight before rounding: the largest numerator is 2^52, well inside a signed long.
            long interpolated = start * 16;
            if (index < 1024)
                interpolated += (TrigTables.SinAt(index + 1) - start) * (offset % 16);
            long value = (interpolated + (1L << 35)) >> 36;
            return SimFix.FromRaw(quadrant >= 2 ? -value : value);
        }

        public static SimFix Cos(SimFix turns) => Sin(SimFix.FromRaw(SimInteger.Remainder(turns.Raw, SimFix.Scale) + 16384));
        public static SimFix Atan2(SimFix y, SimFix x)
        {
            // Atan2(0, 0) returns exactly zero turns because an absent direction has no preferred axis.
            if (x.Raw == 0 && y.Raw == 0)
                return default;
            ulong ax = FixedInt128.Abs(x.Raw);
            ulong ay = FixedInt128.Abs(y.Raw);
            bool invert = ay > ax;
            ulong low = invert ? ax : ay;
            ulong high = invert ? ay : ax;
            // A common shift preserves the ratio to the contract's precision and bounds low << 22 below 2^62.
            while (high >= (1UL << 40))
            {
                low >>= 1;
                high >>= 1;
            }

            ulong ratio = (low << 22) / high;
            int index = (int)(ratio >> 12);
            long start = TrigTables.AtanAt(index);
            // The table peaks at 2^45; all interpolation terms and the rounding carry fit below 2^58.
            long interpolated = start * 4096;
            if (index < 1024)
                interpolated += (TrigTables.AtanAt(index + 1) - start) * (long)(ratio & 4095);
            long angle = (interpolated + (1L << 43)) >> 44;
            if (invert)
                angle = 16384 - angle;
            if (x.Raw < 0)
                angle = 32768 - angle;
            if (y.Raw < 0)
                angle = -angle;
            return SimFix.FromRaw(angle);
        }
    }
}
