using System;

namespace WarCore.Sim
{
    // Ray radicands need at most 158 bits. Three limbs avoid general 288-bit Newton iterates.
    internal readonly struct GeometryUInt192
    {
        internal readonly ulong Low;
        internal readonly ulong Middle;
        internal readonly ulong High;

        internal GeometryUInt192(ulong low, ulong middle = 0, ulong high = 0)
        {
            Low = low;
            Middle = middle;
            High = high;
        }

        internal static GeometryUInt192 Scale32(FixedUInt128 value)
            => new GeometryUInt192(value.Low << 32, (value.Low >> 32) | (value.High << 32), value.High >> 32);

        internal GeometryUInt192 TimesFour()
        {
            // Keep the range factor wide; 4*range^2 has only one spare bit in a ulong.
            if ((High >> 62) != 0)
                throw new SimBoundaryException("Geometry scaling exceeds 192 bits.");
            return new GeometryUInt192(Low << 2, (Middle << 2) | (Low >> 62), (High << 2) | (Middle >> 62));
        }

        internal static GeometryUInt192 Multiply(FixedUInt128 value, ulong factor)
        {
            FixedUInt128 lo = FixedUInt128.Multiply(value.Low, factor);
            FixedUInt128 hi = FixedUInt128.Multiply(value.High, factor);
            ulong middle = unchecked(lo.High + hi.Low);
            return new GeometryUInt192(lo.Low, middle, hi.High + (middle < lo.High ? 1UL : 0UL));
        }

        internal static GeometryUInt192 Square(FixedUInt128 value)
        {
            if (value.High > uint.MaxValue)
                throw new SimBoundaryException("Geometry square exceeds 192 bits.");
            FixedUInt128 lo = FixedUInt128.Multiply(value.Low, value.Low);
            FixedUInt128 cross = FixedUInt128.Multiply(value.Low, value.High);
            ulong middle = unchecked(lo.High + cross.Low);
            ulong carry = middle < lo.High ? 1UL : 0UL;
            ulong next = unchecked(middle + cross.Low);
            carry += next < middle ? 1UL : 0UL;
            ulong high = value.High * value.High + cross.High * 2 + carry;
            return new GeometryUInt192(lo.Low, next, high);
        }

        internal static GeometryUInt192 Multiply(FixedUInt128 a, FixedUInt128 b)
        {
            if (a.High > uint.MaxValue || b.High > uint.MaxValue)
                throw new SimBoundaryException("Geometry factors exceed 96 bits.");
            FixedUInt128 lo = FixedUInt128.Multiply(a.Low, b.Low);
            FixedUInt128 x = FixedUInt128.Multiply(a.Low, b.High);
            FixedUInt128 y = FixedUInt128.Multiply(a.High, b.Low);
            ulong middle = unchecked(lo.High + x.Low);
            ulong carry = middle < lo.High ? 1UL : 0UL;
            ulong next = unchecked(middle + y.Low);
            carry += next < middle ? 1UL : 0UL;
            return new GeometryUInt192(lo.Low, next, a.High * b.High + x.High + y.High + carry);
        }

        internal int CompareTo(GeometryUInt192 other)
        {
            if (High != other.High)
                return High < other.High ? -1 : 1;
            if (Middle != other.Middle)
                return Middle < other.Middle ? -1 : 1;
            return Low == other.Low ? 0 : Low < other.Low ? -1 : 1;
        }

        internal static int Bits(ulong value)
        {
            uint hi = (uint)(value >> 32);
            return hi == 0 ? 32 - GeometryWordDivision.LeadingZeros(unchecked((uint)value))
                : 64 - GeometryWordDivision.LeadingZeros(hi);
        }

        internal static ulong Sqrt64(ulong value)
        {
            if (value == 0)
                return 0;
            ulong root = 1UL << ((Bits(value) + 1) / 2);
            while (true)
            {
                ulong next = (root + value / root) >> 1;
                if (next >= root)
                    return root;
                root = next;
            }
        }

        private ulong ShiftTo64(int shift)
        {
            if (shift >= 128)
                return High >> (shift - 128);
            if (shift >= 64)
                return shift == 64 ? Middle : (Middle >> (shift - 64)) | (High << (128 - shift));
            return shift == 0 ? Low : (Low >> shift) | (Middle << (64 - shift));
        }

        internal FixedUInt128 Sqrt()
        {
            if ((High | Middle) == 0)
                return new FixedUInt128(Sqrt64(Low));
            int bits = High == 0 ? 64 + Bits(Middle) : 128 + Bits(High);
            int shift = (bits - 61) & ~1;
            // A 62-bit prefix gives an upper estimate within one prefix-root unit.
            // Exact Newton correction then converges without any floating-point approximation.
            ulong prefix = Sqrt64(ShiftTo64(shift)) + 1;
            int half = shift / 2;
            FixedUInt128 root = half >= 64 ? new FixedUInt128(0, prefix << (half - 64))
                : new FixedUInt128(prefix << half, half == 0 ? 0 : prefix >> (64 - half));
            while (true)
            {
                GeometryUInt192 quotient = Divide(root);
                var next = (root + new FixedUInt128(quotient.Low, quotient.Middle)).ShiftRight(1);
                if (next.CompareTo(root) >= 0)
                    return root;
                root = next;
            }
        }

        internal GeometryUInt192 Divide(FixedUInt128 divisor)
        {
            if (divisor.IsZero)
                throw new SimBoundaryException("Geometry divisor must be positive.");
            if ((High | Middle | divisor.High) == 0)
                return new GeometryUInt192(Low / divisor.Low);
            Span<uint> a = stackalloc uint[6];
            Span<uint> b = stackalloc uint[4];
            Span<uint> q = stackalloc uint[6];
            a[0] = unchecked((uint)Low);
            a[1] = (uint)(Low >> 32);
            a[2] = unchecked((uint)Middle);
            a[3] = (uint)(Middle >> 32);
            a[4] = unchecked((uint)High);
            a[5] = (uint)(High >> 32);
            for (int i = 0; i < 4; i++)
                b[i] = divisor.Word(i);
            int na = 6;
            int nb = 4;
            while (na > 0 && a[na - 1] == 0)
                na--;
            while (nb > 1 && b[nb - 1] == 0)
                nb--;
            if (na < nb)
                return default;
            q.Clear();
            GeometryWordDivision.Divide(a.Slice(0, na), b.Slice(0, nb), q);
            return new GeometryUInt192((ulong)q[0] | ((ulong)q[1] << 32),
                (ulong)q[2] | ((ulong)q[3] << 32), (ulong)q[4] | ((ulong)q[5] << 32));
        }
    }
}
