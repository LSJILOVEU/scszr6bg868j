using System;
using System.Diagnostics;
using System.Globalization;
using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    // Per-call cost of the oriented-box narrow phase through the shape dispatch, on the same
    // reproducible samples the oracle fuzz uses. Report only: no timing assertions.
    [Category("M0c3a")]
    [Category("Manual")]
    [Explicit("Report-only timing; run by name on a quiet machine.")]
    public sealed class BoxDispatchBench
    {
        private const int Samples = 2000;
        private const int Batches = 61;
        private static long sink;

        [TestCase(SpatialQueryKind.Sphere)]
        [TestCase(SpatialQueryKind.Cylinder)]
        [TestCase(SpatialQueryKind.Sector)]
        [TestCase(SpatialQueryKind.Capsule)]
        [TestCase(SpatialQueryKind.Ray)]
        public void BoxNarrowPhasePerCall(SpatialQueryKind kind)
        {
            var inputs = new BoxInputs(0x3A0BE000UL + (ulong)kind);
            var samples = new BoxInputs.Sample[Samples];
            int hits = 0;
            for (int i = 0; i < samples.Length; i++)
            {
                samples[i] = inputs.Random(kind, i);
                hits += SpatialGeometry.Intersects(in samples[i].Body, in samples[i].Request, out _, false) ? 1 : 0;
            }
            for (int i = 0; i < 8; i++)
                sink = unchecked(sink + Batch(samples));
            var elapsed = new long[Batches];
            for (int i = 0; i < Batches; i++)
            {
                long start = Stopwatch.GetTimestamp();
                sink = unchecked(sink + Batch(samples));
                elapsed[i] = Stopwatch.GetTimestamp() - start;
            }
            Array.Sort(elapsed);
            double scale = 1000000000.0 / Stopwatch.Frequency / Samples;
            TestContext.WriteLine("3a-bench box.{0}: samples={1}; hits={2}; batches={3}; median={4} ns/call; p95={5} ns/call; checksum={6}",
                kind, Samples, hits, Batches, (elapsed[Batches / 2] * scale).ToString("F1", CultureInfo.InvariantCulture),
                (elapsed[(Batches * 95 - 1) / 100] * scale).ToString("F1", CultureInfo.InvariantCulture), sink);
        }

        private static long Batch(BoxInputs.Sample[] samples)
        {
            long checksum = 0;
            for (int i = 0; i < samples.Length; i++)
            {
                bool hit = SpatialGeometry.Intersects(in samples[i].Body, in samples[i].Request, out SimFix distance, false);
                checksum = unchecked(checksum + (hit ? 1 : 0) + distance.Raw);
            }
            return checksum;
        }
    }
}
