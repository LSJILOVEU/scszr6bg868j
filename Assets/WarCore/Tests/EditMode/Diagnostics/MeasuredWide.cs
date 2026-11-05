using WarCore.Sim;

namespace WarCore.Tests.Diagnostics
{
    // This adapter calls product arithmetic. It does not implement a second integer algorithm.
    // Histograms count API operands/results; operations inside product Sqrt remain inside its time.
    internal readonly struct MeasuredWide
    {
        private readonly GeometryInt288 value;
        private readonly QueryTrace trace;
        private MeasuredWide(GeometryInt288 value, QueryTrace trace)
        {
            this.value = value;
            this.trace = trace;
        }

        internal bool IsZero => value.IsZero;
        internal int Sign => value.Sign;
        internal static MeasuredWide From(long value, QueryTrace trace)
        {
            GeometryInt288 result;
            using (trace.Measure(QueryStage.WideConvert))
                result = GeometryInt288.From(value);
            trace.Result(QueryStage.WideConvert, result);
            return new MeasuredWide(result, trace);
        }

        internal static MeasuredWide FromUnsigned(ulong value, QueryTrace trace)
        {
            GeometryInt288 result;
            using (trace.Measure(QueryStage.WideConvert))
                result = GeometryInt288.FromUnsigned(value);
            trace.Result(QueryStage.WideConvert, result);
            return new MeasuredWide(result, trace);
        }

        internal static MeasuredWide From128(FixedUInt128 value, QueryTrace trace)
        {
            GeometryInt288 result;
            using (trace.Measure(QueryStage.WideConvert))
                result = GeometryInt288.From128(value);
            trace.Result(QueryStage.WideConvert, result);
            return new MeasuredWide(result, trace);
        }

        public static MeasuredWide operator -(MeasuredWide a)
        {
            if (a.trace == null)
                return default;
            GeometryInt288 result;
            using (a.trace.Measure(QueryStage.WideAdd))
                result = -a.value;
            a.trace.Operand(QueryStage.WideAdd, a.value);
            a.trace.Result(QueryStage.WideAdd, result);
            return new MeasuredWide(result, a.trace);
        }

        public static MeasuredWide operator +(MeasuredWide a, MeasuredWide b) => Binary(a, b, QueryStage.WideAdd, false);
        public static MeasuredWide operator -(MeasuredWide a, MeasuredWide b) => Binary(a, b, QueryStage.WideAdd, true);
        public static MeasuredWide operator *(MeasuredWide a, MeasuredWide b) => Binary(a, b, QueryStage.WideMultiply, false);
        public static MeasuredWide operator /(MeasuredWide a, MeasuredWide b) => Binary(a, b, QueryStage.WideDivide, false);
        private static MeasuredWide Binary(MeasuredWide a, MeasuredWide b, QueryStage stage, bool subtract)
        {
            QueryTrace trace = a.trace ?? b.trace;
            GeometryInt288 result;
            using (trace.Measure(stage))
            {
                if (stage == QueryStage.WideMultiply)
                    result = a.value * b.value;
                else if (stage == QueryStage.WideDivide)
                    result = a.value / b.value;
                else
                    result = subtract ? a.value - b.value : a.value + b.value;
            }

            trace.Operand(stage, a.value);
            trace.Operand(stage, b.value);
            trace.Result(stage, result);
            return new MeasuredWide(result, trace);
        }

        internal int CompareTo(MeasuredWide other)
        {
            QueryTrace owner = trace ?? other.trace;
            int result;
            using (owner.Measure(QueryStage.WideCompare))
                result = value.CompareTo(other.value);
            owner.Operand(QueryStage.WideCompare, value);
            owner.Operand(QueryStage.WideCompare, other.value);
            return result;
        }

        internal MeasuredWide Sqrt()
        {
            GeometryInt288 result;
            using (trace.Measure(QueryStage.Sqrt))
                result = value.Sqrt();
            trace.Operand(QueryStage.Sqrt, value);
            trace.Result(QueryStage.Sqrt, result);
            return new MeasuredWide(result, trace);
        }

        internal long ToLong()
        {
            long result;
            using (trace.Measure(QueryStage.WideConvert))
                result = value.ToLong();
            trace.Operand(QueryStage.WideConvert, value);
            return result;
        }
    }
}
