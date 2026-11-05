namespace WarCore.Sim
{
    // The gameplay domain is separate from SimFix's older overflow contract.
    public static class GameplayMath
    {
        public const long Scale = 65536;
        public const long MaxMagnitude = (1L << 47) - 1;
        public const long MaxFactor = 2147483647;
        public const int MaxModifiers = 65535;
        public const int MaxPackets = 1048575;

        public static void RequireMagnitude(long value)
        {
            if (value < 0 || value > MaxMagnitude)
                throw new SimBoundaryException("M0C_GAMEPLAY_MAGNITUDE: Outside [0,M].");
        }
        public static void RequireSignedMagnitude(long value)
        {
            if (value < -MaxMagnitude || value > MaxMagnitude)
                throw new SimBoundaryException("M0C_GAMEPLAY_SIGNED: Outside [-M,M].");
        }
        public static void RequireFactor(long value)
        {
            if (value < 0 || value > MaxFactor)
                throw new SimBoundaryException("M0C_GAMEPLAY_FACTOR: Outside [0,F].");
        }
        public static void RequireRate(long value)
        {
            if (value < -MaxFactor || value > MaxFactor)
                throw new SimBoundaryException("M0C_GAMEPLAY_RATE: Outside [-F,F].");
        }
        public static void RequireRatio(long value)
        {
            if (value < 0 || value > Scale)
                throw new SimBoundaryException("M0C_GAMEPLAY_RATIO: Outside [0,S].");
        }
        public static void CountSaturation(ref long count)
        {
            if (count < 0 || count == long.MaxValue)
                throw new SimBoundaryException("M0C_GAMEPLAY_DIAGNOSTIC: Counter exhausted or invalid.");
            count++;
        }
        public static long FutureTick(long now, int duration, ref long timeSaturations)
        {
            const long limit=long.MaxValue-1;
            if(now<0 || now>limit || duration<=0 || timeSaturations<0)
                throw new SimBoundaryException("M0C_GAMEPLAY_TIME: Invalid clock, period or counter.");
            // Compare the available distance before adding; no overflowing addition is evaluated.
            bool saturated=duration>limit-now;
            long future=saturated?limit:now+duration;
            if(future<=now)
                throw new SimBoundaryException("M0C_TIME_EXHAUSTED: No representable future tick.");
            if(saturated)CountSaturation(ref timeSaturations);
            return future;
        }
        public static long ChangeResource(long value, long delta, long lower, long upper, ref long saturationCount)
        {
            RequireSignedMagnitude(value); RequireSignedMagnitude(delta);
            RequireSignedMagnitude(lower); RequireSignedMagnitude(upper);
            if (lower > upper || value < lower || value > upper || saturationCount < 0)
                throw new SimBoundaryException("M0C_GAMEPLAY_RESOURCE: Invalid resource bounds.");
            // Validated operands give |sum| <= 2M < 2^48, including signed limits.
            long result=value+delta;
            if (result < lower) { CountSaturation(ref saturationCount); return lower; }
            if (result > upper) { CountSaturation(ref saturationCount); return upper; }
            return result;
        }
        public static bool CanSpend(long value, long lower, long cost)
        {
            RequireSignedMagnitude(value); RequireSignedMagnitude(lower); RequireMagnitude(cost);
            if (value < lower)
                throw new SimBoundaryException("M0C_GAMEPLAY_RESOURCE: Value is below lower bound.");
            return value-lower >= cost; // S1_MUTATION:resource_reject
        }
        public static bool Spend(long value, long lower, long cost, out long result)
        {
            bool accepted=CanSpend(value,lower,cost);
            result=accepted ? value-cost : value;
            return accepted;
        }
        public static long SaturatingAdd(long value, long addition, ref long saturationCount)
        {
            RequireMagnitude(value); RequireSignedMagnitude(addition);
            return ClampWide(new FixedInt128(new FixedUInt128((ulong)value),false)+
                new FixedInt128(new FixedUInt128(FixedInt128.Abs(addition)),addition<0),ref saturationCount);
        }
        public static long MultiplyFactor(long value, long factor, ref long saturationCount)
        {
            RequireMagnitude(value); RequireFactor(factor);
            if (saturationCount < 0)
                throw new SimBoundaryException("M0C_GAMEPLAY_DIAGNOSTIC: Negative counter.");
            // x < 2^47 and factor < 2^31: product < 2^78, shift is the fixed legal count 16.
            FixedUInt128 product=FixedUInt128.Multiply((ulong)value,(ulong)factor);
            FixedUInt128 scaled=product.ShiftRight(16); // S1_MUTATION:product_rounding
            if (scaled.High != 0 || scaled.Low > (ulong)MaxMagnitude) // S1_MUTATION:product_saturation
            { CountSaturation(ref saturationCount); return MaxMagnitude; }
            return (long)scaled.Low;
        }
        internal static long ClampWide(FixedInt128 value, ref long saturationCount)
        {
            if (saturationCount < 0)
                throw new SimBoundaryException("M0C_GAMEPLAY_DIAGNOSTIC: Negative counter.");
            if (value.Negative) { CountSaturation(ref saturationCount); return 0; } // S1_MUTATION:flat_clamp
            if (value.Magnitude.High != 0 || value.Magnitude.Low > (ulong)MaxMagnitude)
            { CountSaturation(ref saturationCount); return MaxMagnitude; }
            return (long)value.Magnitude.Low;
        }
    }
}
