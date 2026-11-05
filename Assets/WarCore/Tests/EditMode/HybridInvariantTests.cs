using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Sim;
using WarCore.Tests.Oracle;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    [Category("Step2bB")]
    public sealed class HybridInvariantTests
    {
        [TestCase(63, 0)]
        [TestCase(64, 0)]
        [TestCase(65, 1)]
        public void LargeThresholdIsAnInclusiveCellBudget(int cells, int large)
        {
            var fixture = new QueryModeFixture();
            EntityId entity = fixture.Add(new SimVec3(SimFix.Ratio(3, 2), SimFix.Ratio(3, 2), SimFix.Ratio(3, 2)),
                SimFix.Scale / 8, (cells - 1L) * 3 * SimFix.Scale);
            Assert.That(fixture.Grid.Index.LargeBodyCount, Is.EqualTo(large));
            Assert.That(fixture.Grid.MembershipCount, Is.EqualTo(large == 0 ? cells : 1));
            var q = SpatialQueries.Sphere(fixture.Bodies[0].Position, default);
            fixture.Check(q, default, 1, 1);
            fixture.Store.Destroy(entity);
            Assert.That(fixture.Grid.Any(in q), Is.False);
            fixture.Grid.Synchronize();
            Assert.That(fixture.Grid.MembershipCount, Is.Zero);
            Assert.That(fixture.Grid.Index.LargeBodyCount, Is.Zero);
        }

        [Test]
        public void CellBudgetFailureKeepsPublishedGeometryAndMasksThenRecovers()
        {
            var sim = new Simulation(1, config: HybridStorageAudit.Config(4, 100, 16));
            var p = sim.Entities.Register<Position>("position");
            var c = sim.Entities.Register<Collider>("collider");
            var view = (SpatialQueryView)sim.ConfigureSpatial(p, c, SimFix.FromInt(3));
            var grid = new GridSpatialQuery(sim.SpatialIndex, view.QueryContext);
            EntityId first = HybridStorageTests.Add(sim.Entities, p, c, SimVec3.Metres(1, 1, 1));
            sim.CompleteSpatialBoundary();
            string[] before = GridIndexRegressionAudit.Logical(grid);
            sim.Entities.Set(p, first, new Position(SimVec3.Metres(1000, 1, 1)));
            EntityId excess = HybridStorageTests.Add(sim.Entities, p, c, SimVec3.Metres(-16000, 1, 1));
            SimBoundaryException error = Assert.Throws<SimBoundaryException>(() => sim.CompleteSpatialBoundary());
            Assert.That(error.Message, Does.Contain("cell capacity 16").And.Contain("8"));
            GridIndexRegressionAudit.Equal(before, GridIndexRegressionAudit.Logical(grid), "Cell exhaustion must not publish any change.");
            sim.Entities.Destroy(excess);
            sim.CompleteSpatialBoundary();
            Assert.That(grid.Sphere(SimVec3.Metres(1000, 1, 1), default, new EntityId[4]), Is.EqualTo(1));
            Assert.That(grid.Sphere(SimVec3.Metres(1, 1, 1), default, new EntityId[4]), Is.Zero);
        }

        [Test]
        public void CellMasksSkipMembersBeforeNarrowphaseAndDetectInjectedStaleBits()
        {
            var scene = new GridOwnershipTestFixture();
            EntityId excluded = scene.CreateBody(SimVec3.Metres(1, 1, 1), 0, 0);
            scene.CreateBody(SimVec3.Metres(17, 1, 1), 0, 0);
            scene.Store.Set(scene.Colliders, excluded, new Collider(default, default, 2u));
            scene.InitializeIndex();
            var counters = new SpatialQueryDiagnostics();
            scene.Grid.Diagnostics = counters;
            var q = SpatialQueries.Sphere(SimVec3.Metres(1, 1, 1), default, 1u);
            Assert.That(scene.Grid.Any(in q), Is.False);
            Assert.That(counters.MaskRejectedCells, Is.EqualTo(1));
            Assert.That(counters.LeafMembers, Is.Zero);
            Assert.That(scene.Grid.LastNarrowPhaseCount, Is.Zero);
            SpatialGridState state = (SpatialGridState)GridIndexRegressionAudit.Field(scene.Grid, "active");
            int at = state.Unit.Table.Occupied[0];
            uint correct = state.Unit.Table.Cells[at].Mask;
            state.Unit.Table.Cells[at].Mask ^= 0x80000000u;
            Assert.Throws<AssertionException>(() => HybridStorageAudit.Masks(scene.Grid.Index));
            state.Unit.Table.Cells[at].Mask = correct;
            HybridStorageAudit.Masks(scene.Grid.Index);
        }

        [Test]
        public void NearestCoarseEventKeepsSameEventNeighbourAndRawTie()
        {
            var fixture = new QueryModeFixture();
            EntityId expected = fixture.Add(SimVec3.Metres(48, 48, 0));
            fixture.Add(SimVec3.Metres(48, 47, 0), 0, 2 * SimFix.Scale);
            fixture.Add(SimVec3.Metres(96, 96, 0));
            var q = SpatialQueries.Ray(default, SimVec3.Metres(1, 1, 0), SimFix.FromInt(3072));
            fixture.Check(q, default, 3, 3);
            Assert.That(fixture.Grid.Nearest(in q, out SpatialHit hit), Is.True);
            Assert.That(hit.Entity, Is.EqualTo(expected));
            Assert.That(fixture.Grid.LastCandidateCount, Is.EqualTo(2));
        }

        [Test]
        public void NearestProcessesLargeBypassBeforeAcceptingAnEarlyCellBound()
        {
            var fixture = new QueryModeFixture();
            fixture.Add(SimVec3.Metres(2, 0, 0));
            EntityId expected = fixture.Add(SimVec3.Metres(33, 0, 0), 32 * SimFix.Scale, 64 * SimFix.Scale);
            var q = SpatialQueries.Ray(default, SimVec3.Metres(1, 0, 0), SimFix.FromInt(5000));
            Assert.That(fixture.Grid.Index.LargeBodyCount, Is.EqualTo(1));
            fixture.Check(q, default, 2, 2);
            fixture.Grid.Nearest(in q, out SpatialHit hit);
            Assert.That(hit.Entity, Is.EqualTo(expected));
            Assert.That(hit.Distance.Raw, Is.EqualTo(SimFix.Scale));
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void NearestMustProveTheOtherIndexBoundIncludingItsLargeList(bool reverse, bool large)
        {
            var fixture = new QueryModeFixture();
            EntityId near = fixture.Add(SimVec3.Metres(large ? 33 : 5, 0, 0), large ? 32 * SimFix.Scale : 0, large ? 64 * SimFix.Scale : 0);
            EntityId far = fixture.Add(SimVec3.Metres(1024, 0, 0));
            var a = new GridSpatialIndex(fixture.Store, fixture.Positions, fixture.Colliders, SimFix.FromInt(3), 768, readLiveLayers: false);
            var b = new GridSpatialIndex(fixture.Store, fixture.Positions, fixture.Colliders, SimFix.FromInt(3), 768, readLiveLayers: false);
            a.Upsert(reverse ? near : far);
            b.Upsert(reverse ? far : near);
            var ca = new GridQueryContext(768);
            var cb = new GridQueryContext(768);
            var request = SpatialQueries.Ray(default, SimVec3.Metres(1, 0, 0), SimFix.FromInt(5000));
            var collector = new SpatialHitCollector(SpatialResultMode.Nearest, default, default, 0);
            a.ExecuteMode(ca, in request, ref collector);
            cb.Begin();
            b.AccumulateMode(cb, in request, ref collector);
            Assert.That(collector.Best.Entity, Is.EqualTo(near));
            Assert.That(collector.Best.Distance.Raw, Is.EqualTo((large ? 1 : 5) * SimFix.Scale));
            Assert.That(ca.CandidateCount + cb.CandidateCount, Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void TwoHundredMutationsPreserveAllShapesModesMasksAndLogicalRestoration()
        {
            const ulong seed = 0x2BB200;
            var scene = new GridOwnershipTestFixture(16, 1024);
            var random = new A1MathInputs(seed);
            var handles = new EntityId[16];
            for (int i = 0; i < handles.Length; i++)
                handles[i] = scene.CreateBody(SimVec3.Metres(i * 2, 0, 0));
            scene.InitializeIndex();
            var ids = new EntityId[16];
            var rays = new RayHit[16];
            var output = new SpatialHit[5];
            int comparisons = 0;
            for (int step = 0; step < 200; step++)
            {
                int slot = (int)(random.Next() % 16);
                EntityId entity = handles[slot];
                if (step % 5 == 0)
                {
                    scene.Store.Destroy(entity);
                    scene.Sim.CompleteSpatialBoundary();
                    handles[slot] = scene.CreateBody(default);
                    entity = handles[slot];
                }
                SimVec3 point = SimVec3.Metres((long)(random.Next() % 81) - 40, (long)(random.Next() % 21) - 10, (long)(random.Next() % 81) - 40);
                scene.Store.Set(scene.Positions, entity, new Position(point));
                int radius = step % 7 == 0 ? 24 : 1;
                uint layer = step % 4 == 0 ? 0u : step % 4 == 1 ? 1u : step % 4 == 2 ? 0x80000000u : uint.MaxValue;
                scene.Store.Set(scene.Colliders, entity, new Collider(SimFix.FromInt(radius), SimFix.FromInt(radius * 2), layer));
                scene.Sim.CompleteSpatialBoundary();
                string[] logical = GridIndexRegressionAudit.Logical(scene.Grid);
                var rebuilt = new GridSpatialQuery(scene.Store, scene.Positions, scene.Colliders, SimFix.FromInt(8), 16, 1024);
                rebuilt.Rebuild();
                GridIndexRegressionAudit.Equal(logical, GridIndexRegressionAudit.Logical(rebuilt), "Incremental history versus rebuild at step " + step);
                if (step % 17 == 0)
                {
                    byte[] saved = scene.Sim.SaveSnapshot();
                    scene.Store.Set(scene.Positions, entity, new Position(SimVec3.Metres(512, 512, 512)));
                    scene.Sim.CompleteSpatialBoundary();
                    scene.Sim.RestoreSnapshot(saved);
                    GridIndexRegressionAudit.Equal(logical, GridIndexRegressionAudit.Logical(scene.Grid), "Restore at step " + step);
                }
                var bodies = new List<SpatialBody>();
                foreach (EntityId id in scene.Store)
                    bodies.Add(new SpatialBody(id, scene.Store.Read(scene.Positions, id).Value, scene.Store.Read(scene.Colliders, id)));
                var naive = new NaiveSpatialQuery(bodies);
                foreach (SpatialQueryKind kind in Enum.GetValues(typeof(SpatialQueryKind)))
                {
                    SpatialRequest q = GridOwnershipTestFixture.Request(kind, point);
                    GridOwnershipTestFixture.EqualResults(GridOwnershipTestFixture.Results(naive, in q), GridOwnershipTestFixture.Results(scene.Grid, in q), "Legacy step " + step);
                    var masked = new SpatialRequest(q.Kind, q.Origin, q.Radius, q.Direction, q.Range, q.HalfHeight, q.Cosine, layer);
                    List<SpatialHit> expected = QueryModeOracle.Query(bodies, scene.Store, scene.Colliders, masked, point);
                    Assert.That(scene.Grid.Any(in masked), Is.EqualTo(expected.Count != 0));
                    Assert.That(scene.Grid.Nearest(in masked, point, out SpatialHit hit), Is.EqualTo(expected.Count != 0));
                    QueryModeFixture.Same(hit, expected.Count == 0 ? default : expected[0]);
                    SpatialQueryResult result = scene.Grid.AllCapped(in masked, point, 5, output);
                    Assert.That(result.TotalHits, Is.EqualTo(expected.Count));
                    Assert.That(result.Written, Is.EqualTo(Math.Min(expected.Count, 5)));
                    Assert.That(result.Truncated, Is.EqualTo(expected.Count > 5));
                    for (int i = 0; i < result.Written; i++)
                        QueryModeFixture.Same(output[i], expected[i]);
                    comparisons++;
                }
            }
            Assert.That(comparisons, Is.EqualTo(1000));
            TestContext.WriteLine("2bB differential: seed=0x{0:X}; steps=200; five-shape sets=1000; legacy/Any/Nearest/AllCapped; masks=0/1/high/all; restore every 17 steps; logical comparisons=200; mismatches=0.", seed);
        }
    }
}
