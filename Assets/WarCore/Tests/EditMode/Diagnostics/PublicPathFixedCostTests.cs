using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests.Diagnostics
{
    public sealed class PublicPathFixedCostTests
    {
        [TestCase(0, true)]
        [TestCase(1, true)]
        [TestCase(4, true)]
        [TestCase(1, false)]
        [TestCase(4, false)]
        public void EmptyAndFewCandidateQueriesExposeValidationAndTreeFixedCosts(int bodies, bool rootBoundary)
        {
            SimVec3 offset = rootBoundary ? default : new SimVec3(SimFix.FromInt(30), SimFix.Ratio(3, 2), SimFix.FromInt(30));
            var store = new EntityStore(4);
            var positions = store.Register<Position>("position");
            var colliders = store.Register<Collider>("collider");
            for (int i = 0; i < bodies; i++)
            {
                EntityId entity = store.Create();
                store.Set(positions, entity, new Position(offset + SimVec3.Metres(i * 3, 0, 0)));
                store.Set(colliders, entity, new Collider(SimFix.Ratio(1, 4), SimFix.FromInt(3)));
            }

            var grid = new GridSpatialQuery(store, positions, colliders, SimFix.FromInt(3), 4, 128);
            var replay = new ProfiledGridSpatialQuery(store, positions, colliders, SimFix.FromInt(3), 4, 128);
            grid.Rebuild();
            replay.Rebuild();
            replay.Trace.DeepGeometry = false;
            var ids = new EntityId[4];
            var rays = new RayHit[4];
            var expectedIds = new EntityId[4];
            var expectedRays = new RayHit[4];
            var counters = new SpatialQueryDiagnostics();
            replay.Diagnostics = new SpatialQueryDiagnostics();
            var totals = new[] { new QueryTrace(), new QueryTrace(), new QueryTrace() };
            var meter = new AllocationMeter();
            var report = new StringWriter(CultureInfo.InvariantCulture);
            BenchEnvironment.Report(report);
            report.WriteLine("Origin shift raw=({0},{1},{2}); root-boundary layout={3}.", offset.X.Raw, offset.Y.Raw, offset.Z.Raw, rootBoundary);
            report.WriteLine("Fixed-cost scene: {0} capsules; grid=3m; relative positions=(3*i,0,0) plus printed origin shift; radius=0.25m height=3m; sphere/sector origin=0 radius=6m; sector dir=(1,0,1), cosine=600, halfHeight=2m; ray origin=(-4,0,0) dir=(1,0,0) range=12m.", bodies);
            report.WriteLine("| kind | product OFF us mean/median/p95/max | counters ON us | coarse replay OFF us | coarse replay ON us | Walk | dictionary | leaves | dedup | duplicates | visited | candidates | precise | hits | allocation |");
            report.WriteLine("|---|---|---|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
            for (int kind = 0; kind < 3; kind++)
            {
                BenchQuery input = kind == 0 ? new BenchQuery(0, offset + SimVec3.Metres(-4, 0, 0), SimVec3.Metres(1, 0, 0), SimFix.FromInt(12))
                    : new BenchQuery(kind, offset, SimVec3.Metres(1, 0, 1), SimFix.FromInt(6));
                int expected = input.Run(grid, expectedIds, expectedRays);
                var samples = new long[4][];
                for (int mode = 0; mode < 4; mode++)
                    samples[mode] = new long[1001];
                for (int warm = 0; warm < 16; warm++)
                {
                    grid.Diagnostics = counters;
                    input.Run(grid, ids, rays);
                    replay.Trace.Enabled = true;
                    input.Run(replay, ids, rays);
                }

                var windows = new AllocationWindow();
                AllocationSample bytes;
                do
                {
                    totals[kind].Reset();
                    windows.Begin(meter);
                    for (int sample = 0; sample < 1001; sample++)
                        for (int order = 0; order < 4; order++)
                        {
                            int mode = (sample + order) % 4;
                            grid.Diagnostics = mode == 1 ? counters : null;
                            replay.Trace.Enabled = mode == 3;
                            replay.Trace.Reset();
                            ISpatialQuery provider = mode < 2 ? (ISpatialQuery)grid : replay;
                            long start = Stopwatch.GetTimestamp();
                            int found = input.Run(provider, ids, rays);
                            samples[mode][sample] = Stopwatch.GetTimestamp() - start;
                            RealSimBench.AssertOutput(kind, expected, found, expectedIds, expectedRays, ids, rays);
                            if (mode == 3)
                                totals[kind].Add(replay.Trace);
                        }
                    bytes = windows.End();
                }
                while (windows.NeedsRetry);
                AllocationAssertions.Zero(bytes);
                report.WriteLine("| {0} | {1} | {2} | {3} | {4} | {5} | {6} | {7} | {8} | {9} | {10} | {11} | {12} | {13} | {14} |", QueryProfiling.Kind(kind),
                    BenchReport.Distribution(samples[0], 1000000.0 / Stopwatch.Frequency), BenchReport.Distribution(samples[1], 1000000.0 / Stopwatch.Frequency),
                    BenchReport.Distribution(samples[2], 1000000.0 / Stopwatch.Frequency), BenchReport.Distribution(samples[3], 1000000.0 / Stopwatch.Frequency),
                    counters.WalkCalls, counters.DictionaryLookups, counters.LeafMembers, counters.DedupChecks, counters.DuplicateSkips,
                    grid.LastVisitedCells, grid.LastCandidateCount, grid.LastNarrowPhaseCount, expected, bytes.Describe());
            }

            string directory = BenchEnvironment.OutputDirectory();
            string label = "Fixed" + bodies + (rootBoundary ? "Boundary" : "Shifted");
            QueryProfiling.ReportStages(report, totals, Path.Combine(directory, label + "-phases.csv"));
            report.WriteLine("Hybrid selects a grid level directly; Walk counts selected-cell probes, not recursive descent. Compare empty vs 1/4 candidates and actual lookup counts when separating validation and traversal costs. Replay percentages include probe cost; product OFF is the complete public call.");
            File.WriteAllText(Path.Combine(directory, label + "-summary.txt"), report.ToString());
            TestContext.Out.Write(report.ToString());
        }
    }
}
