using System;

namespace WarCore.Tests.Oracle
{
    internal static class FixedMathOracle
    {
        internal const long Scale = 65536;

        internal static long Multiply(long a, long b)
        {
            WideInt product = WideInt.From(a) * WideInt.From(b);
            WideInt magnitude = product.Sign < 0 ? -product : product;
            // This guard freezes the world-range contract, which is stricter than result overflow.
            if (magnitude.CompareTo(WideInt.From(long.MaxValue)) > 0)
                throw new OverflowException();
            return (product / WideInt.From(Scale)).ToLong();
        }

        internal static long Divide(long a, long b) => (WideInt.From(a) * WideInt.From(Scale) / WideInt.From(b)).ToLong();
        internal static long Sqrt(long value) => (WideInt.From(value) * WideInt.From(Scale)).Sqrt().ToLong();
        internal static long Midpoint(long a, long b) => ((WideInt.From(a) + WideInt.From(b)) / WideInt.From(2)).ToLong();
        private static WideInt Product(long a, long b) => WideInt.From(a) * WideInt.From(b);

        internal static SimWide Dot(long[] a, long[] b) => new SimWide(Product(a[0], b[0]) + Product(a[1], b[1]) + Product(a[2], b[2]));

        internal static SimWideVec3 Cross(long[] a, long[] b) => new SimWideVec3(
            Product(a[1], b[2]) - Product(a[2], b[1]),
            Product(a[2], b[0]) - Product(a[0], b[2]),
            Product(a[0], b[1]) - Product(a[1], b[0]));
    }
}
