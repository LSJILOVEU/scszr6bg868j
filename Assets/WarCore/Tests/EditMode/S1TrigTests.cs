using System;
using System.Globalization;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using WarCore.Sim;
using WarCore.Tests.Oracle;

namespace WarCore.Tests
{
    public sealed class S1TrigTests
    {
        private readonly Q32TrigReference reference = new Q32TrigReference();
        private long sink;

        [Test]
        public void EveryPhaseMatchesQ32AndHasErrorBelowPointSixRawUnits()
        {
            double maxSin = 0;
            double maxCos = 0;
            for (int raw = 0; raw < 65536; raw++)
            {
                long sine = SimMath.Sin(SimFix.FromRaw(raw)).Raw;
                long cosine = SimMath.Cos(SimFix.FromRaw(raw)).Raw;
                Assert.That(sine, Is.EqualTo(reference.Sin(raw)), "sin phase=" + raw);
                Assert.That(cosine, Is.EqualTo(reference.Cos(raw)), "cos phase=" + raw);
                double radians = raw / 65536.0 * 2 * Math.PI;
                maxSin = Math.Max(maxSin, Math.Abs(sine - Math.Sin(radians) * 65536));
                maxCos = Math.Max(maxCos, Math.Abs(cosine - Math.Cos(radians) * 65536));
            }

            TestContext.WriteLine("S1 full phase: 65536 sin + 65536 cos exact Q32 comparisons; max error sin={0}, cos={1} raw units; required < 0.6.",
                maxSin.ToString("F9", CultureInfo.InvariantCulture), maxCos.ToString("F9", CultureInfo.InvariantCulture));
            Assert.That(maxSin, Is.LessThan(0.6));
            Assert.That(maxCos, Is.LessThan(0.6));
        }

        [Test]
        public void Atan2MatchesQ32AcrossAllBoundaryPairs()
        {
            long[] edges = A1MathInputs.Edges();
            int executed = 0;
            int mismatches = 0;
            foreach (long y in edges)
                foreach (long x in edges)
                {
                    long expected = reference.Atan2(y, x);
                    long actual = SimMath.Atan2(SimFix.FromRaw(y), SimFix.FromRaw(x)).Raw;
                    if (actual != expected)
                        mismatches++;
                    executed++;
                }

            // The Cartesian product includes both sides of every normalization shift and long.MinValue.
            TestContext.WriteLine("S1 atan2 boundary Cartesian product: edges={0}, executed={1}, mismatches={2}.", edges.Length, executed, mismatches);
            Assert.That(executed, Is.EqualTo(149769));
            Assert.That(mismatches, Is.Zero);
        }

        [Test]
        public void Atan2AcrossMagnitudesHasErrorBelowPointSixRawUnits()
        {
            const ulong seed = 0x51320001UL;
            const int count = 300000;
            var input = new A1MathInputs(seed);
            long[] edges = A1MathInputs.Edges();
            double maxError = 0;
            long worstX = 0;
            long worstY = 0;
            for (int i = 0; i < count; i++)
            {
                long y = input.Sample(edges);
                long x = input.Sample(edges);
                long actual = SimMath.Atan2(SimFix.FromRaw(y), SimFix.FromRaw(x)).Raw;
                double expected = Math.Atan2(y, x) / (2 * Math.PI) * 65536;
                double error = Math.Abs(actual - expected);
                if (error > maxError)
                {
                    maxError = error;
                    worstX = x;
                    worstY = y;
                }
            }

            TestContext.WriteLine("S1 atan2 accuracy: seed=0x{0:X8}, random={1}, max error={2} raw units at y={3}, x={4}; required < 0.6.",
                seed, count, maxError.ToString("F9", CultureInfo.InvariantCulture), worstY, worstX);
            Assert.That(maxError, Is.LessThan(0.6));
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static long TrigBatch()
        {
            long checksum = 0;
            for (int i = 0; i < 65536; i++)
            {
                checksum += SimMath.Sin(SimFix.FromRaw(i)).Raw;
                checksum += SimMath.Cos(SimFix.FromRaw(i)).Raw;
                checksum += SimMath.Atan2(SimFix.FromRaw(long.MinValue + i), SimFix.FromRaw(long.MaxValue - i)).Raw;
            }

            return checksum;
        }

        [Test]
        public void WarmSinCosAndAtan2AllocateZero()
        {
            var windows = new AllocationWindow();
            do
            {
                sink = TrigBatch();
                var meter = new AllocationMeter();
                windows.Begin(meter);
                sink += TrigBatch();
                AllocationSample allocated = windows.End();
                TestContext.WriteLine("S1 warm trig: 196608 calls, {0}; checksum={1}.", allocated.Describe(), sink);
                if (!windows.NeedsRetry)
                    AllocationAssertions.Zero(allocated);
            }
            while (windows.NeedsRetry);
        }
    }
}
