using System;

namespace WarCore.Sim
{
    // Knuth D needs one extra normalization word, not a wider stored geometry value.
    internal static class GeometryWordDivision
    {
        private const ulong Radix = 1UL << 32;

        internal static int LeadingZeros(uint value)
        {
            if (value == 0)
                return 32;
            int count = 0;
            if ((value & 0xffff0000U) == 0)
            {
                count += 16;
                value <<= 16;
            }

            if ((value & 0xff000000U) == 0)
            {
                count += 8;
                value <<= 8;
            }

            if ((value & 0xf0000000U) == 0)
            {
                count += 4;
                value <<= 4;
            }

            if ((value & 0xc0000000U) == 0)
            {
                count += 2;
                value <<= 2;
            }

            if ((value & 0x80000000U) == 0)
                count++;
            return count;
        }

        internal static void Divide(ReadOnlySpan<uint> dividend, ReadOnlySpan<uint> divisor, Span<uint> quotient)
        {
            if (divisor.Length == 0 || divisor[divisor.Length - 1] == 0)
                throw new SimDivideByZeroException();
            if (dividend.Length > 9 || divisor.Length > 9 || quotient.Length < dividend.Length)
                throw new SimBoundaryException("Word division requires bounded input and output spans.");
            if (divisor.Length == 1)
            {
                ulong remainder = 0;
                for (int i = dividend.Length - 1; i >= 0; i--)
                {
                    ulong value = (remainder << 32) | dividend[i];
                    quotient[i] = (uint)(value / divisor[0]);
                    remainder = value % divisor[0];
                }

                return;
            }

            Span<uint> u = stackalloc uint[10];
            Span<uint> v = stackalloc uint[9];
            u.Clear();
            v.Clear();
            int shift = LeadingZeros(divisor[divisor.Length - 1]);
            Normalize(dividend, u, shift);
            Normalize(divisor, v, shift);
            int n = divisor.Length;
            ulong top = v[n - 1];
            for (int j = dividend.Length - n; j >= 0; j--)
            {
                ulong high = u[j + n];
                ulong low = u[j + n - 1];
                ulong estimate;
                ulong remainder;
                if (high == top)
                {
                    estimate = uint.MaxValue;
                    remainder = low + top;
                }
                else
                {
                    ulong pair = (high << 32) | low;
                    estimate = pair / top;
                    remainder = pair % top;
                }

                // Only shift a remainder below B; its 33rd bit otherwise already proves the bound.
                while (remainder < Radix && estimate * v[n - 2] > (remainder << 32) + u[j + n - 2])
                {
                    estimate--;
                    remainder += top;
                }

                ulong borrow = 0;
                for (int i = 0; i < n; i++)
                {
                    ulong product = estimate * v[i] + borrow;
                    uint bottom = unchecked((uint)product);
                    uint prior = u[j + i];
                    u[j + i] = unchecked(prior - bottom);
                    borrow = (product >> 32) + (prior < bottom ? 1UL : 0UL);
                }

                bool underflow = u[j + n] < borrow;
                u[j + n] = unchecked((uint)((ulong)u[j + n] - borrow));
                if (underflow)
                {
                    estimate--;
                    ulong carry = 0;
                    for (int i = 0; i < n; i++)
                    {
                        ulong sum = (ulong)u[j + i] + v[i] + carry;
                        u[j + i] = unchecked((uint)sum);
                        carry = sum >> 32;
                    }

                    // Add-back cancels the borrowed B^(n+1); its top carry is deliberately discarded.
                    u[j + n] = unchecked(u[j + n] + (uint)carry);
                }

                quotient[j] = (uint)estimate;
            }
        }

        private static void Normalize(ReadOnlySpan<uint> input, Span<uint> output, int shift)
        {
            ulong carry = 0;
            for (int i = 0; i < input.Length; i++)
            {
                ulong value = ((ulong)input[i] << shift) | carry;
                output[i] = unchecked((uint)value);
                carry = value >> 32;
            }

            if (carry != 0)
                output[input.Length] = (uint)carry;
        }
    }
}
