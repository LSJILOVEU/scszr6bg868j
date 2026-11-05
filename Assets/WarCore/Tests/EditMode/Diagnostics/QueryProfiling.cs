using System;
using System.Diagnostics;
using System.IO;
using WarCore.Sim;

namespace WarCore.Tests.Diagnostics
{
    internal static class QueryProfiling
    {
        internal static void Run(RealBenchScene scene, TextWriter output, string directory, string name)
        {
            const int repeats = 3;
            var totals = new[] { new QueryTrace(), new QueryTrace(), new QueryTrace() };
            var deepTotals = new[] { new QueryTrace(), new QueryTrace(), new QueryTrace() };
            var durations = new long[5][];
            for (int mode = 0; mode < 5; mode++)
                durations[mode] = new long[repeats * scene.Queries.Length];
            var allocations = new AllocationSample[repeats * scene.Queries.Length * 5];
            var replayCounters = new SpatialQueryDiagnostics();
            var meter = new AllocationMeter();
            var windows = new AllocationWindow();
            do
            {
                for (int kind = 0; kind < totals.Length; kind++)
                {
                    totals[kind].Reset();
                    deepTotals[kind].Reset();
                }
                windows.Begin(meter);
                for (int repeat = 0; repeat < repeats; repeat++)
                    for (int i = 0; i < scene.Queries.Length; i++)
                        for (int order = 0; order < 5; order++)
                        {
                            // Rotate the comparison order to reduce systematic first-call/cache bias.
                            int mode = (order + i + repeat) % 5;
                            scene.Grid.Diagnostics = mode == 1 ? scene.Counters : null;
                            scene.Replay.Diagnostics = mode >= 3 ? replayCounters : null;
                            scene.Replay.Trace.Enabled = mode >= 3;
                            scene.Replay.Trace.DeepGeometry = mode == 4;
                            scene.Replay.Trace.Reset();
                            ISpatialQuery query = mode < 2 ? (ISpatialQuery)scene.Grid : scene.Replay;
                            BenchQuery input = scene.Queries[i].Query;
                            AllocationSample before = meter.Read();
                            long start = Stopwatch.GetTimestamp();
                            int count = input.Run(query, scene.Entities, scene.Rays);
                            durations[mode][repeat * scene.Queries.Length + i] = Stopwatch.GetTimestamp() - start;
                            AllocationSample bytes = meter.Difference(before);
                            allocations[(repeat * scene.Queries.Length + i) * 5 + mode] = bytes;
                            AllocationAssertions.CheckDirect(bytes);
                            if (count != scene.ExpectedCounts[i] || scene.Signature(input.Kind, count) != scene.ExpectedSignatures[i])
                                throw new InvalidOperationException("Phase profiling changed output.");
                            if (mode == 3)
                                totals[input.Kind].Add(scene.Replay.Trace);
                            if (mode == 4)
                                deepTotals[input.Kind].Add(scene.Replay.Trace);
                        }
                windows.End();
            }
            while (windows.NeedsRetry);
            output.WriteLine("Allocation windows phase replay: " + windows.Result.Describe());
            AllocationAssertions.Check(windows.Result);
            for (int i = 0; i < allocations.Length; i++)
            {
                allocations[i] = windows.Annotate(allocations[i]);
                AllocationAssertions.CheckObserved(allocations[i]);
            }
            scene.Grid.Diagnostics = null;
            scene.Replay.Diagnostics = replayCounters;
            scene.Replay.Trace.Enabled = false;
            output.WriteLine("Phase replay: test-only source-checked copy of public validation/grid/geometry control flow; calls original Sim request, bounds and wide arithmetic. Coarse replay binds the actual product sphere/area/ray methods once; deep replay adds arithmetic wrappers and histograms. All sorted ids/generations/raw distances checked with both replays OFF/ON against actual product; hash unchanged; allocation checked in all five timing modes.");
            output.WriteLine("Phase replay {0}", AllocationSample.Summary(allocations));
            output.WriteLine("Phase times are instrumented replay diagnostics, not exact uninstrumented product shares. Scope bookkeeping, magnitude histograms and wrapper copies perturb costs; no overhead subtraction. Inclusive stages overlap; exclusive stages partition the measured scope tree. Coarse sphere/sector precise time includes scalar rejection; deep replay splits that part as a nested prefilter. Discriminant includes its wide arithmetic; Sqrt includes its internal Newton/division work; wide API counts/widths exclude operations internal to product Sqrt.");
            output.WriteLine("| kind | calls/mode | product OFF us mean/median/p95/max | counters ON us | coarse replay OFF us | coarse replay ON us | deep replay ON us | counter mean delta % | coarse ON/product delta % | deep ON/product delta % |");
            output.WriteLine("|---|---:|---|---|---|---|---|---:|---:|---:|");
            for (int kind = 0; kind < 3; kind++)
            {
                var samples = new long[5][];
                int count = 0;
                for (int i = 0; i < scene.Queries.Length; i++)
                    if (scene.Queries[i].Query.Kind == kind)
                        count += repeats;
                for (int mode = 0; mode < 5; mode++)
                {
                    samples[mode] = new long[count];
                    int cursor = 0;
                    for (int i = 0; i < durations[mode].Length; i++)
                        if (scene.Queries[i % scene.Queries.Length].Query.Kind == kind)
                            samples[mode][cursor++] = durations[mode][i];
                }

                double baseline = BenchReport.Mean(samples[0]);
                output.WriteLine("| {0} | {1} | {2} | {3} | {4} | {5} | {6} | {7} | {8} | {9} |", Kind(kind), count,
                    BenchReport.Distribution(samples[0], 1000000.0 / Stopwatch.Frequency), BenchReport.Distribution(samples[1], 1000000.0 / Stopwatch.Frequency),
                    BenchReport.Distribution(samples[2], 1000000.0 / Stopwatch.Frequency), BenchReport.Distribution(samples[3], 1000000.0 / Stopwatch.Frequency), BenchReport.Distribution(samples[4], 1000000.0 / Stopwatch.Frequency),
                    BenchReport.Number((BenchReport.Mean(samples[1]) / baseline - 1) * 100), BenchReport.Number((BenchReport.Mean(samples[3]) / baseline - 1) * 100), BenchReport.Number((BenchReport.Mean(samples[4]) / baseline - 1) * 100));
            }

            output.WriteLine("COARSE public phases (actual product geometry):");
            ReportStages(output, totals, Path.Combine(directory, name + "-phases.csv"));
            output.WriteLine("DEEP forced-slow reference geometry phases (intrusive arithmetic tracing; not optimized product shares):");
            ReportStages(output, deepTotals, Path.Combine(directory, name + "-deep-phases.csv"));
            ReportWidths(output, deepTotals, Path.Combine(directory, name + "-widths.csv"));
        }

        internal static string Kind(int kind) => kind == 0 ? "Ray" : kind == 1 ? "Sphere" : "Sector";
        internal static void ReportStages(TextWriter output, QueryTrace[] totals, string path)
        {
            using var csv = new StreamWriter(path);
            csv.WriteLine("kind,stage,calls,inclusive_stopwatch_ticks,exclusive_stopwatch_ticks,exclusive_percent");
            output.WriteLine("| kind | phase | calls | inclusive ms | exclusive ms | exclusive % |");
            output.WriteLine("|---|---|---:|---:|---:|---:|");
            for (int kind = 0; kind < totals.Length; kind++)
            {
                QueryTrace trace = totals[kind];
                long total = 0;
                for (int i = 0; i < trace.Exclusive.Length; i++)
                    total += trace.Exclusive[i];
                for (int stage = 0; stage < trace.Calls.Length; stage++)
                {
                    double percent = total == 0 ? 0 : trace.Exclusive[stage] * 100.0 / total;
                    csv.WriteLine("{0},{1},{2},{3},{4},{5}", Kind(kind), (QueryStage)stage, trace.Calls[stage], trace.Inclusive[stage], trace.Exclusive[stage], BenchReport.Number(percent));
                    output.WriteLine("| {0} | {1} | {2} | {3} | {4} | {5} |", Kind(kind), (QueryStage)stage, trace.Calls[stage],
                        BenchReport.Number(trace.Inclusive[stage] * 1000.0 / Stopwatch.Frequency), BenchReport.Number(trace.Exclusive[stage] * 1000.0 / Stopwatch.Frequency), BenchReport.Number(percent));
                }
            }
        }

        internal static void ReportWidths(TextWriter output, QueryTrace[] totals, string path)
        {
            using var csv = new StreamWriter(path);
            csv.WriteLine("kind,operation,magnitude_bits,operand_count,result_count");
            for (int kind = 0; kind < totals.Length; kind++)
                for (int stage = 0; stage < (int)QueryStage.Count; stage++)
                {
                    var buckets = new long[10];
                    int max = 0;
                    for (int bits = 0; bits <= 288; bits++)
                    {
                        int index = stage * 289 + bits;
                        long operands = totals[kind].OperandWidths[index];
                        long results = totals[kind].ResultWidths[index];
                        csv.WriteLine("{0},{1},{2},{3},{4}", Kind(kind), (QueryStage)stage, bits, operands, results);
                        buckets[bits == 0 ? 0 : (bits + 31) / 32] += operands;
                        if (operands + results > 0)
                            max = bits;
                    }

                    if (kind != 1 && totals[kind].Calls[stage] > 0 && stage >= (int)QueryStage.Sqrt)
                        output.WriteLine("{3} magnitude widths {0}: max operand/result={1}; operand bins [0,1-32,33-64,65-96,97-128,129-160,161-192,193-224,225-256,257-288]={2}.",
                            (QueryStage)stage, max, string.Join("/", buckets), Kind(kind));
                }
        }

        internal static void Calibration(TextWriter output)
        {
            const int count = 10001;
            var clock = new long[count];
            var scope = new long[count];
            var allocations = new long[count];
            var trace = new QueryTrace { Enabled = true };
            var meter = new AllocationMeter();
            for (int i = 0; i < 100; i++)
            {
                using (trace.Measure(QueryStage.WideAdd))
                    meter.Read();
            }

            var windows = new AllocationWindow();
            AllocationSample allocation;
            do
            {
                windows.Begin(meter);
                for (int i = 0; i < count; i++)
                {
                    long start = Stopwatch.GetTimestamp();
                    clock[i] = Stopwatch.GetTimestamp() - start;
                    start = Stopwatch.GetTimestamp();
                    using (trace.Measure(QueryStage.WideAdd))
                    {
                    }
                    scope[i] = Stopwatch.GetTimestamp() - start;
                    start = Stopwatch.GetTimestamp();
                    meter.Read();
                    meter.Read();
                    allocations[i] = Stopwatch.GetTimestamp() - start;
                }

                allocation = windows.End();
            }
            while (windows.NeedsRetry);
            AllocationAssertions.Check(allocation);
            output.WriteLine("Probe calibration ns mean/median/p95/max: empty timestamp bracket={0}; empty scope inside outer bracket={1}; two allocation sample reads inside bracket={2}; {4}. Timer quantization={3}ns; no subtraction.",
                BenchReport.Distribution(clock, 1000000000.0 / Stopwatch.Frequency), BenchReport.Distribution(scope, 1000000000.0 / Stopwatch.Frequency),
                BenchReport.Distribution(allocations, 1000000000.0 / Stopwatch.Frequency), BenchReport.Number(1000000000.0 / Stopwatch.Frequency), allocation.Describe());
        }
    }
}
