using System;
using System.Collections.Generic;

namespace WarCore.Sim
{
    // Which bound replaced the drag product in one calculation.
    public enum StatClamp
    {
        None = 0,
        Floor = 1,
        Ceil = 2
    }

    // The receipt of one StatLine.Apply: the inputs, whether the drags were clamped, and the same result Apply returns.
    public sealed class StatReceipt
    {
        public long Base { get; }
        public IReadOnlyList<int> Gears { get; }
        public IReadOnlyList<int> Drags { get; }
        public int DragFloorPermille { get; }
        public int DragCeilPermille { get; }
        public StatClamp Clamp { get; }
        // The single factor that stood in for every drag (the floor or the ceiling); 0 when the drags were not clamped.
        public int ClampPermille { get; }
        public long Result { get; }

        internal StatReceipt(long baseValue, int[] gears, int[] drags, int floor, int ceil, StatClamp clamp, int clampPermille, long result)
        {
            Base = baseValue;
            Gears = Array.AsReadOnly(gears);
            Drags = Array.AsReadOnly(drags);
            DragFloorPermille = floor;
            DragCeilPermille = ceil;
            Clamp = clamp;
            ClampPermille = clampPermille;
            Result = result;
        }
    }

    // One formula for every derived number (speed, spread, ...): result = sign(base) * floor(|base| * product(factors) / 1000^n).
    // Factors are permille, 1..4000; all of them are multiplied first and the product is divided once, so the result does not
    // depend on their order. Apply multiplies gears as they are and clamps only the drag product, between a floor and a ceiling.
    //
    // Widths (n factors, n <= 7; |base| <= 2^31 - 1; each factor <= 4000):
    // - A = |base| * f0 * f1 <= (2^31 - 1) * 4000^2 ~ 3.44e16 < 2^63 fits a ulong; a third factor would not (~1.37e20 > 2^64).
    // - B = f2 * ... * f6 <= 4000^5 = 1.024e18 < 2^63.
    // - FixedUInt128.Multiply(A, B) < 3.52e34 < 2^128.
    // - n <= 6: one DivideToUInt64(1000^n) with 1000^n <= 1e18 < 2^64. n = 7: DivideToUInt64(1e18) (quotient <= 3.52e16), then
    //   a ulong division by 1000. For positive integers floor(floor(x / a) / b) = floor(x / (a * b)), so the two divisions are bit
    //   for bit the single one; dividing after every factor is a different thing and loses precision.
    // - The result is at most 2^31 * 4^7 = 2^45.
    // - Clamp test: P * 1000 < floor * 1000^d is P < floor * 1000^(d - 1). P <= 4000^7 ~ 1.64e25 is the 128-bit product of the
    //   first four drags (<= 2.56e14) and the rest (<= 6.4e10); the right side is Multiply(floor, 1000^(d - 1)) with
    //   1000^(d - 1) <= 1e18. Both sides are compared with CompareTo. The ceiling likewise.
    public static class StatLine
    {
        public const int MinPermille = 1, MaxPermille = 4000, MaxFactors = 7;
        public const long MaxBase = 2147483647;
        private const int One = 1000;

        public static long Scale(long baseValue, ReadOnlySpan<int> factors)
        {
            CheckBase(baseValue);
            if (factors.Length > MaxFactors)
                throw new ArgumentOutOfRangeException(nameof(factors));
            CheckFactors(factors, nameof(factors));
            return Multiply(baseValue, factors, ReadOnlySpan<int>.Empty, 0);
        }

        public static long Apply(long baseValue, ReadOnlySpan<int> gears, ReadOnlySpan<int> drags, int dragFloorPermille, int dragCeilPermille)
            => Compute(baseValue, gears, drags, dragFloorPermille, dragCeilPermille, out _, out _);

        public static StatReceipt Explain(long baseValue, ReadOnlySpan<int> gears, ReadOnlySpan<int> drags, int dragFloorPermille, int dragCeilPermille)
        {
            long result = Compute(baseValue, gears, drags, dragFloorPermille, dragCeilPermille, out StatClamp clamp, out int clampPermille);
            return new StatReceipt(baseValue, gears.ToArray(), drags.ToArray(), dragFloorPermille, dragCeilPermille, clamp, clampPermille, result);
        }

        // Apply and Explain both run this; Explain only adds the receipt.
        private static long Compute(long baseValue, ReadOnlySpan<int> gears, ReadOnlySpan<int> drags, int dragFloorPermille, int dragCeilPermille,
            out StatClamp clamp, out int clampPermille)
        {
            CheckBase(baseValue);
            if (gears.Length > MaxFactors)
                throw new ArgumentOutOfRangeException(nameof(gears));
            if (gears.Length + drags.Length > MaxFactors)
                throw new ArgumentOutOfRangeException(nameof(drags));
            CheckFactors(gears, nameof(gears));
            CheckFactors(drags, nameof(drags));
            int floor = dragFloorPermille, ceil = dragCeilPermille;
            CheckBound(floor, nameof(dragFloorPermille));
            CheckBound(ceil, nameof(dragCeilPermille));
            if (floor != 0 && ceil != 0 && floor > ceil)
                throw new ArgumentOutOfRangeException(nameof(dragFloorPermille));
            clamp = StatClamp.None;
            clampPermille = 0;
            if (drags.Length != 0 && (floor != 0 || ceil != 0))
            {
                FixedUInt128 product = Product(drags);
                ulong scale = Power(drags.Length - 1);
                if (floor != 0 && product.CompareTo(FixedUInt128.Multiply((ulong)floor, scale)) < 0)
                {
                    clamp = StatClamp.Floor;
                    clampPermille = floor;
                }
                else if (ceil != 0 && product.CompareTo(FixedUInt128.Multiply((ulong)ceil, scale)) > 0)
                {
                    clamp = StatClamp.Ceil;
                    clampPermille = ceil;
                }
            }

            return clamp == StatClamp.None
                ? Multiply(baseValue, gears, drags, 0)
                : Multiply(baseValue, gears, ReadOnlySpan<int>.Empty, clampPermille);
        }

        // The factors are first ++ second, then extra when it is not 0; at most MaxFactors in all (checked by the callers).
        private static long Multiply(long baseValue, ReadOnlySpan<int> first, ReadOnlySpan<int> second, int extra)
        {
            int count = first.Length + second.Length + (extra != 0 ? 1 : 0);
            if (count == 0)
                return baseValue;
            bool negative = baseValue < 0;
            ulong a = (ulong)(negative ? -baseValue : baseValue);
            ulong b = 1;
            for (int i = 0; i < count; i++)
            {
                ulong factor = (ulong)FactorAt(first, second, extra, i);
                if (i < 2)
                    a *= factor;
                else
                    b *= factor;
            }

            FixedUInt128 wide = FixedUInt128.Multiply(a, b);
            ulong quotient = count < MaxFactors
                ? wide.DivideToUInt64(Power(count))
                : wide.DivideToUInt64(Power(MaxFactors - 1)) / One;
            long magnitude = (long)quotient;
            return negative ? -magnitude : magnitude;
        }

        private static int FactorAt(ReadOnlySpan<int> first, ReadOnlySpan<int> second, int extra, int index)
        {
            if (index < first.Length)
                return first[index];
            index -= first.Length;
            return index < second.Length ? second[index] : extra;
        }

        // The exact product of up to seven permille factors.
        private static FixedUInt128 Product(ReadOnlySpan<int> factors)
        {
            ulong head = 1, tail = 1;
            for (int i = 0; i < factors.Length; i++)
            {
                if (i < 4)
                    head *= (ulong)factors[i];
                else
                    tail *= (ulong)factors[i];
            }

            return FixedUInt128.Multiply(head, tail);
        }

        // 1000^exponent for exponent 0..6.
        private static ulong Power(int exponent)
        {
            ulong value = 1;
            for (int i = 0; i < exponent; i++)
                value *= One;
            return value;
        }

        private static void CheckBase(long baseValue)
        {
            if (baseValue > MaxBase || baseValue < -MaxBase)
                throw new ArgumentOutOfRangeException(nameof(baseValue));
        }

        private static void CheckFactors(ReadOnlySpan<int> factors, string name)
        {
            for (int i = 0; i < factors.Length; i++)
                if (factors[i] < MinPermille || factors[i] > MaxPermille)
                    throw new ArgumentOutOfRangeException(name);
        }

        private static void CheckBound(int bound, string name)
        {
            if (bound != 0 && (bound < MinPermille || bound > MaxPermille))
                throw new ArgumentOutOfRangeException(name);
        }
    }
}
