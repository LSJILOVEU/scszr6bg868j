using System;
using System.Runtime.InteropServices;

namespace WarCore.Sim
{
    // Sector endpoint determinants exceed 256 bits; nine base-2^32 words retain their exact sign.
    [StructLayout(LayoutKind.Sequential)]
    internal struct GeometryInt288 : IComparable<GeometryInt288>
    {
        private const int WordCount = 9;
        private uint w0;
        private uint w1;
        private uint w2;
        private uint w3;
        private uint w4;
        private uint w5;
        private uint w6;
        private uint w7;
        private uint w8;
        private int length;
        private bool negative;

        internal bool IsZero => length == 0;
        internal int Sign => IsZero ? 0 : negative ? -1 : 1;

        // Sequential uint fields provide inline storage without unsafe code or heap arrays.
        private static Span<uint> Words(ref GeometryInt288 value) => MemoryMarshal.CreateSpan(ref value.w0, WordCount);

        internal static GeometryInt288 From(long value)
        {
            GeometryInt288 result = FromUnsigned(FixedInt128.Abs(value));
            result.negative = value < 0;
            return result;
        }

        internal static GeometryInt288 FromUnsigned(ulong value)
        {
            return new GeometryInt288
            {
                w0 = unchecked((uint)value),
                w1 = (uint)(value >> 32),
                length = value == 0 ? 0 : value > uint.MaxValue ? 2 : 1
            };
        }

        internal static GeometryInt288 From128(FixedUInt128 value)
        {
            var result = new GeometryInt288
            {
                w0 = unchecked((uint)value.Low),
                w1 = (uint)(value.Low >> 32),
                w2 = unchecked((uint)value.High),
                w3 = (uint)(value.High >> 32),
                length = 4
            };
            result.Trim();
            return result;
        }

        internal FixedUInt128 Magnitude128()
        {
            if (length > 4)
                throw new SimBoundaryException("Geometry magnitude exceeds 128 bits.");
            return new FixedUInt128((ulong)w0 | ((ulong)w1 << 32), (ulong)w2 | ((ulong)w3 << 32));
        }

        private void Trim()
        {
            Span<uint> words = Words(ref this);
            while (length > 0 && words[length - 1] == 0)
                length--;
            if (IsZero)
                negative = false;
        }

        private static int MagnitudeCompare(ref GeometryInt288 a, ref GeometryInt288 b)
        {
            if (a.length != b.length)
                return a.length < b.length ? -1 : 1;
            Span<uint> x = Words(ref a);
            Span<uint> y = Words(ref b);
            for (int i = a.length - 1; i >= 0; i--)
                if (x[i] != y[i])
                    return x[i] < y[i] ? -1 : 1;
            return 0;
        }

        public int CompareTo(GeometryInt288 other)
        {
            if (Sign != other.Sign)
                return Sign < other.Sign ? -1 : 1;
            return Sign * MagnitudeCompare(ref this, ref other);
        }

        public static GeometryInt288 operator -(GeometryInt288 value)
        {
            value.negative = !value.negative && !value.IsZero;
            return value;
        }

        public static GeometryInt288 operator +(GeometryInt288 a, GeometryInt288 b)
        {
            if (a.IsZero)
                return b;
            if (b.IsZero)
                return a;
            if (MagnitudeCompare(ref a, ref b) < 0)
            {
                GeometryInt288 swap = a;
                a = b;
                b = swap;
            }

            var result = new GeometryInt288 { length = a.length, negative = a.negative };
            Span<uint> x = Words(ref a);
            Span<uint> y = Words(ref b);
            Span<uint> output = Words(ref result);
            ulong carry = 0;
            for (int i = 0; i < a.length; i++)
            {
                if (a.negative == b.negative)
                {
                    ulong sum = (ulong)x[i] + y[i] + carry;
                    output[i] = unchecked((uint)sum);
                    carry = sum >> 32;
                }
                else
                {
                    ulong sub = (ulong)y[i] + carry;
                    output[i] = unchecked((uint)((ulong)x[i] - sub));
                    carry = x[i] < sub ? 1UL : 0UL;
                }
            }

            if (carry != 0)
            {
                if (a.negative != b.negative || result.length == WordCount)
                    throw new SimOverflowException();
                output[result.length] = (uint)carry;
                result.length++;
            }

            result.Trim();
            return result;
        }

        public static GeometryInt288 operator -(GeometryInt288 a, GeometryInt288 b) => a + -b;

        public static GeometryInt288 operator *(GeometryInt288 a, GeometryInt288 b)
        {
            if (a.IsZero || b.IsZero)
                return default;
            var result = new GeometryInt288 { negative = a.negative != b.negative };
            Span<uint> x = Words(ref a);
            Span<uint> y = Words(ref b);
            Span<uint> output = Words(ref result);
            for (int i = 0; i < a.length; i++)
            {
                ulong carry = 0;
                for (int j = 0; j < b.length; j++)
                {
                    int k = i + j;
                    if (k >= WordCount)
                    {
                        if (x[i] != 0 && y[j] != 0 || carry != 0)
                            throw new SimOverflowException();
                        continue;
                    }

                    // (B-1)^2 + (B-1) + (B-1) = B^2-1, so the temporary fits ulong.
                    ulong product = (ulong)x[i] * y[j] + output[k] + carry;
                    output[k] = unchecked((uint)product);
                    carry = product >> 32;
                }

                if (carry != 0)
                {
                    if (i + b.length >= WordCount)
                        throw new SimOverflowException();
                    output[i + b.length] = (uint)carry;
                }
            }

            result.length = a.length + b.length;
            if (result.length > WordCount)
                result.length = WordCount;
            result.Trim();
            return result;
        }

        public static GeometryInt288 operator /(GeometryInt288 a, GeometryInt288 b)
        {
            if (b.IsZero)
                throw new SimDivideByZeroException();
            if (MagnitudeCompare(ref a, ref b) < 0)
                return default;
            var result = new GeometryInt288 { length = a.length - b.length + 1, negative = a.negative != b.negative };
            GeometryWordDivision.Divide(Words(ref a).Slice(0, a.length), Words(ref b).Slice(0, b.length), Words(ref result));
            result.Trim();
            return result;
        }

        private GeometryInt288 Half()
        {
            GeometryInt288 result = this;
            Span<uint> words = Words(ref result);
            uint carry = 0;
            for (int i = length - 1; i >= 0; i--)
            {
                uint word = words[i];
                words[i] = (word >> 1) | carry;
                carry = word << 31;
            }

            result.Trim();
            return result;
        }

        internal GeometryInt288 Sqrt()
        {
            if (negative)
                throw new ArgumentOutOfRangeException("value");
            if (IsZero)
                return default;
            Span<uint> words = Words(ref this);
            int bits = length * 32 - GeometryWordDivision.LeadingZeros(words[length - 1]);
            int rootBit = (bits + 1) / 2;
            if (length <= 2)
            {
                ulong value = ((ulong)w1 << 32) | w0;
                ulong estimate = 1UL << rootBit;
                while (true)
                {
                    ulong next = (estimate + value / estimate) >> 1;
                    if (next >= estimate)
                        return FromUnsigned(estimate);
                    estimate = next;
                }
            }

            var root = new GeometryInt288 { length = rootBit / 32 + 1 };
            Words(ref root)[rootBit / 32] = 1U << (rootBit % 32);
            // Starting above sqrt makes Newton descend; stopping before a rise gives floor(sqrt).
            // Both summands are below 2^145 even for a full 288-bit radicand.
            while (true)
            {
                GeometryInt288 next = (root + this / root).Half();
                if (next.CompareTo(root) >= 0)
                    return root;
                root = next;
            }
        }

        internal long ToLong()
        {
            if (length > 2)
                throw new SimOverflowException();
            return FixedInt128.ToLong(((ulong)w1 << 32) | w0, negative);
        }
    }
}
