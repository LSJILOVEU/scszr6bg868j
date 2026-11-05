using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using WarCore.Sim;
using WarCore.Verification.Fixtures;
using WarCore.Verification.Fixtures.Diagnostics;

namespace WarCore.Verification
{
    public static class BenchmarkRunner
    {
        public static void Run(string mode, string outputPath)
        {
            if (mode != "smoke" && mode != "full") throw new ArgumentException("Benchmark mode must be smoke or full.");
            int ticks = mode == "smoke" ? 30 : 300;
            CultureInfo previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
                using (var output = new StreamWriter(outputPath, false, new System.Text.UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" })
                {
                    output.WriteLine("G23 benchmarks; mode={0}; warmup=8; realTicks={1}; Stopwatch.Frequency={2}; allocation evidence is labelled per measurement; see companion metadata for backend/architecture.", mode, ticks, Stopwatch.Frequency);
                    output.WriteLine("Smoke cannot validate G24. No timing assertions. Legacy SimBench includes original signature checks; real profiles use the shared workload, product diagnostics OFF.");
                    // Same profiles, query input ordering, oracle checks and individual-call timings as the old tests.
                    foreach (SimBenchWorkload workload in new[] { SimBenchWorkload.Light, SimBenchWorkload.Heavy, SimBenchWorkload.DenseAimedRays })
                        SimBench.RunProfile(workload, 2000, mode == "smoke" ? 30 : 51, output);
                    SimBench.RunSingleQueries(2000, output);
                    for (int layout = 0; layout < 2; layout++)
                        for (int peak = 0; peak < 2; peak++)
                            Real(layout != 0, peak != 0, ticks, output, outputPath);
                    for (int layout = 0; layout < 2; layout++)
                        for (int peak = 0; peak < 2; peak++)
                            Semantic(layout != 0, peak != 0, false, ticks, output, outputPath);
                    Semantic(true, true, true, ticks, output, outputPath);
                    Round4ChurnBench.Run(mode,output);
                    output.WriteLine("complete|benchmarks=ok");
                }
            }
            finally { CultureInfo.CurrentCulture = previous; }
        }

        private static void Semantic(bool dense, bool peak, bool overflow, int ticks, TextWriter output, string path)
        {
            var scene = new SemanticBenchScene(dense, peak, overflow);
            Console.WriteLine("G23 semantic benchmark " + scene.Label + ": " + ticks + " measured ticks");
            SemanticBenchMeasurement.Run(scene, ticks, new SemanticPlayerMeter(), output, path + ".semantic");
        }

        private static void Real(bool dense, bool peak, int ticks, TextWriter output, string path)
        {
            var windows = new AllocationWindow();
            do
            {
                string name = (dense ? "Dense" : "Scattered") + (peak ? "Peak" : "Regular");
                Console.WriteLine("G23 benchmark " + name + ": " + ticks + " measured ticks");
                var scene = new RealProductScene(dense, peak);
                for (int i = 0; i < 8; i++) scene.Sim.Step(default);
                var elapsed = new long[ticks];
                var allocations = new AllocationSample[ticks];
                var meter = new AllocationMeter();
                Stopwatch.GetTimestamp();
                long callsBefore = scene.Calls;
                windows.Begin(meter);
                for (int i = 0; i < ticks; i++)
                {
                    AllocationSample before = meter.Read();
                    long start = Stopwatch.GetTimestamp();
                    scene.Sim.Step(default);
                    elapsed[i] = Stopwatch.GetTimestamp() - start;
                    allocations[i] = meter.Difference(before);
                    AllocationPolicy.CheckDirect(allocations[i]);
                    int living = 0;
                    foreach (EntityId entity in scene.Sim.Entities) living++;
                    HashScenarios.Require(living == 2000 && scene.Sim.Events.EventsDroppedTotal == 0, "Real benchmark lost entities/events");
                }
                windows.End();
                HashScenarios.Require(scene.Calls - callsBefore == (long)ticks * scene.Queries.Length, "Real benchmark work incomplete");
                if (windows.NeedsRetry)
                    continue;
                output.WriteLine("Allocation windows {0}: {1}", name, windows.Result.Describe());
                AllocationPolicy.Check(windows.Result);
                for (int i = 0; i < allocations.Length; i++)
                {
                    allocations[i] = windows.Annotate(allocations[i]);
                    if (!windows.Result.IsInconclusive)
                        AllocationPolicy.CheckObserved(allocations[i]);
                }
                // Empty byte columns mean unavailable byte evidence, never zero bytes.
                using (var csv = new StreamWriter(path + "." + name + ".ticks.csv"))
                {
                    csv.WriteLine("sample,sim_tick,stopwatch_ticks,elapsed_ms,allocation_basis,gc_bytes,gc0_delta,gc1_delta,gc2_delta,total_memory_delta,allocation_evidence,rays,affected,queries,live_entities");
                    for (int i = 0; i < ticks; i++)
                        csv.WriteLine("{0},{1},{2},{3:F6},{4},{5},{6},{7},2000", i + 1, i + 9, elapsed[i], elapsed[i] * 1000.0 / Stopwatch.Frequency,
                            allocations[i].Csv(), scene.RayCount, scene.Affected, scene.Queries.Length);
                }
                double total = 0;
                foreach (long value in elapsed) total += value;
                Array.Sort(elapsed);
                double scale = 1000.0 / Stopwatch.Frequency;
                int rank = (ticks * 99 + 99) / 100 - 1;
                output.WriteLine("Real {0}: ticks={1}; mean_ms={2:F6}; p99_ms={3:F6}; max_ms={4:F6}; {5}; rays/tick={6}; E/tick={7}; queries/tick={8}; finalHash={9}; checksum={10}.",
                    name, ticks, total / ticks * scale, elapsed[rank] * scale, elapsed[ticks - 1] * scale,
                    windows.Result.Basis == AllocationBasis.Direct ? AllocationSample.Summary(allocations) : windows.Result.Describe(),
                    scene.RayCount, scene.Affected, scene.Queries.Length,
                    StableDigest.Hex64(scene.Sim.StateHash()), StableDigest.Hex64(scene.Checksum));
            }
            while (windows.NeedsRetry);
        }
    }

    // Product diagnostics-OFF loop from RealBenchScene; the builder and oracle are shared source.
    internal sealed class RealProductScene : RealBenchWorkload, ISimSystem
    {
        public int Order => 1;
        internal RealProductScene(bool dense, bool peak) : base(dense, peak) => Sim.Register("real-bench-queries", this);
        public void Step(SimContext context)
        {
            LastAffected = 0;
            LastRays = 0;
            int cover = 0;
            for (int i = 0; i < Queries.Length; i++)
            {
                RealQuery query = Queries[i].Role == QueryRole.Cover ? Cover(Entities[cover++]) : Queries[i];
                int found = query.Query.Run(Grid, Entities, Rays);
                ulong signature = Signature(query.Query.Kind, found);
                if (found != ExpectedCounts[i] || signature != ExpectedSignatures[i]) throw new InvalidOperationException("Real benchmark output changed.");
                if (query.Role == QueryRole.Explosion) LastAffected = found;
                if (query.Query.Kind == 0) LastRays++;
                Calls++;
                TotalHits += found;
                Checksum = unchecked(Checksum * 1099511628211UL ^ signature);
            }
            if (LastAffected != Affected || LastRays != RayCount || cover != Affected) throw new InvalidOperationException("Real benchmark work disappeared.");
            context.Emit(new SimEvent(new EventTypeId(902), tag: Queries.Length));
        }
        public void WriteState(StateHash hash) { hash.Add(Calls); hash.Add(Checksum); }
    }
}
