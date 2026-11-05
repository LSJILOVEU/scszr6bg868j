using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using WarCore.Sim;

namespace WarCore.Tests.Diagnostics
{
    public static class RealSimBench
    {
        internal const int MeasuredTicks = 300;
        internal static string Label(bool dense, bool peak) => (dense ? "Dense" : "Scattered") + (peak ? "Peak" : "Regular");

        public static void Run(bool dense, bool peak, TextWriter output)
            => Run(dense, peak, output, MeasuredTicks);

        internal static void Run(bool dense, bool peak, TextWriter output, int ticks)
        {
            if (ticks != 30 && ticks < 300)
                throw new ArgumentOutOfRangeException(nameof(ticks));
            string name = Label(dense, peak);
            string directory = BenchEnvironment.OutputDirectory();
            if (ticks == 30)
            {
                directory = Path.Combine(directory, "smoke");
                Directory.CreateDirectory(directory);
            }
            var report = new StringWriter(CultureInfo.InvariantCulture);
            BenchEnvironment.Report(report);
            var scene = new RealBenchScene(dense, peak);
            Describe(scene, report);
            VerifySwitches(scene);
            var elapsed = new long[ticks];
            var bytes = new AllocationSample[ticks];
            var observedElapsed = new long[ticks];
            var observedBytes = new AllocationSample[ticks];
            scene.Grid.Diagnostics = null;
            scene.Observe = false;
            Warm(scene);
            long firstTick = Measure(scene, elapsed, bytes);
            scene.Observations = new QueryObservation[ticks * scene.Queries.Length];
            scene.Grid.Diagnostics = scene.Counters;
            scene.Observe = true;
            Warm(scene);
            scene.ObservationOffset = 0;
            long observedFirstTick = Measure(scene, observedElapsed, observedBytes);
            if (scene.ObservationOffset != scene.Observations.Length)
                throw new InvalidOperationException("Every measured query must have an observation.");
            report.WriteLine("Real SimBench {0}: continuous {1} ticks per mode; 8 warmup ticks per mode; live entities=2000 throughout; no timing assertion.", name, ticks);
            report.WriteLine("| mode | ticks | mean ms | p99 ms | max ms | allocation | rays/tick | E/tick |");
            report.WriteLine("|---|---:|---:|---:|---:|---|---:|---:|");
            BenchReport.TickSummary(report, "product diagnostics OFF", elapsed, bytes, scene.RayCount, scene.Affected);
            BenchReport.TickSummary(report, "product counters + query observation ON", observedElapsed, observedBytes, scene.RayCount, scene.Affected);
            report.WriteLine("Observer whole-tick mean delta={0}%; includes counters, two timestamps, two GC reads and observation storage per query; no subtraction from published samples.",
                BenchReport.Number((BenchReport.Mean(observedElapsed) / BenchReport.Mean(elapsed) - 1) * 100));
            report.WriteLine("Both modes include full-output signature verification, event emission and the Sim tick pipeline. File IO, summary sorting and phase replay are outside measured ticks; EventsDropped=0.");
            WriteSamples(directory, name, scene, elapsed, bytes, observedElapsed, observedBytes, firstTick, observedFirstTick);
            BenchReport.Queries(report, scene, ticks);
            QueryProfiling.Run(scene, report, directory, name);
            report.WriteLine("Raw artifacts: {0}/{1}-ticks.csv; {1}-queries.csv; {1}-inputs.csv; {1}-phases.csv; {1}-deep-phases.csv; {1}-widths.csv.", directory, name);
            File.WriteAllText(Path.Combine(directory, name + "-summary.txt"), report.ToString());
            output.Write(report.ToString());
        }

        private static void Warm(RealBenchScene scene)
        {
            for (int i = 0; i < SimBench.WarmupTicks; i++)
                scene.Sim.Step(default);
        }

        private static long Measure(RealBenchScene scene, long[] elapsed, AllocationSample[] bytes)
        {
            var meter = new AllocationMeter();
            var windows = new AllocationWindow();
            Stopwatch.GetTimestamp();
            do
            {
                scene.ObservationOffset = 0;
                long callsBefore = scene.Calls;
                windows.Begin(meter);
                for (int tick = 0; tick < elapsed.Length; tick++)
                {
                    AllocationSample before = meter.Read();
                    long start = Stopwatch.GetTimestamp();
                    scene.Sim.Step(default);
                    elapsed[tick] = Stopwatch.GetTimestamp() - start;
                    bytes[tick] = meter.Difference(before);
                    AllocationAssertions.CheckDirect(bytes[tick]);
                    if (scene.Sim.Events.EventsDroppedTotal != 0 || LiveCount(scene) != RealBenchScene.EntityCount)
                        throw new InvalidOperationException("Measured ticks must keep the crowd alive and drop no events.");
                }
                windows.End();
                if (scene.Calls - callsBefore != (long)elapsed.Length * scene.Queries.Length)
                    throw new InvalidOperationException("Measured window must execute every query.");
            }
            while (windows.NeedsRetry);
            AllocationAssertions.Zero(windows.Result);
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = windows.Annotate(bytes[i]);
                AllocationAssertions.CheckObserved(bytes[i]);
            }
            for (int i = 0; i < scene.ObservationOffset; i++)
            {
                scene.Observations[i].Allocation = windows.Annotate(scene.Observations[i].Allocation);
                AllocationAssertions.CheckObserved(scene.Observations[i].Allocation);
            }
            // Retried windows advance the clock even though their timing samples are discarded.
            return scene.Sim.Clock.Tick - elapsed.Length + 1;
        }

        private static int LiveCount(RealBenchScene scene)
        {
            int count = 0;
            foreach (EntityId entity in scene.Sim.Entities)
                count++;
            return count;
        }

        internal static void Describe(RealBenchScene scene, TextWriter output)
        {
            output.WriteLine("Workload={0}; seed=0x{1:X}; grid=3m; 50 columns x 40 rows; spacing={2}m; capsules radius=0.25m height=3m; y=1.5m; fixed static inputs repeat every tick.",
                Label(scene.Dense, scene.Peak), RealBenchScene.Seed, scene.Dense ? "0.75" : "3");
            output.WriteLine("Per tick: 660 AI rays + 165 spheres + 165 sectors{0}. Peak additions, when enabled, are applied on EVERY tick.",
                scene.Peak ? " + 40 single-shot rays + 32 shotgun rays (4 x 8) + 100 volley rays + 60 melee sectors + 1 explosion sphere + E cover rays" : "");
            output.WriteLine("AI sphere radius={0}m; AI sector radius={1}m; all sectors cosine=600 halfHeight=2m direction=(1,0,1); melee radius=3m; ray range={2}m.",
                scene.Dense ? 2 : 6, scene.Dense ? 3 : 6, scene.Dense ? 48 : 12);
            output.WriteLine("Dense rays: origin=(-4,1.5,(20+targetId%7-3)*0.75), aim at row 20+targetId%5-2, column 25. Scattered AI rays repeat body-origin/(-4,0,0)-offset/upward-miss/(-4,0,0)-offset; firing rays alternate offsets (-4,0,-2) and (-4,0,0), directions (2,0,1) and (1,0,0). Seed selects target ids; every actual input is in inputs.csv.");
            output.WriteLine("Shotgun: four origins, eight consecutive pellets each; aim-direction offsets in metres are (0,+/-1/64,(2*pellet-7)/64), pellet=0..7; fixed spread is workload data, not a weapon model.");
            output.WriteLine("Explosion center raw=({0},{1},{2}); radius=8m; E={3}; each actual sphere result produces one cover ray from explosion center to that capsule center, range=floor fixed-point center distance. No faction, source, target or cover-hit exclusion; endpoint capsule counts as a hit. E is the geometric affected set before damage/occlusion rules. No damage, despawn or entity replenishment is simulated.",
                scene.ExplosionOrigin.X.Raw, scene.ExplosionOrigin.Y.Raw, scene.ExplosionOrigin.Z.Raw, scene.Affected);
        }

        internal static void VerifySwitches(RealBenchScene scene)
        {
            var expectedEntities = new EntityId[RealBenchScene.EntityCount];
            var expectedRays = new RayHit[RealBenchScene.EntityCount];
            ulong before = scene.Sim.StateHash();
            for (int i = 0; i < scene.Queries.Length; i++)
            {
                BenchQuery query = scene.Queries[i].Query;
                scene.Grid.Diagnostics = null;
                int count = query.Run(scene.Grid, expectedEntities, expectedRays);
                for (int mode = 0; mode < 4; mode++)
                {
                    scene.Grid.Diagnostics = scene.Counters;
                    scene.Replay.Trace.Enabled = mode >= 2;
                    scene.Replay.Trace.DeepGeometry = mode != 3;
                    scene.Replay.Trace.Reset();
                    ISpatialQuery provider = mode == 0 ? (ISpatialQuery)scene.Grid : scene.Replay;
                    int actual = query.Run(provider, scene.Entities, scene.Rays);
                    AssertOutput(query.Kind, count, actual, expectedEntities, expectedRays, scene.Entities, scene.Rays);
                    if (mode >= 2 && (scene.Replay.Trace.Calls[(int)QueryStage.Precise] != scene.Replay.LastNarrowPhaseCount
                        || scene.Replay.Trace.Calls[(int)QueryStage.Write] != actual
                        || scene.Replay.Trace.Calls[(int)QueryStage.Validation] != 1
                        || scene.Replay.Trace.Calls[(int)QueryStage.Broadphase] != 1
                        || scene.Replay.Trace.Calls[(int)QueryStage.Sort] != 1
                        || ((mode == 2 || query.Kind == 0) && scene.Replay.Trace.Calls[(int)QueryStage.Prefilter] != scene.Replay.LastCandidateCount)))
                        throw new InvalidOperationException("Diagnostic stage calls do not match the product work.");
                    if (mode != 0 && (scene.Grid.LastCandidateCount != scene.Replay.LastCandidateCount
                        || scene.Grid.LastNarrowPhaseCount != scene.Replay.LastNarrowPhaseCount
                        || scene.Grid.LastVisitedCells != scene.Replay.LastVisitedCells
                        || !SameCounters(scene.Counters, scene.Replay.Diagnostics)))
                        throw new InvalidOperationException("Diagnostic replay counters differ from the product.");
                }
            }

            scene.Grid.Diagnostics = null;
            scene.Replay.Trace.Enabled = false;
            if (scene.Sim.StateHash() != before)
                throw new InvalidOperationException("Query instrumentation changed simulation hash.");
        }

        internal static bool SameCounters(SpatialQueryDiagnostics a, SpatialQueryDiagnostics b) => a.WalkCalls == b.WalkCalls
            && a.DictionaryLookups == b.DictionaryLookups && a.LeafMembers == b.LeafMembers
            && a.DedupChecks == b.DedupChecks && a.DuplicateSkips == b.DuplicateSkips
            && a.EmptyLookups == b.EmptyLookups && a.HashProbes == b.HashProbes
            && a.MaskRejectedCells == b.MaskRejectedCells && a.RegionNodes == b.RegionNodes
            && a.DirectorySlotsScanned == b.DirectorySlotsScanned
            && a.StaticMembers == b.StaticMembers && a.UnitMembers == b.UnitMembers
            && a.VisitedPartitions == b.VisitedPartitions && a.SkippedPartitions == b.SkippedPartitions;

        internal static void AssertOutput(int kind, int expected, int actual, EntityId[] expectedEntities, RayHit[] expectedRays, EntityId[] entities, RayHit[] rays)
        {
            if (expected != actual)
                throw new InvalidOperationException("Diagnostic switch changed result count.");
            for (int i = 0; i < expected; i++)
            {
                bool equal = kind == 0 ? expectedRays[i].Entity.Equals(rays[i].Entity) && expectedRays[i].Distance.Raw == rays[i].Distance.Raw
                    : expectedEntities[i].Equals(entities[i]);
                if (!equal)
                    throw new InvalidOperationException("Diagnostic switch changed sorted ids, generations or raw distances.");
            }
        }

        private static void WriteSamples(string directory, string name, RealBenchScene scene, long[] elapsed, AllocationSample[] bytes, long[] observed, AllocationSample[] observedBytes, long firstTick, long observedFirstTick)
        {
            using (var writer = new StreamWriter(Path.Combine(directory, name + "-ticks.csv")))
            {
                writer.WriteLine("mode,sample,sim_tick,elapsed_stopwatch_ticks,elapsed_ms,allocation_basis,gc_bytes,gc0_delta,gc1_delta,gc2_delta,total_memory_delta,total_rays,affected_E,query_calls,live_entities");
                for (int mode = 0; mode < 2; mode++)
                    for (int i = 0; i < elapsed.Length; i++)
                    {
                        long duration = mode == 0 ? elapsed[i] : observed[i];
                        writer.WriteLine("{0},{1},{2},{3},{4},{5},{6},{7},{8},2000", mode == 0 ? "off" : "observed", i,
                            (mode == 0 ? firstTick : observedFirstTick) + i, duration,
                            BenchReport.Number(duration * 1000.0 / Stopwatch.Frequency), (mode == 0 ? bytes[i] : observedBytes[i]).CsvValues(), scene.RayCount, scene.Affected, scene.Queries.Length);
                    }
            }

            using (var writer = new StreamWriter(Path.Combine(directory, name + "-queries.csv")))
            {
                writer.WriteLine("sample,query_id,role,elapsed_stopwatch_ticks,allocation_basis,gc_bytes,gc0_delta,gc1_delta,gc2_delta,total_memory_delta,hits,deduplicated_candidates,precise_entries,walk_calls,dictionary_lookups,leaf_members,dedup_checks,duplicate_skips,last_visited_cells");
                for (int i = 0; i < scene.Observations.Length; i++)
                {
                    QueryObservation q = scene.Observations[i];
                    writer.WriteLine("{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13}", i / scene.Queries.Length, i % scene.Queries.Length,
                        scene.Queries[i % scene.Queries.Length].Role, q.Elapsed, q.Allocation.CsvValues(), q.Hits, q.Candidates, q.Precise, q.Walks, q.Lookups, q.Members, q.Dedup, q.Duplicates, q.Visited);
                }
            }

            using (var writer = new StreamWriter(Path.Combine(directory, name + "-inputs.csv")))
            {
                writer.WriteLine("query_id,role,kind,origin_x_raw,origin_y_raw,origin_z_raw,direction_x_raw,direction_y_raw,direction_z_raw,radius_or_range_raw");
                for (int i = 0; i < scene.Queries.Length; i++)
                {
                    BenchQuery q = scene.Queries[i].Query;
                    writer.WriteLine("{0},{1},{2},{3},{4},{5},{6},{7},{8},{9}", i, scene.Queries[i].Role, q.Kind, q.Origin.X.Raw, q.Origin.Y.Raw, q.Origin.Z.Raw,
                        q.Direction.X.Raw, q.Direction.Y.Raw, q.Direction.Z.Raw, q.Radius.Raw);
                }
            }
        }
    }
}
