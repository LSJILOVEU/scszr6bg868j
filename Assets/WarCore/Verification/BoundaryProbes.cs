using System;
using System.Globalization;
using System.Reflection;
using WarCore.Sim;

namespace WarCore.Verification
{
    internal static class BoundaryProbes
    {
        private static SimFix Raw(long value) => SimFix.FromRaw(value);
        internal static void Run(Report report)
        {
            foreach (string name in new[] { "raw_integer_divzero", "raw_long_min_div_neg1", "raw_remainder_zero", "raw_min_remainder_neg1",
                "raw_negate_min", "raw_abs_min", "raw_narrow_int", "raw_narrow_long", "raw_multiply_add", "raw_fixed_convert", "raw_checked_multiply" })
                report.Line("probe." + name, RawRuntimeProbes.Run(name));
            foreach (int bits in new[] { 0, 1, 63, 64, 65, -1 })
                foreach (string direction in new[] { "left", "right" })
                {
                    string name = "raw_shift_" + direction + "_" + bits;
                    report.Line("probe." + name, RawRuntimeProbes.Run(name));
                }
            Probe(report, "integer_divzero", () => InvokeWide("DivideToUInt64", 1, 0, 0UL).ToString());
            Probe(report, "fix_divzero", () => (Raw(1) / Raw(0)).Raw.ToString());
            Probe(report, "fix_min_div_neg1", () => (Raw(long.MinValue) / Raw(-1)).Raw.ToString());
            Probe(report, "ratio_min_div_neg1", () => SimFix.Ratio(long.MinValue, -1).Raw.ToString());
            Probe(report, "multiply_overflow", () => (Raw(long.MaxValue) * Raw(2)).Raw.ToString());
            Probe(report, "add_overflow", () => (Raw(long.MaxValue) + Raw(1)).Raw.ToString());
            Probe(report, "subtract_overflow", () => (Raw(long.MinValue) - Raw(1)).Raw.ToString());
            Probe(report, "negate_overflow", () => (-Raw(long.MinValue)).Raw.ToString());
            Probe(report, "convert_overflow", () => SimFix.FromInt(long.MaxValue).Raw.ToString());
            Probe(report, "wide_convert_overflow", () => SimVec3.Dot(MathVectors.Vector(new[] { long.MaxValue, 0L, 0L }),
                MathVectors.Vector(new[] { long.MaxValue, 0L, 0L })).ToSimFix().Raw.ToString());
            Probe(report, "posture_min_one", () => PostureScale.Apply(Raw(long.MinValue), SimFix.FromInt(1)).Raw.ToString());
            Probe(report, "posture_max_one", () => PostureScale.Apply(Raw(long.MaxValue), SimFix.FromInt(1)).Raw.ToString());
            Probe(report, "posture_min_zero", () => PostureScale.Apply(Raw(long.MinValue), default).Raw.ToString());
            Probe(report, "posture_truncate", () => PostureScale.Apply(Raw(-3), SimFix.Ratio(1, 2)).Raw.ToString());
            Probe(report, "posture_min_two", () => PostureScale.Apply(Raw(long.MinValue), SimFix.FromInt(2)).Raw.ToString());
            Probe(report, "posture_max_two", () => PostureScale.Apply(Raw(long.MaxValue), SimFix.FromInt(2)).Raw.ToString());
            Probe(report, "tick_add_overflow", () => typeof(SimFix).Assembly.GetType("WarCore.Sim.SimChecked", true)
                .GetMethod("Add", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { 1L, long.MaxValue }).ToString());
            foreach (int bits in new[] { 0, 1, 63, 64, 65, 127, 128, 129, -1 })
            {
                int shift = bits;
                Probe(report, "shift_right_" + bits, () =>
                {
                    object result = InvokeWide("ShiftRight", 1, 0x8000000000000001UL, shift);
                    Type type = result.GetType();
                    return ((ulong)type.GetField("Low", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(result)).ToString("X16", CultureInfo.InvariantCulture)
                        + "," + ((ulong)type.GetField("High", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(result)).ToString("X16", CultureInfo.InvariantCulture);
                });
            }
            ExplicitScalarProbes(report);
        }
        private static void ExplicitScalarProbes(Report report)
        {
            Probe(report, "signed_div_min_neg1", () => SimInteger.Divide(long.MinValue, -1).ToString());
            Probe(report, "signed_rem_min_neg1", () => SimInteger.Remainder(long.MinValue, -1).ToString());
            Probe(report, "signed_rem_zero", () => SimInteger.Remainder(long.MinValue, 0).ToString());
            Probe(report, "signed_abs_min", () => SimInteger.Abs(long.MinValue).ToString());
            Probe(report, "unsigned_abs_min", () => FixedInt128.Abs(long.MinValue).ToString());
            Probe(report, "narrow_int_min", () => SimInteger.ToInt32(long.MinValue).ToString());
            Probe(report, "narrow_int_max", () => SimInteger.ToInt32(long.MaxValue).ToString());
            Probe(report, "bits_long_min", () => SimInteger.FromBits(1UL << 63).ToString());
            Probe(report, "bits_long_max", () => SimInteger.FromBits(ulong.MaxValue).ToString());
            Probe(report, "signed_multiply_add", () => SimInteger.Add(SimInteger.Multiply(long.MaxValue, 2), 1).ToString());
            Probe(report, "fixed_convert_min_valid", () => SimFix.FromInt(long.MinValue / 65536).Raw.ToString());
            Probe(report, "fixed_convert_min_invalid", () => SimFix.FromInt(long.MinValue / 65536 - 1).Raw.ToString());
            Probe(report, "wide_add_carry", () => (new FixedUInt128(ulong.MaxValue, ulong.MaxValue) + new FixedUInt128(1)).High.ToString());
            Probe(report, "wide_sub_borrow", () => (new FixedUInt128() - new FixedUInt128(1)).High.ToString());
            Probe(report, "leading_zeros_zero", () => GeometryWordDivision.LeadingZeros(0).ToString());
            Probe(report, "geometry_divzero", () => (GeometryInt288.From(1) / GeometryInt288.From(0)).ToLong().ToString());
            Probe(report, "geometry192_divzero", () => new GeometryUInt192(1).Divide(default).Low.ToString());
            Probe(report, "geometry192_scale_overflow", () => new GeometryUInt192(0, 0, ulong.MaxValue).TimesFour().High.ToString());
            Probe(report, "geometry192_sqrt_max", () => GeometryUInt192.Sqrt64(ulong.MaxValue).ToString());
            Probe(report, "sqrt_negative", () => SimMath.Sqrt(Raw(-1)).Raw.ToString());
            Probe(report, "atan2_zero", () => SimMath.Atan2(default, default).Raw.ToString());
            Probe(report, "odd_height", () => new Collider(Raw(1), Raw(5)).Height.Raw.ToString());
            foreach (int bits in new[] { 0, 1, 63, 64, 65, -1 })
            {
                int shift = bits;
                Probe(report, "signed_shift_left_" + bits, () => SimInteger.ShiftLeft(-3, shift).ToString());
                Probe(report, "signed_shift_right_" + bits, () => SimInteger.ShiftRight(-3, shift).ToString());
            }
        }

        private static object InvokeWide(string method, ulong low, ulong high, object argument)
        {
            Type type = typeof(SimFix).Assembly.GetType("WarCore.Sim.FixedUInt128", true);
            object value = Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { low, high }, CultureInfo.InvariantCulture);
            return type.GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(value, new[] { argument });
        }
        private static void Probe(Report report, string name, Func<string> operation)
        {
            string result;
            try
            {
                result = "value:" + operation();
            }
            catch (Exception error)
            {
                while (error is TargetInvocationException && error.InnerException != null)
                    error = error.InnerException;
                result = "exception:" + error.GetType().FullName;
            }
            report.Line("probe." + name, result);
        }
    }
}
