using System;

namespace WarCore.Sim
{
    // Q16.16-long means 16 fractional bits in a signed long, not a 32-bit container.
    // One metre is 65536 raw units: resolution is about 0.0153 millimetres.
    // All rescaling truncates toward zero, including negative multiplication and division.
    public readonly struct SimFix : IEquatable<SimFix>, IComparable<SimFix>, IStateHash
    {
        public const long Scale = 65536;
        public long Raw
        {
            get;
        }

        private SimFix(long raw)
        {
            Raw = raw;
        }

        public static SimFix FromRaw(long raw) => new SimFix(raw);
        // Integer conversion is exact; explicit range checks reject values that cannot be scaled.
        public static SimFix FromInt(long value) => new SimFix(SimInteger.Multiply(value, Scale));
        // Ratios truncate toward zero: 1/30 is 2184 raw and -1/30 is -2184 raw.
        public static SimFix Ratio(long numerator, long denominator) => FromRaw(FixedInt128.DivideScaled(numerator, denominator));

        public static SimFix Abs(SimFix value) => FromRaw(SimInteger.Abs(value.Raw));
        public static SimFix Min(SimFix a, SimFix b) => a.Raw <= b.Raw ? a : b;
        public static SimFix Max(SimFix a, SimFix b) => a.Raw >= b.Raw ? a : b;
        public static SimFix Clamp(SimFix value, SimFix minimum, SimFix maximum)
        {
            if (minimum > maximum)
                throw new ArgumentException("Clamp minimum must not exceed maximum.");
            return Max(minimum, Min(value, maximum));
        }

        // Do not clamp t: extrapolation is useful for deterministic prediction. Accumulate before
        // truncating toward zero so opposite long endpoints need no overflowing long difference.
        public static SimFix Lerp(SimFix a, SimFix b, SimFix t)
        {
            FixedInt128 numerator = FixedInt128.Product(a.Raw, Scale)
                + FixedInt128.Product(b.Raw, t.Raw) - FixedInt128.Product(a.Raw, t.Raw);
            return FromRaw(numerator.Shift16ToLong());
        }

        // Floor moves toward negative infinity. MinValue is already an exact multiple of Scale.
        public static SimFix Floor(SimFix value)
        {
            long remainder = value.Raw % Scale;
            long whole = value.Raw - remainder;
            return FromRaw(remainder < 0 ? SimInteger.Subtract(whole, Scale) : whole);
        }

        // Nearest integer, with exact halves away from zero; a positive endpoint carry is an
        // explicit G25 overflow, never a wrapped negative value or a runtime arithmetic trap.
        public static SimFix Round(SimFix value)
        {
            long remainder = value.Raw % Scale;
            long whole = value.Raw - remainder;
            if (remainder >= Scale / 2)
                whole = SimInteger.Add(whole, Scale);
            else if (remainder <= -Scale / 2)
                whole = SimInteger.Subtract(whole, Scale);
            return FromRaw(whole);
        }
        public static SimFix operator +(SimFix x, SimFix y) => FromRaw(SimInteger.Add(x.Raw, y.Raw));
        public static SimFix operator -(SimFix x, SimFix y) => FromRaw(SimInteger.Subtract(x.Raw, y.Raw));
        public static SimFix operator -(SimFix x) => FromRaw(SimInteger.Negate(x.Raw));
        public static SimFix operator *(SimFix x, SimFix y)
        {
            FixedUInt128 magnitude = FixedUInt128.Multiply(FixedInt128.Abs(x.Raw), FixedInt128.Abs(y.Raw));
            // This world-range guard is intentionally stricter than long result overflow, even for negatives.
            // The unscaled product magnitude must not exceed 2^63 - 1.
            if (magnitude.High != 0 || magnitude.Low > long.MaxValue)
                throw new SimOverflowException("Product must be below 2^31 square metres.");
            long raw = (long)(magnitude.Low >> 16);
            return FromRaw((x.Raw < 0) != (y.Raw < 0) ? -raw : raw);
        }

        public static SimFix operator /(SimFix x, SimFix y) => FromRaw(FixedInt128.DivideScaled(x.Raw, y.Raw));
        public static bool operator <(SimFix x, SimFix y) => x.Raw < y.Raw;
        public static bool operator>(SimFix x, SimFix y) => x.Raw > y.Raw;
        public static bool operator <=(SimFix x, SimFix y) => x.Raw <= y.Raw;
        public static bool operator >=(SimFix x, SimFix y) => x.Raw >= y.Raw;
        public static bool operator ==(SimFix x, SimFix y) => x.Raw == y.Raw;
        public static bool operator !=(SimFix x, SimFix y) => x.Raw != y.Raw;
        public bool Equals(SimFix other) => Raw == other.Raw;
        public override bool Equals(object obj) => obj is SimFix other && Equals(other);
        public override int GetHashCode() => Raw.GetHashCode();
        public int CompareTo(SimFix other) => Raw.CompareTo(other.Raw);
        public void WriteState(StateHash hash)
        {
            hash.Add(Raw);
        }

        public override string ToString() => Raw.ToString() + "/65536";
    }

}
