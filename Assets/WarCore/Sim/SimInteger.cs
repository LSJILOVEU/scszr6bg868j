using System;
using System.Runtime.CompilerServices;

namespace WarCore.Sim
{
    // Arithmetic contract v2 distinguishes simulation failures from runtime-generated traps.
    public sealed class SimOverflowException : SimBoundaryException
    {
        public SimOverflowException() : base("Integer result exceeds the simulation range.")
        {
        }

        public SimOverflowException(string message) : base(message)
        {
        }
    }

    public sealed class SimDivideByZeroException : SimBoundaryException
    {
        public SimDivideByZeroException() : base("Simulation divisor must be nonzero.")
        {
        }
    }

    internal static class SimInteger
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static long Add(long a, long b)
        {
            if ((b > 0 && a > long.MaxValue - b) || (b < 0 && a < long.MinValue - b))
                throw new SimOverflowException();
            return a + b;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static long Subtract(long a, long b)
        {
            if ((b > 0 && a < long.MinValue + b) || (b < 0 && a > long.MaxValue + b))
                throw new SimOverflowException();
            return a - b;
        }

        internal static long Negate(long value)
        {
            if (value == long.MinValue)
                throw new SimOverflowException();
            return -value;
        }

        internal static long Abs(long value) => value < 0 ? Negate(value) : value;

        internal static long Multiply(long a, long b)
        {
            FixedUInt128 product = FixedUInt128.Multiply(FixedInt128.Abs(a), FixedInt128.Abs(b));
            if (product.High != 0)
                throw new SimOverflowException();
            return FixedInt128.ToLong(product.Low, (a < 0) != (b < 0));
        }

        internal static long Divide(long a, long b)
        {
            if (b == 0)
                throw new SimDivideByZeroException();
            if (a == long.MinValue && b == -1)
                throw new SimOverflowException();
            return a / b;
        }

        internal static long Remainder(long a, long b)
        {
            if (b == 0)
                throw new SimDivideByZeroException();
            // The exact remainder is zero; avoid the native signed division instruction at this pair.
            if (a == long.MinValue && b == -1)
                return 0;
            return a % b;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int ToInt32(long value)
        {
            if (value < int.MinValue || value > int.MaxValue)
                throw new SimOverflowException();
            return (int)value;
        }

        // Bit encodings use only representable signed casts; C++ need not interpret an out-of-range cast.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static long FromBits(ulong bits) => bits <= long.MaxValue ? (long)bits : long.MinValue + (long)(bits & long.MaxValue);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static ulong ToBits(long value) => value >= 0 ? (ulong)value : (1UL << 63) | (ulong)(value - long.MinValue);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int Int32Bits(ulong bits)
        {
            uint low = (uint)(bits & uint.MaxValue);
            return low <= int.MaxValue ? (int)low : int.MinValue + (int)(low & int.MaxValue);
        }

        internal static long ShiftLeft(long value, int count)
        {
            // Scalar shifts retain the C# low-six-bit count contract, including negative counts.
            int shift = count & 63;
            return FromBits(ToBits(value) << shift);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static long ShiftRight(long value, int count)
        {
            int shift = count & 63;
            if (shift == 0)
                return value;
            ulong bits = ToBits(value) >> shift;
            if (value < 0)
                bits |= ulong.MaxValue << (64 - shift);
            return FromBits(bits);
        }
    }
}
