using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using WarCore.Sim;

namespace WarCore.Tests.Diagnostics
{
    internal struct SemanticObservation
    {
        internal long Elapsed;
        internal AllocationSample Allocation;
        internal SemanticResult Result;
        internal int Candidates;
        internal int Precise;
        internal int TruncatedHits;
    }

    internal sealed class SemanticObserver : ISemanticObserver
    {
        internal readonly SemanticObservation[] Samples;
        internal int Count;
        private readonly IAllocationMeter meter;
        private AllocationSample before;
        private long start;

        internal SemanticObserver(int count, IAllocationMeter allocationMeter)
        {
            Samples = new SemanticObservation[count];
            meter = allocationMeter;
        }

        public void Begin()
        {
            before = meter.Read();
            start = Stopwatch.GetTimestamp();
        }

        public void End(int index, SemanticResult result, GridSpatialQuery grid)
        {
            long elapsed = Stopwatch.GetTimestamp() - start;
            AllocationSample allocation = meter.Difference(before);
            AllocationAssertions.CheckDirect(allocation);
            Samples[Count++] = new SemanticObservation { Elapsed = elapsed, Allocation = allocation, Result = result,
                Candidates = grid.LastCandidateCount, Precise = grid.LastNarrowPhaseCount, TruncatedHits = grid.LastTruncatedHits };
        }
    }

    internal static class SemanticBenchMeasurement
    {
        internal static void Run(SemanticBenchScene scene, int ticks, IAllocationMeter meter, TextWriter output, string directory)
        {
            SemanticBenchScene.Require(ticks == 30 || ticks >= 300, "Use 30 smoke ticks or at least 300 full ticks.");
            Directory.CreateDirectory(directory);
            string prefix = Path.Combine(directory, "Semantic" + scene.Label);
            var summary = new StringWriter(CultureInfo.InvariantCulture);
            SemanticBenchArtifacts.Describe(scene, summary);
            SemanticBenchArtifacts.Write(scene, prefix);
            var observer = new SemanticObserver(scene.Queries.Length * ticks, meter);
            for (int mode = 0; mode < 2; mode++)
            {
                scene.Observer = mode == 0 ? null : observer;
                // Use a separate warmup buffer so measured samples always cover all selected ticks.
                var warm = new SemanticObserver(scene.Queries.Length * 8, meter);
                if (mode != 0)
                    scene.Observer = warm;
                for (int i = 0; i < 8; i++)
                    scene.Sim.Step(default);
                scene.Sim.CompleteWarmup();
                scene.Observer = mode == 0 ? null : observer;
                var elapsed = new long[ticks];
                var allocations = new AllocationSample[ticks];
                var windows = new AllocationWindow();
                do
                {
                    observer.Count = 0;
                    long beforeCalls = scene.Calls;
                    windows.Begin(meter);
                    for (int i = 0; i < ticks; i++)
                    {
                        AllocationSample before = meter.Read();
                        long start = Stopwatch.GetTimestamp();
                        scene.Sim.Step(default);
                        elapsed[i] = Stopwatch.GetTimestamp() - start;
                        allocations[i] = meter.Difference(before);
                        AllocationAssertions.CheckDirect(allocations[i]);
                        SemanticBenchScene.Require(scene.Sim.Events.EventsDroppedTotal == 0, "Semantic benchmark dropped events.");
                    }
                    windows.End();
                    SemanticBenchScene.Require(scene.Calls - beforeCalls == (long)ticks * scene.Queries.Length, "Semantic tick work incomplete.");
                }
                while (windows.NeedsRetry);
                output.WriteLine("Allocation windows {0}/{1}: {2}", scene.Label, mode, windows.Result.Describe());
                AllocationAssertions.Check(windows.Result);
                for (int i = 0; i < ticks; i++)
                {
                    allocations[i] = windows.Annotate(allocations[i]);
                    if (!windows.Result.IsInconclusive)
                        AllocationAssertions.CheckObserved(allocations[i]);
                }
                for (int i = 0; i < observer.Count; i++)
                {
                    observer.Samples[i].Allocation = windows.Annotate(observer.Samples[i].Allocation);
                    if (!windows.Result.IsInconclusive)
                        AllocationAssertions.CheckObserved(observer.Samples[i].Allocation);
                }
                summary.WriteLine("Allocation windows {0}/{1}: {2}", scene.Label, mode, windows.Result.Describe());
                WriteTicks(scene, ticks, mode, elapsed, allocations, windows.Result, prefix, summary);
            }
            SemanticBenchScene.Require(observer.Count == observer.Samples.Length, "Semantic query observations incomplete.");
            WriteQueries(scene, observer.Samples, ticks, prefix, summary);
            summary.WriteLine("Timing includes Sim.Step, signatures, retained-target cover generation and live/event checks. Observer timing also includes per-query timestamp/allocation reads. No overhead subtraction. Oracle construction, raw exports and report formatting are outside measurement.");
            summary.WriteLine("G24=UNVERIFIED; smoke is not acceptance; IL2CPP indirect counters cannot prove strict zero bytes. Debug truncation reporting: lastTruncatedQueries/tick={0}; omittedHits/tick={1}; actualCoverRays/tick={2}.",
                scene.LastTruncatedQueries, scene.LastTruncatedHits, scene.LastCoverRays);
            File.WriteAllText(prefix + "-summary.txt", summary.ToString());
            output.Write(summary.ToString());
        }

        private static void WriteTicks(SemanticBenchScene scene, int ticks, int mode, long[] elapsed, AllocationSample[] allocations, AllocationSample window, string prefix, TextWriter output)
        {
            string label = mode == 0 ? "off" : "observed";
            using (var writer = new StreamWriter(prefix + "-" + label + "-ticks.csv"))
            {
                writer.WriteLine("sample,elapsed_ticks,allocation_basis,gc_bytes,gc0_delta,gc1_delta,gc2_delta,total_memory_delta,rays,E,queries,live");
                for (int i = 0; i < ticks; i++)
                    writer.WriteLine("{0},{1},{2},{3},{4},{5},{6}", i, elapsed[i], allocations[i].CsvValues(), scene.RayCount, scene.Affected, scene.Queries.Length, SemanticBenchScene.BodyCount);
            }
            long sum = 0;
            foreach (long value in elapsed)
                sum += value;
            Array.Sort(elapsed);
            double scale = 1000.0 / Stopwatch.Frequency;
            output.WriteLine("Semantic timing {0}: mode={1}; ticks={2}; mean_ms={3:F6}; p99_ms={4:F6}; max_ms={5:F6}; {6}; rays/tick={7}; E/tick={8}; queries/tick={9}.",
                scene.Label, label, ticks, (double)sum / ticks * scale, elapsed[(ticks * 99 + 99) / 100 - 1] * scale,
                elapsed[ticks - 1] * scale, window.Basis == AllocationBasis.Direct ? AllocationSummary(allocations) : window.Describe(),
                scene.RayCount, scene.Affected, scene.Queries.Length);
        }

        private static string AllocationSummary(AllocationSample[] samples) => AllocationSample.Summary(samples);

        private static void WriteQueries(SemanticBenchScene scene, SemanticObservation[] samples, int ticks, string prefix, TextWriter output)
        {
            using (var writer = new StreamWriter(prefix + "-queries.csv"))
            {
                writer.WriteLine("tick,query_id,role,mode,hit,total_hits,written,truncated,oracle_true_hits,blocker_hits,candidates,precise,elapsed_ticks,allocation_basis,gc_bytes,gc0_delta,gc1_delta,gc2_delta,total_memory_delta,omitted_alarm");
                for (int i = 0; i < samples.Length; i++)
                {
                    int q = i % scene.Queries.Length;
                    SemanticObservation s = samples[i];
                    writer.WriteLine("{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14}", i / scene.Queries.Length, q,
                        scene.Queries[q].Role, scene.Queries[q].Mode, s.Result.Hit, s.Result.TotalHits, s.Result.Written,
                        s.Result.Truncated, scene.Expected[q].TrueHits, scene.Expected[q].BlockerHits, s.Candidates, s.Precise, s.Elapsed, s.Allocation.CsvValues(), s.TruncatedHits);
                }
            }
            output.WriteLine("Per-query means; eligible static/units are content populations; true/static/unit/blocker hits use exhaustive oracle. Blocked ratio counts queries with >0 actual blocker hits. Any has no entity output.");
            output.WriteLine("| role | mode | calls/tick | eligible static/units | true hits | static/unit hits | blocker hits | blocked ratio | candidates | precise | us | allocation |");
            output.WriteLine("|---|---|---:|---|---:|---|---:|---:|---:|---:|---:|---|");
            for (int role = 0; role < 9; role++)
                for (int mode = 0; mode < 3; mode++)
                {
                    long count = 0;
                    long hits = 0;
                    long staticHits = 0;
                    long unitHits = 0;
                    long blockers = 0;
                    long blocked = 0;
                    long candidates = 0;
                    long precise = 0;
                    long elapsed = 0;
                    uint mask = 0;
                    var allocationList = new System.Collections.Generic.List<AllocationSample>();
                    for (int i = 0; i < samples.Length; i++)
                    {
                        int q = i % scene.Queries.Length;
                        SemanticQuery query = scene.Queries[q];
                        if ((int)query.Role != role || (int)query.Mode != mode)
                            continue;
                        count++;
                        mask = query.Request.LayerMask;
                        hits += scene.Expected[q].TrueHits;
                        staticHits += scene.Expected[q].StaticHits;
                        unitHits += scene.Expected[q].UnitHits;
                        blockers += scene.Expected[q].BlockerHits;
                        blocked += scene.Expected[q].BlockerHits > 0 ? 1 : 0;
                        candidates += samples[i].Candidates;
                        precise += samples[i].Precise;
                        elapsed += samples[i].Elapsed;
                        allocationList.Add(samples[i].Allocation);
                    }
                    if (count == 0)
                        continue;
                    int eligibleStatic = 0;
                    int eligibleUnits = 0;
                    for (int i = 0; i < scene.Bodies.Length; i++)
                        if ((scene.Bodies[i].Collider.Layer & mask) != 0)
                        {
                            if (scene.IsStatic[i])
                                eligibleStatic++;
                            else
                                eligibleUnits++;
                        }
                    output.WriteLine("| {0} | {1} | {2} | {3}/{4} | {5:F3} | {6:F3}/{7:F3} | {8:F3} | {9:P2} | {10:F3} | {11:F3} | {12:F3} | {13} |",
                        (QueryRole)role, (SemanticMode)mode, count / ticks, eligibleStatic, eligibleUnits, (double)hits / count,
                        (double)staticHits / count, (double)unitHits / count, (double)blockers / count, (double)blocked / count,
                        (double)candidates / count, (double)precise / count, (double)elapsed / count * 1000000 / Stopwatch.Frequency, AllocationSummary(allocationList.ToArray()));
                }
        }
    }
}
