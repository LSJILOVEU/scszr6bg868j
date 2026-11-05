using System;
using System.Collections.Generic;
using System.Diagnostics;
using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests.Diagnostics
{
    [Category("NarrowPhase")]
    public sealed class NarrowPhaseDiagnosticTests
    {
        [Test]
        public void OptimizedRayReplayMeasuresRemainingWorkAndPreservesEverySample()
        {
            var groups = new[] { new List<A2SpatialInputs.Sample>(), new List<A2SpatialInputs.Sample>() };
            var inputs = new A2SpatialInputs(0xA2B00001);
            VectorJson vectors = VectorJson.Load("spatial-narrowphase-vectors.json")["cases"];
            for (int i = 0; i < vectors.Count; i++)
                if (vectors[i]["kind"].Text == "ray")
                    Classify(inputs.Vector(vectors[i]), groups);
            for (int i = 0; i < 1000; i++)
                Classify(inputs.Random(SpatialQueryKind.Ray, i), groups);
            var traces = new[] { new QueryTrace(), new QueryTrace() };
            for (int group = 0; group < groups.Length; group++)
            {
                QueryTrace trace = traces[group];
                var replay = new ProfiledSpatialRay(trace);
                foreach (A2SpatialInputs.Sample sample in groups[group])
                {
                    bool actual = replay.Intersects(in sample.Body, in sample.Request, out SimFix a);
                    bool expected = SpatialGeometry.Intersects(in sample.Body, in sample.Request, out SimFix b, true);
                    Assert.That(actual, Is.EqualTo(expected));
                    Assert.That(a, Is.EqualTo(b));
                }
                trace.Enabled = true;
                for (int repeat = 0; repeat < 50; repeat++)
                    foreach (A2SpatialInputs.Sample sample in groups[group])
                        replay.Intersects(in sample.Body, in sample.Request, out _);
                Report(group == 0 ? "RayPreciseMiss" : "RayPositiveHit", groups[group].Count, trace);
            }
            Assert.That(groups[0].Count, Is.EqualTo(54));
            Assert.That(groups[1].Count, Is.EqualTo(226));
            Assert.That(traces[1].Calls[(int)QueryStage.Sqrt], Is.GreaterThan(0), "Positive control for square-root probes.");
            Assert.That(traces[0].Calls[(int)QueryStage.Sqrt], Is.Zero, "This fixed miss corpus must reject before scaled-root evaluation.");
        }

        private static void Classify(A2SpatialInputs.Sample sample, List<A2SpatialInputs.Sample>[] groups)
        {
            bool hit = SpatialGeometry.Intersects(in sample.Body, in sample.Request, out SimFix distance, true);
            if (hit && distance.Raw > 0)
                groups[1].Add(sample);
            else if (!hit && SpatialBounds.RayMayHit(in sample.Body, in sample.Request))
                groups[0].Add(sample);
        }

        private static void Report(string name, int samples, QueryTrace trace)
        {
            long total = 0;
            foreach (long ticks in trace.Exclusive)
                total += ticks;
            TestContext.WriteLine("Optimized ray replay {0}: samples={1}; repeats=50; intrusive phase shares; square-root/division implementations are product calls; general final-distance wide sqrt stays inside Distance; no overhead subtraction.", name, samples);
            for (int i = 0; i < trace.Calls.Length; i++)
                if (trace.Calls[i] != 0)
                    TestContext.WriteLine("Optimized phase {0}/{1}: calls={2}; exclusive_us={3:F3}; exclusive_percent={4:F3}.",
                        name, (QueryStage)i, trace.Calls[i], trace.Exclusive[i] * 1000000.0 / Stopwatch.Frequency, trace.Exclusive[i] * 100.0 / total);
            var bins = new long[7];
            for (int bits = 0; bits <= 192; bits++)
                bins[(bits + 31) / 32] += trace.OperandWidths[(int)QueryStage.Sqrt * 289 + bits];
            TestContext.WriteLine("Optimized sqrt widths {0}: operand bins [0,1-32,33-64,65-96,97-128,129-160,161-192]={1}.", name, string.Join("/", bins));
        }
    }
}
