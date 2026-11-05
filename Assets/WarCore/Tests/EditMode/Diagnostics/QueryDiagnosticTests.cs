using System;
using System.IO;
using System.Security.Cryptography;
using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests.Diagnostics
{
    public sealed class QueryDiagnosticTests
    {
        [Test]
        public void ReplaySourcesMatchTheirProductAndGeneratorFingerprints()
        {
            string root = Path.Combine(ArchitectureTests.AssetRoot(), "WarCore");
            string[] products = { "SpatialQuery", "GridSpatialQuery", "GridSpatialIndex", "GridSpatialIndex.Modes", "SpatialQueryView", "GridSpatialQuery.Modes", "SpatialGeometry", "SpatialGeometrySlow", "SpatialSector", "SpatialRay", "GeometryUInt192", "SpatialBounds", "GeometryInt288", "GeometryWordDivision", "SpatialCellTable", "SpatialGridState", "SpatialGridScan", "SpatialCellRegions", "SpatialBodyColumns" };
            string[] productHashes = { DiagnosticSourceStamp.SpatialQuery, DiagnosticSourceStamp.GridSpatialQuery, DiagnosticSourceStamp.GridSpatialIndex, DiagnosticSourceStamp.GridSpatialIndex_Modes, DiagnosticSourceStamp.SpatialQueryView, DiagnosticSourceStamp.GridSpatialQuery_Modes, DiagnosticSourceStamp.SpatialGeometry, DiagnosticSourceStamp.SpatialGeometrySlow, DiagnosticSourceStamp.SpatialSector, DiagnosticSourceStamp.SpatialRay, DiagnosticSourceStamp.GeometryUInt192,
                DiagnosticSourceStamp.SpatialBounds, DiagnosticSourceStamp.GeometryInt288, DiagnosticSourceStamp.GeometryWordDivision, DiagnosticSourceStamp.SpatialCellTable, DiagnosticSourceStamp.SpatialGridState, DiagnosticSourceStamp.SpatialGridScan, DiagnosticSourceStamp.SpatialCellRegions, DiagnosticSourceStamp.SpatialBodyColumns };
            for (int i = 0; i < products.Length; i++)
                AssertHash(Path.Combine(root, "Sim", products[i] + ".cs"), productHashes[i]);
            string[] replays = { "ProfiledSpatialQueryBase", "ProfiledGridSpatialQuery", "ProfiledSpatialGeometry", "ProfiledSpatialRay" };
            string[] replayHashes = { DiagnosticSourceStamp.ProfiledSpatialQueryBase, DiagnosticSourceStamp.ProfiledGridSpatialQuery, DiagnosticSourceStamp.ProfiledSpatialGeometry, DiagnosticSourceStamp.ProfiledSpatialRay };
            for (int i = 0; i < replays.Length; i++)
                AssertHash(Path.Combine(root, "Tests/EditMode/Diagnostics", replays[i] + ".cs"), replayHashes[i]);
        }

        private static void AssertHash(string path, string expected)
        {
            using var sha = SHA256.Create();
            string actual = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
            Assert.That(actual, Is.EqualTo(expected), "Regenerate and review diagnostic mirrors: " + path);
        }

        [Test]
        public void EveryFrozenVectorHasIdenticalPublicResultsWithCountersAndReplayOffOrOn()
        {
            var inputs = new A2SpatialInputs(RealBenchScene.Seed);
            VectorJson rows = VectorJson.Load("spatial-narrowphase-vectors.json")["cases"];
            var expectedIds = new EntityId[1];
            var expectedRays = new RayHit[1];
            var ids = new EntityId[1];
            var rays = new RayHit[1];
            for (int i = 0; i < rows.Count; i++)
            {
                A2SpatialInputs.Sample sample = inputs.Vector(rows[i]);
                var store = new EntityStore(1);
                var positions = store.Register<Position>("position");
                var colliders = store.Register<Collider>("collider");
                EntityId id = store.Create();
                store.Set(positions, id, new Position(sample.Body.Position));
                store.Set(colliders, id, sample.Body.Collider);
                SimFix cell = SimFix.FromRaw(sample.Body.Collider.Height.Raw > 8 * SimFix.Scale ? sample.Body.Collider.Height.Raw : 8 * SimFix.Scale);
                var grid = new GridSpatialQuery(store, positions, colliders, cell, 1, 64);
                var replay = new ProfiledGridSpatialQuery(store, positions, colliders, cell, 1, 64);
                grid.Rebuild();
                replay.Rebuild();
                Assert.That(grid.Diagnostics, Is.Null);
                int expected = Run(grid, sample.Request, expectedIds, expectedRays);
                Assert.That(expected, Is.EqualTo(rows[i]["expectHit"].Boolean ? 1 : 0), sample.Source);
                if (expected > 0 && sample.Request.Kind == SpatialQueryKind.Ray)
                    Assert.That(expectedRays[0].Distance.Raw, Is.EqualTo(rows[i]["expectDistanceRaw"].Integer), sample.Source);
                grid.Diagnostics = new SpatialQueryDiagnostics();
                replay.Diagnostics = new SpatialQueryDiagnostics();
                for (int mode = 0; mode < 4; mode++)
                {
                    replay.Trace.Enabled = mode >= 2;
                    replay.Trace.DeepGeometry = mode != 3;
                    int actual = Run(mode == 0 ? (ISpatialQuery)grid : replay, sample.Request, ids, rays);
                    RealSimBench.AssertOutput(sample.Request.Kind == SpatialQueryKind.Ray ? 0 : 1, expected, actual, expectedIds, expectedRays, ids, rays);
                    if (mode != 0)
                    {
                        Assert.That(RealSimBench.SameCounters(grid.Diagnostics, replay.Diagnostics), Is.True, sample.Source);
                        Assert.That(replay.LastCandidateCount, Is.EqualTo(grid.LastCandidateCount), sample.Source);
                        Assert.That(replay.LastNarrowPhaseCount, Is.EqualTo(grid.LastNarrowPhaseCount), sample.Source);
                    }
                }
            }

            TestContext.WriteLine("Diagnostic public replay: all 733 frozen vectors, five shapes, counters ON and coarse/deep replay OFF/ON preserve ids/generations/raw distances and traversal counts; JSON unchanged.");
        }

        internal static int Run(ISpatialQuery query, SpatialRequest request, EntityId[] ids, RayHit[] rays)
        {
            switch (request.Kind)
            {
                case SpatialQueryKind.Sphere:
                    return query.Sphere(request.Origin, request.Radius, ids);
                case SpatialQueryKind.Cylinder:
                    return query.Cylinder(request.Origin, request.Radius, request.HalfHeight, ids);
                case SpatialQueryKind.Sector:
                    return query.Sector(request.Origin, request.Radius, request.Direction, request.Cosine, request.HalfHeight, ids);
                case SpatialQueryKind.Capsule:
                    return query.Capsule(request.Origin, request.Origin + request.Direction, request.Radius, ids);
                default:
                    return query.Ray(request.Origin, request.Direction, request.Range, rays);
            }
        }

        [Test]
        public void GeometryReplayRetainsBoundaryAndRandomResultsAndExactMagnitudeWidths()
        {
            const ulong seed = 0xB3D1A601;
            var inputs = new A2SpatialInputs(seed);
            var trace = new QueryTrace { Enabled = true };
            var replay = new ProfiledSpatialGeometry(trace);
            int count = 0;
            foreach (SpatialQueryKind kind in Enum.GetValues(typeof(SpatialQueryKind)))
                for (int i = 0; i < 2000; i++)
                {
                    A2SpatialInputs.Sample sample = inputs.Random(kind, i);
                    bool expected = SpatialGeometry.Intersects(sample.Body, sample.Request, out SimFix expectedDistance, out bool expectedPrecise);
                    bool actual = replay.Intersects(sample.Body, sample.Request, out SimFix distance, out bool precise);
                    Assert.That(actual, Is.EqualTo(expected), sample.Source);
                    Assert.That(distance.Raw, Is.EqualTo(expectedDistance.Raw), sample.Source);
                    Assert.That(precise, Is.EqualTo(expectedPrecise), sample.Source);
                    count++;
                }

            Assert.That(QueryTrace.BitWidth(default), Is.Zero);
            GeometryInt288 power = GeometryInt288.From(1);
            for (int bits = 1; bits <= 288; bits++)
            {
                Assert.That(QueryTrace.BitWidth(power), Is.EqualTo(bits));
                Assert.That(QueryTrace.BitWidth(-power), Is.EqualTo(bits));
                Assert.That(QueryTrace.BitWidth(power - GeometryInt288.From(1)), Is.EqualTo(bits - 1));
                if (bits < 288)
                    power = power * GeometryInt288.From(2);
            }

            TestContext.WriteLine("Diagnostic geometry: seed=0x{0:X}; {1} boundary/random cases; hit, distance and precise-entry flag identical; all 288 magnitude widths verified.", seed, count);
        }

        [Test]
        public void CounterSwitchPreservesTickHashIncludingEvents()
        {
            var plain = new SimBenchScene(16, SimBenchWorkload.Heavy);
            var observed = new SimBenchScene(16, SimBenchWorkload.Heavy);
            observed.Grid.Diagnostics = new SpatialQueryDiagnostics();
            for (int tick = 0; tick < 32; tick++)
            {
                plain.Sim.Step(default);
                observed.Sim.Step(default);
                Assert.That(observed.Sim.StateHash(), Is.EqualTo(plain.Sim.StateHash()), "tick=" + tick);
                Assert.That(observed.TotalHits, Is.EqualTo(plain.TotalHits));
                Assert.That(observed.Checksum, Is.EqualTo(plain.Checksum));
            }
        }

        [Test]
        public void CountersResetPerQueryAndNeverEnterSimulationHashOrAllocate()
        {
            var windows = new AllocationWindow();
            do
            {
                var scene = new A5GridScenario();
                var ids = new EntityId[8];
                var rays = new RayHit[8];
                var counters = new SpatialQueryDiagnostics();
                var query = new BenchQuery(1, SimVec3.Metres(4, 4, 4), default, default);
                Assert.That(scene.Grid.Diagnostics, Is.Null);
                scene.Grid.Diagnostics = counters;
                Assert.That(query.Run(scene.Grid, ids, rays), Is.Zero);
                Assert.That(counters.WalkCalls, Is.Zero, "Empty partitions are skipped before cell traversal.");
                Assert.That(counters.DictionaryLookups, Is.Zero);
                Assert.That(counters.SkippedPartitions, Is.EqualTo(2));
                Assert.That(counters.LeafMembers, Is.Zero);
                scene.CreateBody(query.Origin, 1, 2);
                query.Run(scene.Grid, ids, rays);
                Assert.That(counters.LeafMembers, Is.EqualTo(1));
                Assert.That(counters.DedupChecks, Is.EqualTo(1));
                Assert.That(counters.DuplicateSkips, Is.Zero);
                long walks = counters.WalkCalls;
                Assert.That(walks, Is.EqualTo(1), "Occupancy does not add recursive descent.");
                query.Run(scene.Grid, ids, rays);
                Assert.That(counters.WalkCalls, Is.EqualTo(walks));
                var meter = new AllocationMeter();
                for (int warm = 0; warm < 16; warm++)
                    query.Run(scene.Grid, ids, rays);
                ulong beforeHash = Fixtures.Hash(scene.Store);
                windows.Begin(meter);
                for (int i = 0; i < 1000; i++)
                {
                    scene.Grid.Diagnostics = (i & 1) == 0 ? counters : null;
                    query.Run(scene.Grid, ids, rays);
                }

                AllocationSample bytes = windows.End();
                if (!windows.NeedsRetry)
                    AllocationAssertions.Zero(bytes);
                Assert.That(Fixtures.Hash(scene.Store), Is.EqualTo(beforeHash));
            }
            while (windows.NeedsRetry);
        }
    }
}
