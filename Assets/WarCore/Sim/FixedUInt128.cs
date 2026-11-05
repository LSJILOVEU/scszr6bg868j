using System;
using System.Runtime.CompilerServices;

namespace WarCore.Sim
{
    // Two limbs suffice for three full-range long products, including 3 * 2^126.
    internal readonly struct FixedUInt128 : IComparable<FixedUInt128>
    {
        internal readonly ulong Low;
        internal readonly ulong High;
        internal bool IsZero => (Low | High) == 0;

        internal FixedUInt128(ulong low, ulong high = 0)
        {
            Low = low;
            High = high;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static FixedUInt128 Multiply(ulong a, ulong b)
        {
            ulong a0 = unchecked((uint)a);
            ulong b0 = unchecked((uint)b);
            ulong a1 = a >> 32;
            ulong b1 = b >> 32;
            ulong p00 = a0 * b0;
            ulong p01 = a0 * b1;
            ulong p10 = a1 * b0;
            ulong p11 = a1 * b1;
            // Three 32-bit terms need at most 34 bits; every carry enters the high limb.
            ulong middle = (p00 >> 32) + unchecked((uint)p01) + (ulong)unchecked((uint)p10);
            ulong low = (middle << 32) | unchecked((uint)p00);
            // Nonnegative partial sums cannot exceed the final high limb (at most 2^64 - 2).
            ulong high = p11 + (p01 >> 32) + (p10 >> 32) + (middle >> 32);
            return new FixedUInt128(low, high);
        }

        internal static FixedUInt128 Scale16(ulong value) => new FixedUInt128(value << 16, value >> 48);

        public int CompareTo(FixedUInt128 other)
        {
            if (High != other.High)
                return High < other.High ? -1 : 1;
            return Low == other.Low ? 0 : Low < other.Low ? -1 : 1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedUInt128 operator +(FixedUInt128 a, FixedUInt128 b)
        {
            ulong low = unchecked(a.Low + b.Low);
            ulong carry = low < a.Low ? 1UL : 0UL;
            if (a.High > ulong.MaxValue - b.High || a.High + b.High > ulong.MaxValue - carry)
                throw new SimOverflowException();
            return new FixedUInt128(low, a.High + b.High + carry);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedUInt128 operator -(FixedUInt128 a, FixedUInt128 b)
        {
            ulong borrow = a.Low < b.Low ? 1UL : 0UL;
            if (a.High < b.High || a.High - b.High < borrow)
                throw new SimOverflowException();
            return new FixedUInt128(unchecked(a.Low - b.Low), a.High - b.High - borrow);
        }

        internal FixedUInt128 TimesFour()
        {
            if ((High >> 62) != 0)
                throw new SimOverflowException();
            return new FixedUInt128(Low << 2, (High << 2) | (Low >> 62));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal FixedUInt128 ShiftRight(int bits)
        {
            if (bits < 0)
                throw new SimBoundaryException("Wide shift count must be nonnegative.");
            // A logical wide shift discards the complete value at its width; it never masks the count.
            if (bits >= 128)
                return default;
            if (bits == 0)
                return this;
            if (bits >= 64)
                return new FixedUInt128(High >> (bits - 64));
            return new FixedUInt128((Low >> bits) | (High << (64 - bits)), High >> bits);
        }

        private static int BitLength(ulong value)
        {
            int bits = 0;
            if (value >= (1UL << 32))
            {
                value >>= 32;
                bits += 32;
            }

            if (value >= (1UL << 16))
            {
                value >>= 16;
                bits += 16;
            }

            if (value >= (1UL << 8))
            {
                value >>= 8;
                bits += 8;
            }

            if (value >= (1UL << 4))
            {
                value >>= 4;
                bits += 4;
            }

            if (value >= (1UL << 2))
            {
                value >>= 2;
                bits += 2;
            }

            if (value >= (1UL << 1))
            {
                value >>= 1;
                bits++;
            }

            return bits + (int)value;
        }

        private int Bits => High == 0 ? BitLength(Low) : 64 + BitLength(High);

        internal ulong DivideToUInt64(ulong divisor)
        {
            if (divisor == 0)
                throw new SimDivideByZeroException();
            if (High == 0)
                return Low / divisor;
            // A high limb at least as large as the divisor means a 65-bit quotient.
            if (High >= divisor)
                throw new SimOverflowException();
            int bit = Bits - BitLength(divisor);
            if (bit > 63)
                bit = 63;
            var shifted = new FixedUInt128(divisor << bit, bit == 0 ? 0 : divisor >> (64 - bit));
            FixedUInt128 remainder = this;
            ulong quotient = 0;
            // Align by quotient width: the old Q16 atan ratio needs at most 17 steps.
            for (; bit >= 0; bit--)
            {
                if (remainder.CompareTo(shifted) >= 0)
                {
                    remainder -= shifted;
                    quotient |= 1UL << bit;
                }

                shifted = shifted.ShiftRight(1);
            }

            return quotient;
        }

        internal ulong Sqrt()
        {
            if (IsZero)
                return 0;
            if (High == 0 && Low < ScalarRootLimit)
                return SqrtLow();
            int bitIndex = (Bits - 1) & ~1;
            FixedUInt128 bit = bitIndex < 64
                ? new FixedUInt128(1UL << bitIndex)
                : new FixedUInt128(0, 1UL << (bitIndex - 64));
            FixedUInt128 remainder = this;
            var root = new FixedUInt128();
            // Restoring square root consumes two input bits per step without wide multiplication.
            // root + bit stays below 2^128; the final root always fits an unsigned 64-bit limb.
            while (!bit.IsZero)
            {
                FixedUInt128 trial = root + bit;
                if (remainder.CompareTo(trial) >= 0)
                {
                    remainder -= trial;
                    root = root.ShiftRight(1) + bit;
                }
                else
                    root = root.ShiftRight(1);
                bit = bit.ShiftRight(2);
            }

            return root.Low;
        }

        internal const ulong ScalarRootLimit = 1UL << 62;

        private ulong SqrtLow()
        {
            // The same restoring algorithm needs only one limb below 2^62.
            // bit starts at <=2^60; trial <=5*2^58 and root updates <=2^60.
            // Remainder keeps two spare bits, all additions at least three; no native trap is used.
            ulong bit = 1UL << ((BitLength(Low) - 1) & ~1);
            ulong remainder = Low;
            ulong root = 0;
            while (bit != 0)
            {
                ulong trial = root + bit;
                if (remainder >= trial)
                {
                    remainder -= trial;
                    root = (root >> 1) + bit;
                }
                else
                    root >>= 1;
                bit >>= 2;
            }
            return root;
        }

        internal uint Word(int index)
        {
            if (index < 0 || index >= 4)
                return 0;
            ulong limb = index < 2 ? Low : High;
            return unchecked((uint)(limb >> ((index & 1) * 32)));
        }
    }
}
