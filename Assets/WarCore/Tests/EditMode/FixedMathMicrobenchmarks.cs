using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class FixedMathMicrobenchmarks
    {
        private const int InputCount = 1024;
        private const int Repetitions = 8;
        private const int WarmupBatches = 12;
        private const int SampleCount = 101;
        private long sink;

        private enum Operation
        {
            Multiply,
            Divide,
            Sqrt,
            Atan2
        }

        private readonly struct Input
        {
            internal readonly SimFix A;
            internal readonly SimFix B;

            internal Input(long a, long b)
            {
                A = SimFix.FromRaw(a);
                B = SimFix.FromRaw(b);
            }
        }

        private static Input[] Samples(Operation operation)
        {
            var input = new A1MathInputs(0xA11CE100UL + (ulong)operation);
            var values = new Input[InputCount];
            for (int i = 0; i < values.Length; i++)
            {
                int group = i / 256;
                long a;
                long b;
                switch (operation)
                {
                    case Operation.Multiply:
                        if (group < 2)
                        {
                            a = input.Bits(group == 0 ? 17 : 30);
                            b = input.Bits(group == 0 ? 17 : 30);
                        }
                        else if (group == 2)
                        {
                            int bits = 33 + (int)(input.Next() % 30);
                            a = input.Bits(bits);
                            b = input.Bits(63 - bits);
                        }
                        else
                        {
                            b = 1 + (long)(input.Next() & 65535);
                            a = long.MaxValue / b;
                            if ((i & 1) != 0)
                                a = -a;
                            if ((i & 2) != 0)
                                b = -b;
                        }

                        break;
                    case Operation.Divide:
                        if (group == 0)
                        {
                            a = input.Bits(31);
                            b = input.Bits(31) | 1;
                        }
                        else if (group == 1)
                        {
                            a = input.Long();
                            b = 65537 + (long)(input.Next() & uint.MaxValue);
                            if ((i & 1) != 0)
                                b = -b;
                        }
                        else if (group == 2)
                        {
                            a = input.Long();
                            b = long.MinValue + (long)(input.Next() & 65535);
                        }
                        else
                        {
                            a = input.Bits(17);
                            b = input.Long() | (1L << 32);
                        }

                        break;
                    case Operation.Sqrt:
                        b = 0;
                        if (group < 2)
                            a = (long)(input.Next() & ((1UL << (group == 0 ? 16 : 30)) - 1));
                        else if (group == 2)
                            a = (long)(input.Next() & long.MaxValue) | (1L << 48);
                        else
                        {
                            long root = 1 + (long)(input.Next() & ((1UL << 23) - 1));
                            a = root * root * 65536 + i % 3 - 1;
                        }

                        break;
                    default:
                        if (group == 0)
                        {
                            a = input.Bits(30);
                            b = i % 3 == 0 ? 0 : a;
                            if (i % 3 == 1)
                                a = 0;
                        }
                        else if (group == 1)
                        {
                            a = input.Bits(30);
                            b = input.Bits(30);
                        }
                        else if (group == 2)
                        {
                            a = input.Long();
                            b = input.Long();
                        }
                        else
                        {
                            a = (i & 1) == 0 ? long.MinValue + i : 1;
                            b = long.MaxValue - i;
                            if ((i & 2) != 0)
                                b = -b;
                        }

                        break;
                }

                values[i] = new Input(a, b);
            }

            return values;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static long RunBatch(Operation operation, Input[] values)
        {
            long checksum = 0;
            // Dispatch is outside the loops so the reported cost does not include a delegate per call.
            switch (operation)
            {
                case Operation.Multiply:
                    for (int repeat = 0; repeat < Repetitions; repeat++)
                        for (int i = 0; i < values.Length; i++)
                            checksum = unchecked(checksum + (values[i].A * values[i].B).Raw);
                    break;
                case Operation.Divide:
                    for (int repeat = 0; repeat < Repetitions; repeat++)
                        for (int i = 0; i < values.Length; i++)
                            checksum = unchecked(checksum + (values[i].A / values[i].B).Raw);
                    break;
                case Operation.Sqrt:
                    for (int repeat = 0; repeat < Repetitions; repeat++)
                        for (int i = 0; i < values.Length; i++)
                            checksum = unchecked(checksum + SimMath.Sqrt(values[i].A).Raw);
                    break;
                default:
                    for (int repeat = 0; repeat < Repetitions; repeat++)
                        for (int i = 0; i < values.Length; i++)
                            checksum = unchecked(checksum + SimMath.Atan2(values[i].A, values[i].B).Raw);
                    break;
            }

            return checksum;
        }

        [Test]
        public void ReportBatchMeanMedianAndP95WithoutTimingAssertions()
        {
            TestContext.WriteLine("A1 microbench: Stopwatch.Frequency={0}; per operation 1024 fixed inputs, seed=0xA11CE100+operation; 12 warmup batches; 101 measured batches x 8192 calls (8 passes); nearest-rank p95 of batch means; ns/call includes loop, array reads and checksum; no overhead subtraction or timing assertions.", Stopwatch.Frequency);
            TestContext.WriteLine("A1 samples (256 each): multiply=small Q16/world coordinates/33-62 bit operand/near product guard; divide=31-bit/64-bit numerator/near long.MinValue divisor/tiny quotient; sqrt=16-bit/30-bit/48-63 bit/square neighbours; atan2=axes and diagonals/world coordinates/full long/extreme or tiny ratios; successful calls only, mixed signs where valid.");
            for (Operation operation = Operation.Multiply; operation <= Operation.Atan2; operation++)
            {
                Input[] values = Samples(operation);
                var elapsed = new long[SampleCount];
                for (int warmup = 0; warmup < WarmupBatches; warmup++)
                    sink = unchecked(sink + RunBatch(operation, values));
                Stopwatch.GetTimestamp();
                for (int sample = 0; sample < elapsed.Length; sample++)
                {
                    long start = Stopwatch.GetTimestamp();
                    long checksum = RunBatch(operation, values);
                    elapsed[sample] = Stopwatch.GetTimestamp() - start;
                    sink = unchecked(sink + checksum);
                }

                Array.Sort(elapsed);
                double scale = 1000000000.0 / Stopwatch.Frequency / (InputCount * Repetitions);
                double median = elapsed[SampleCount / 2] * scale;
                double p95 = elapsed[(SampleCount * 95 - 1) / 100] * scale;
                TestContext.WriteLine("A1 microbench {0}: median={1} ns/call, p95={2} ns/call", operation, median.ToString("F3", CultureInfo.InvariantCulture), p95.ToString("F3", CultureInfo.InvariantCulture));
                if (operation == Operation.Multiply)
                    TestContext.WriteLine("A1 multiply goal <= 50 ns: median {0}, p95 {1} (report only; this runtime and sample mix).", median <= 50 ? "met" : "missed", p95 <= 50 ? "met" : "missed");
            }

            TestContext.WriteLine("A1 microbench checksum={0}", sink);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static long RunVectorBatch(Input[] values)
        {
            long checksum = 0;
            for (int i = 0; i < 4096; i++)
            {
                Input input = values[256 + (i & 255)];
                var a = new SimVec3(input.A, input.B, SimFix.FromRaw(i));
                var b = new SimVec3(input.B, input.A, SimFix.FromRaw(-i));
                var cross = SimVec3.Cross(a, b);
                checksum = unchecked(checksum + SimVec3.Midpoint(a, b).X.Raw + SimVec3.Dot(a, b).ToSimFix().Raw);
                checksum = unchecked(checksum + a.LengthSquared.Sqrt().Raw + cross.X.ToSimFix().Raw + cross.Y.ToSimFix().Raw + cross.Z.ToSimFix().Raw);
                checksum = unchecked(checksum + SimFix.Ratio(input.A.Raw, 65537).Raw);
            }

            return checksum;
        }

        [Test]
        public void WarmScalarAndVectorPathsAllocateZero()
        {
            var windows = new AllocationWindow();
            do
            {
                var inputs = new Input[4][];
                for (Operation operation = Operation.Multiply; operation <= Operation.Atan2; operation++)
                {
                    inputs[(int)operation] = Samples(operation);
                    for (int warmup = 0; warmup < WarmupBatches; warmup++)
                        sink = unchecked(sink + RunBatch(operation, inputs[(int)operation]));
                }

                sink = unchecked(sink + RunVectorBatch(inputs[0]));
                var meter = new AllocationMeter();
                windows.Begin(meter);
                for (Operation operation = Operation.Multiply; operation <= Operation.Atan2; operation++)
                    sink = unchecked(sink + RunBatch(operation, inputs[(int)operation]));
                sink = unchecked(sink + RunVectorBatch(inputs[0]));
                AllocationSample allocated = windows.End();
                if (!windows.NeedsRetry)
                    AllocationAssertions.Zero(allocated);
                TestContext.WriteLine("A1 warm math: {0}; 32768 scalar calls + 4096 vector/Ratio iterations; checksum={1}", allocated.Describe(), sink);
            }
            while (windows.NeedsRetry);
        }
    }
}
