using System;
using System.Runtime.CompilerServices;

namespace WarCore.Verification
{
    public static class RawRuntimeProbes
    {
        // Every raw operation runs in its own player child: native faults must leave a recorded exit status.
        public static Func<string, string> IsolatedExecutor { get; set; }
        public static string Evaluate(string name)
        {
            switch (name)
            {
                case "raw_integer_divzero":
                    return StableDigest.Capture(() => Divide(1, 0));
                case "raw_long_min_div_neg1":
                    return StableDigest.Capture(() => Divide(long.MinValue, -1));
                case "raw_remainder_zero":
                    return StableDigest.Capture(() => Remainder(1, 0));
                case "raw_min_remainder_neg1":
                    return StableDigest.Capture(() => Remainder(long.MinValue, -1));
                case "raw_negate_min":
                    return StableDigest.Capture(() => Negate(long.MinValue));
                case "raw_abs_min":
                    return StableDigest.Capture(() => Abs(long.MinValue));
                case "raw_narrow_int":
                    return StableDigest.Capture(() => Narrow(long.MaxValue));
                case "raw_narrow_long":
                    return StableDigest.Capture(() => NarrowLong(ulong.MaxValue));
                case "raw_multiply_add":
                    return StableDigest.Capture(() => MultiplyAdd(long.MaxValue, 2, 1));
                case "raw_fixed_convert":
                    return StableDigest.Capture(() => MultiplyAdd(long.MaxValue, 65536, 0));
                case "raw_checked_multiply":
                    return StableDigest.Capture(() => CheckedMultiply(long.MinValue / 65536, 65536));
                default:
                    string[] parts = name.Split('_');
                    if (parts.Length != 4 || parts[0] != "raw" || parts[1] != "shift")
                        throw new ArgumentException("Unknown raw probe " + name);
                    int count = int.Parse(parts[3], System.Globalization.CultureInfo.InvariantCulture);
                    return StableDigest.Capture(() => parts[2] == "left" ? ShiftLeft(-3, count) : ShiftRight(-3, count));
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static long Divide(long a, long b) => a / b;
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static long Remainder(long a, long b) => a % b;
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static long Negate(long value) => unchecked(-value);
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static long Abs(long value) => Math.Abs(value);
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static long Narrow(long value) => unchecked((int)value);
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static long NarrowLong(ulong value) => unchecked((long)value);
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static long MultiplyAdd(long a, long b, long c) => unchecked(a * b + c);
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static long CheckedMultiply(long a, long b) => checked(a * b);
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static long ShiftLeft(long value, int count) => value << count;
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static long ShiftRight(long value, int count) => value >> count;
        internal static string Run(string name) => IsolatedExecutor == null ? Evaluate(name) : IsolatedExecutor(name);
    }
}
