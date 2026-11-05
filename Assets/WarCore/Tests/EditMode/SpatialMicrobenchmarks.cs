using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using WarCore.Sim;
using WarCore.Tests.Oracle;

namespace WarCore.Tests
{
    public sealed class SpatialMicrobenchmarks
    {
        private const int Repetitions = 8;
        private const int WarmupBatches = 12;
        private const int BatchCount = 101;
        private long sink;

        private enum Category
        {
            RayBoundsMiss,
            RayPreciseMiss,
            RayHit,
            Sphere,
            Sector,
            RayPositiveHit
        }

        private static A2SpatialInputs.Sample[][] Samples(A2SpatialInputs.Predicate product, bool report)
        {
            var lists = new List<A2SpatialInputs.Sample>[6];
            for (int i = 0; i < lists.Length; i++)
                lists[i] = new List<A2SpatialInputs.Sample>();
            var inputs = new A2SpatialInputs(0xA2B00001UL);
            var bounds = A2SpatialInputs.Bounds();
            VectorJson vectors = VectorJson.Load("spatial-narrowphase-vectors.json")["cases"];
            for (int i = 0; i < vectors.Count; i++)
            {
                string kind = vectors[i]["kind"].Text;
                if (kind == "ray" || kind == "sphere" || kind == "sector")
                    Classify(inputs.Vector(vectors[i]), product, bounds, lists);
            }

            foreach (SpatialQueryKind kind in new[]{SpatialQueryKind.Ray, SpatialQueryKind.Sphere, SpatialQueryKind.Sector})
                for (int i = 0; i < 1000; i++)
                    Classify(inputs.Random(kind, i), product, bounds, lists);
            var result = new A2SpatialInputs.Sample[lists.Length][];
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = lists[i].ToArray();
                Assert.That(result[i].Length, Is.GreaterThan(0));
                if (report)
                    ReportComposition((Category)i, result[i], product);
            }

            return result;
        }

        private static void Classify(A2SpatialInputs.Sample sample, A2SpatialInputs.Predicate product, A2SpatialInputs.BoundsPredicate bounds, List<A2SpatialInputs.Sample>[] lists)
        {
            bool hit = product(in sample.Body, in sample.Request, out SimFix distance);
            bool expected = OracleSpatialGeometry.Intersects(sample.Body, sample.Request, out SimFix expectedDistance);
            Assert.That(hit, Is.EqualTo(expected), sample.Source);
            Assert.That(distance, Is.EqualTo(expectedDistance), sample.Source);
            Category category;
            if (sample.Request.Kind == SpatialQueryKind.Ray)
            {
                bool possible = bounds(in sample.Body, in sample.Request);
                Assert.That(!possible && hit, Is.False, sample.Source);
                category = hit ? Category.RayHit : possible ? Category.RayPreciseMiss : Category.RayBoundsMiss;
                if (hit && distance.Raw > 0)
                    lists[(int)Category.RayPositiveHit].Add(sample);
            }
            else
                category = sample.Request.Kind == SpatialQueryKind.Sphere ? Category.Sphere : Category.Sector;
            lists[(int)category].Add(sample);
        }

        private static void ReportComposition(Category category, A2SpatialInputs.Sample[] samples, A2SpatialInputs.Predicate product)
        {
            var sources = new SortedDictionary<string, int>(StringComparer.Ordinal);
            int hits = 0;
            int zeroDistance = 0;
            int ballBodies = 0;
            int nonAxisRays = 0;
            int large = 0;
            foreach (A2SpatialInputs.Sample sample in samples)
            {
                string source = sample.Source.StartsWith("vector:", StringComparison.Ordinal) ? "fixed-vector" : sample.Source;
                sources.TryGetValue(source, out int count);
                sources[source] = count + 1;
                bool hit = product(in sample.Body, in sample.Request, out SimFix distance);
                hits += hit ? 1 : 0;
                zeroDistance += hit && distance.Raw == 0 && sample.Request.Kind == SpatialQueryKind.Ray ? 1 : 0;
                ballBodies += sample.Body.Collider.Height.Raw / 2 == sample.Body.Collider.Radius.Raw ? 1 : 0;
                SimVec3 d = sample.Request.Direction;
                int axes = (d.X.Raw != 0 ? 1 : 0) + (d.Y.Raw != 0 ? 1 : 0) + (d.Z.Raw != 0 ? 1 : 0);
                nonAxisRays += axes > 1 && sample.Request.Kind == SpatialQueryKind.Ray ? 1 : 0;
                SimVec3 p = sample.Body.Position;
                SimVec3 o = sample.Request.Origin;
                large += Math.Abs(p.X.Raw) >= A2SpatialInputs.World / 2 || Math.Abs(p.Y.Raw) >= A2SpatialInputs.World / 2 || Math.Abs(p.Z.Raw) >= A2SpatialInputs.World / 2 ||
                    Math.Abs(o.X.Raw) >= A2SpatialInputs.World / 2 || Math.Abs(o.Y.Raw) >= A2SpatialInputs.World / 2 || Math.Abs(o.Z.Raw) >= A2SpatialInputs.World / 2 ? 1 : 0;
            }

            var parts = new List<string>();
            foreach (KeyValuePair<string, int> source in sources)
                parts.Add(source.Key + "=" + source.Value);
            TestContext.WriteLine("A2 samples {0}: count={1}, hits={2}, misses={3}, ray zero-distance hits={4}, degenerate sphere bodies={5}, non-axis rays={6}, coordinates >=8192m={7}; {8}.",
                category, samples.Length, hits, samples.Length - hits, zeroDistance, ballBodies, nonAxisRays, large, string.Join(", ", parts));
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static long RunBatch(A2SpatialInputs.Predicate product, A2SpatialInputs.Sample[] samples)
        {
            long checksum = 0;
            for (int repeat = 0; repeat < Repetitions; repeat++)
                for (int i = 0; i < samples.Length; i++)
                {
                    ref readonly A2SpatialInputs.Sample sample = ref samples[i];
                    bool hit = product(in sample.Body, in sample.Request, out SimFix distance);
                    checksum = unchecked(checksum + (hit ? 1 : 0) + distance.Raw);
                }

            return checksum;
        }

        [Test]
        public void ReportGeometryMedianAndP95WithoutTimingAssertions()
        {
            var product = A2SpatialInputs.Product();
            A2SpatialInputs.Sample[][] samples = Samples(product, true);
            TestContext.WriteLine("A2 microbench: Stopwatch.Frequency={0}; all 156 ray / 135 sphere / 170 sector fixed vectors plus 1000 generated inputs per kind, seed=0xA2B00001; all inputs validated by public query entry points and independently checked against the frozen oracle before timing.", Stopwatch.Frequency);
            TestContext.WriteLine("A2 timing: predicate with prebuilt requests, bound delegate and in parameters; 12 warmup batches; 101 measured batches x 8 passes over every category input; median and nearest-rank p95 of batch means in ns/call; includes delegate/loop/array/checksum, excludes public validation and grid traversal; no overhead subtraction or timing assertions.");
            for (int category = 0; category < samples.Length; category++)
            {
                A2SpatialInputs.Sample[] values = samples[category];
                var elapsed = new long[BatchCount];
                for (int warmup = 0; warmup < WarmupBatches; warmup++)
                    sink = unchecked(sink + RunBatch(product, values));
                Stopwatch.GetTimestamp();
                for (int batch = 0; batch < elapsed.Length; batch++)
                {
                    long start = Stopwatch.GetTimestamp();
                    long checksum = RunBatch(product, values);
                    elapsed[batch] = Stopwatch.GetTimestamp() - start;
                    sink = unchecked(sink + checksum);
                }

                Array.Sort(elapsed);
                double scale = 1000000000.0 / Stopwatch.Frequency / (values.Length * Repetitions);
                double median = elapsed[BatchCount / 2] * scale;
                double p95 = elapsed[(BatchCount * 95 - 1) / 100] * scale;
                TestContext.WriteLine("A2 microbench {0}: calls={1}, median={2} ns/call, p95={3} ns/call", (Category)category,
                    BatchCount * Repetitions * values.Length, median.ToString("F3", CultureInfo.InvariantCulture), p95.ToString("F3", CultureInfo.InvariantCulture));
                double goal = category == (int)Category.Sphere ? 20 : category == (int)Category.RayHit || category == (int)Category.RayPositiveHit ? 3000 : 100;
                if (category != (int)Category.Sector)
                    TestContext.WriteLine("A2 goal {0} <= {1} ns: median {2}, p95 {3} (report only; this runtime and sample mix).", (Category)category, goal, median <= goal ? "met" : "missed", p95 <= goal ? "met" : "missed");
            }

            TestContext.WriteLine("A2 microbench checksum={0}", sink);
        }

        [Test]
        public void WarmGeometryPredicatesAllocateZero()
        {
            var windows = new AllocationWindow();
            do
            {
                var product = A2SpatialInputs.Product();
                A2SpatialInputs.Sample[][] samples = Samples(product, false);
                foreach (A2SpatialInputs.Sample[] values in samples)
                    for (int warmup = 0; warmup < WarmupBatches; warmup++)
                        sink = unchecked(sink + RunBatch(product, values));
                var meter = new AllocationMeter();
                windows.Begin(meter);
                int calls = 0;
                foreach (A2SpatialInputs.Sample[] values in samples)
                {
                    sink = unchecked(sink + RunBatch(product, values));
                    calls += values.Length * Repetitions;
                }

                AllocationSample allocation = windows.End();
                if (!windows.NeedsRetry)
                    AllocationAssertions.Zero(allocation);
                TestContext.WriteLine("A2 warm geometry: {0} calls, {1}; five main categories plus positive-distance hit subset, checksum={2}.", calls, allocation.Describe(), sink);
            }
            while (windows.NeedsRetry);
        }
    }
}
