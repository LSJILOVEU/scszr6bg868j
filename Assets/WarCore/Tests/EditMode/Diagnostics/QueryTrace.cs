using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using WarCore.Sim;

namespace WarCore.Tests.Diagnostics
{
    internal enum QueryStage
    {
        Validation,
        Broadphase,
        Prefilter,
        Precise,
        Sort,
        Write,
        Discriminant,
        Sqrt,
        WideAdd,
        WideMultiply,
        WideDivide,
        WideCompare,
        WideConvert,
        RootEligibility,
        RangeEligibility,
        Distance,
        Count
    }

    // Nested scopes report both inclusive and exclusive time so overlapping work is never added twice.
    internal sealed class QueryTrace
    {
        internal readonly long[] Inclusive = new long[(int)QueryStage.Count];
        internal readonly long[] Exclusive = new long[(int)QueryStage.Count];
        internal readonly long[] Calls = new long[(int)QueryStage.Count];
        internal readonly long[] OperandWidths = new long[(int)QueryStage.Count * 289];
        internal readonly long[] ResultWidths = new long[(int)QueryStage.Count * 289];
        private readonly int[] stages = new int[32];
        private readonly long[] starts = new long[32];
        private readonly long[] children = new long[32];
        private int depth;
        internal bool Enabled;
        internal bool DeepGeometry = true;

        internal readonly struct Scope : IDisposable
        {
            private readonly QueryTrace owner;
            internal Scope(QueryTrace owner)
            {
                this.owner = owner;
            }

            public void Dispose()
            {
                owner?.End();
            }
        }

        internal Scope Measure(QueryStage stage)
        {
            if (!Enabled)
                return default;
            stages[depth] = (int)stage;
            children[depth] = 0;
            starts[depth] = Stopwatch.GetTimestamp();
            depth++;
            Calls[(int)stage]++;
            return new Scope(this);
        }

        private void End()
        {
            long end = Stopwatch.GetTimestamp();
            depth--;
            int stage = stages[depth];
            long elapsed = end - starts[depth];
            Inclusive[stage] += elapsed;
            Exclusive[stage] += elapsed - children[depth];
            if (depth > 0)
                children[depth - 1] += elapsed;
        }

        internal void Reset()
        {
            if (depth != 0)
                throw new InvalidOperationException("Unbalanced diagnostic scopes.");
            Array.Clear(Inclusive, 0, Inclusive.Length);
            Array.Clear(Exclusive, 0, Exclusive.Length);
            Array.Clear(Calls, 0, Calls.Length);
            Array.Clear(OperandWidths, 0, OperandWidths.Length);
            Array.Clear(ResultWidths, 0, ResultWidths.Length);
        }

        internal void Add(QueryTrace other)
        {
            for (int i = 0; i < Calls.Length; i++)
            {
                Calls[i] += other.Calls[i];
                Inclusive[i] += other.Inclusive[i];
                Exclusive[i] += other.Exclusive[i];
            }

            for (int i = 0; i < OperandWidths.Length; i++)
            {
                OperandWidths[i] += other.OperandWidths[i];
                ResultWidths[i] += other.ResultWidths[i];
            }
        }

        // The nine sequential magnitude words are checked by the existing layout regression tests.
        internal static int BitWidth(GeometryInt288 value)
        {
            ReadOnlySpan<uint> words = MemoryMarshal.Cast<GeometryInt288, uint>(MemoryMarshal.CreateReadOnlySpan(ref value, 1));
            for (int i = 8; i >= 0; i--)
                if (words[i] != 0)
                    return i * 32 + 32 - GeometryWordDivision.LeadingZeros(words[i]);
            return 0;
        }

        internal void Operand(QueryStage stage, GeometryInt288 value)
        {
            if (Enabled)
                OperandWidths[(int)stage * 289 + BitWidth(value)]++;
        }

        internal void Result(QueryStage stage, GeometryInt288 value)
        {
            if (Enabled)
                ResultWidths[(int)stage * 289 + BitWidth(value)]++;
        }
    }
}
