using System;
using System.Runtime.CompilerServices;

namespace WarCore.Sim
{
    // A separate sign keeps all 128 magnitude bits: three long.MinValue squares exceed signed 128 bits.
    internal readonly struct FixedInt128
    {
        internal readonly FixedUInt128 Magnitude;
        internal readonly bool Negative;
        internal int Sign => Magnitude.IsZero ? 0 : Negative ? -1 : 1;

        internal FixedInt128(FixedUInt128 magnitude, bool negative)
        {
            Magnitude = magnitude;
            Negative = negative && !magnitude.IsZero;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static ulong Abs(long value) => value < 0 ? unchecked((ulong)(-(value + 1)) + 1) : (ulong)value;

        internal static FixedInt128 Product(long a, long b) => new FixedInt128(FixedUInt128.Multiply(Abs(a), Abs(b)), (a < 0) != (b < 0));

        public static FixedInt128 operator +(FixedInt128 a, FixedInt128 b)
        {
            if (a.Negative == b.Negative)
                return new FixedInt128(a.Magnitude + b.Magnitude, a.Negative);
            return a.Magnitude.CompareTo(b.Magnitude) >= 0
                ? new FixedInt128(a.Magnitude - b.Magnitude, a.Negative)
                : new FixedInt128(b.Magnitude - a.Magnitude, b.Negative);
        }

        public static FixedInt128 operator -(FixedInt128 a, FixedInt128 b) => a + new FixedInt128(b.Magnitude, !b.Negative);

        internal static long ToLong(ulong magnitude, bool negative)
        {
            if (negative && magnitude == 0x8000000000000000UL)
                return long.MinValue;
            if (magnitude > long.MaxValue)
                throw new SimOverflowException();
            return negative ? -(long)magnitude : (long)magnitude;
        }

        internal long Shift16ToLong()
        {
            FixedUInt128 scaled = Magnitude.ShiftRight(16);
            if (scaled.High != 0)
                throw new SimOverflowException();
            return ToLong(scaled.Low, Negative);
        }

        internal long SqrtToLong()
        {
            // Report the caller-facing value, not the removed test-oracle implementation.
            if (Negative)
                throw new ArgumentOutOfRangeException("value");
            return ToLong(Magnitude.Sqrt(), false);
        }

        internal static long DivideScaled(long a, long b)
        {
            ulong quotient = FixedUInt128.Scale16(Abs(a)).DivideToUInt64(Abs(b));
            return ToLong(quotient, (a < 0) != (b < 0));
        }

        internal static long Midpoint(long a, long b)
        {
            // Opposite signs cannot overflow their sum, and direct division keeps truncation toward zero.
            if ((a ^ b) < 0)
                return (a + b) / 2;
            // For equal signs, split the halves before adding so both long endpoints remain valid.
            return a / 2 + b / 2 + (a % 2 + b % 2) / 2;
        }
    }
}
