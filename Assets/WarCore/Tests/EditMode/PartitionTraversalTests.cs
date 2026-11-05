using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    [Category("PartitionPerf")]
    public sealed class PartitionTraversalTests
    {
        internal static GridSpatialQuery Grid(bool split, out SpatialQueryDiagnostics counters)
        {
            var store = new EntityStore(128);
            var positions = store.Register<Position>("position");
            var colliders = store.Register<Collider>("collider");
            for (int i = 0; i < 128; i++)
            {
                EntityId entity = store.Create();
                store.Set(positions, entity, new Position(SimVec3.Metres(i % 32, 0, i / 32)));
                store.Set(colliders, entity, new Collider(SimFix.Ratio(1, 4), SimFix.FromInt(2),
                    i < 64 ? 2u : 1u, isStatic: split && i < 64));
            }
            var index = new GridSpatialIndex(store, positions, colliders, SimFix.FromInt(3), 128, readLiveLayers: false);
            index.Rebuild();
            var grid = new GridSpatialQuery(index, new GridQueryContext(128));
            counters = new SpatialQueryDiagnostics();
            grid.Diagnostics = counters;
            return grid;
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void BothPartitionsShareOneGeometricTraversal(int kind)
        {
            GridSpatialQuery single = Grid(false, out SpatialQueryDiagnostics one);
            GridSpatialQuery dual = Grid(true, out SpatialQueryDiagnostics two);
            var origin = SimVec3.Metres(-4, 0, 1);
            SpatialRequest request = kind == 0 ? SpatialQueries.Ray(origin, SimVec3.Metres(1, 0, 0), SimFix.FromInt(40))
                : kind == 1 ? SpatialQueries.Sphere(origin, SimFix.FromInt(12))
                : SpatialQueries.Sector(origin, SimFix.FromInt(12), SimVec3.Metres(1, 0, 0), 600, SimFix.FromInt(2));
            var a = new SpatialHit[128];
            var b = new SpatialHit[128];
            SpatialQueryResult first = single.AllCapped(in request, 128, a);
            SpatialQueryResult second = dual.AllCapped(in request, 128, b);
            Assert.That(second.TotalHits, Is.EqualTo(first.TotalHits));
            Assert.That(first.TotalHits, Is.GreaterThan(0));
            for (int i = 0; i < first.Written; i++)
            {
                Assert.That(b[i].Entity, Is.EqualTo(a[i].Entity));
                Assert.That(b[i].Distance.Raw, Is.EqualTo(a[i].Distance.Raw));
            }
            Assert.That(one.WalkCalls, Is.GreaterThan(0), "Traversal counter needs a nonempty control.");
            Assert.That(two.WalkCalls, Is.EqualTo(one.WalkCalls), "The same cells must not be traversed twice.");
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void RejectedAndEmptyPartitionsDoNoTraversalOrMemberWork(int kind)
        {
            GridSpatialQuery grid = Grid(true, out SpatialQueryDiagnostics work);
            var output = new SpatialHit[128];
            foreach (uint mask in new[] { 3u, 2u, 1u, 4u, 0u })
            {
                SpatialRequest request = Request(kind, mask);
                grid.AllCapped(in request, 128, output);
                long stationary = work.StaticLookups + work.StaticRegionNodes + work.StaticMembers;
                long unit = work.UnitLookups + work.UnitRegionNodes + work.UnitMembers;
                if ((mask & 2) == 0)
                    Assert.That(stationary, Is.Zero, "Rejected static partition must do no work.");
                else
                    Assert.That(stationary, Is.GreaterThan(0), "Static work needs a positive control.");
                if ((mask & 1) == 0)
                    Assert.That(unit, Is.Zero, "Rejected unit partition must do no work.");
                else
                    Assert.That(unit, Is.GreaterThan(0), "Unit work needs a positive control.");
                Assert.That(work.TraversalStarts, Is.EqualTo((mask & 3) == 0 ? 0 : 1));
            }
            grid = Grid(false, out work);
            SpatialRequest all = Request(kind, uint.MaxValue);
            grid.AllCapped(in all, 128, output);
            Assert.That(work.StaticLookups + work.StaticRegionNodes + work.StaticMembers, Is.Zero);
            Assert.That(work.UnitLookups + work.UnitMembers, Is.GreaterThan(0));
        }

        private static SpatialRequest Request(int kind, uint mask)
        {
            var origin = SimVec3.Metres(-4, 0, 1);
            return kind == 0 ? SpatialQueries.Ray(origin, SimVec3.Metres(1, 0, 0), SimFix.FromInt(40), mask)
                : kind == 1 ? SpatialQueries.Sphere(origin, SimFix.FromInt(12), mask)
                : SpatialQueries.Sector(origin, SimFix.FromInt(12), SimVec3.Metres(1, 0, 0), 600, SimFix.FromInt(2), mask);
        }

        [Test]
        public void PublishedGeometryViewTracksBothBanksAndSnapshotRebuild()
        {
            var scene = new PartitionScene(16, 3);
            EntityId wall = scene.Add(SimVec3.Metres(2, 1, 1), true, 2);
            scene.Add(SimVec3.Metres(5, 1, 1), false, 1);
            scene.Sim.CompleteSpatialBoundary();
            byte[] snapshot = scene.Sim.SaveSnapshot();
            for (int i = 0; i < 8; i++)
            {
                scene.Sim.RequestColliderStatic(wall, i % 2 != 0);
                scene.Sim.RequestPosition(wall, SimVec3.Metres(i + 2, 1, 1));
                scene.Sim.Step(default);
                CheckGeometry(scene.State);
            }
            scene.Sim.RestoreSnapshot(snapshot);
            CheckGeometry(scene.State);
            Assert.That(scene.State.Bodies.Geometry[wall.Id].Position, Is.EqualTo(SimVec3.Metres(2, 1, 1)));
            Assert.That(scene.State.Bodies.Geometry[wall.Id].Collider.IsStatic, Is.True);
        }

        private static void CheckGeometry(SpatialGridState state)
        {
            for (int id = 0; id < state.Bodies.Length; id++)
            {
                if (!state.Present[id])
                    continue;
                SpatialBody view = state.Bodies.Geometry[id];
                Assert.That(view.Entity, Is.EqualTo(state.Bodies.Entities[id]));
                Assert.That(view.Position, Is.EqualTo(state.Bodies.Positions[id]));
                Assert.That(view.Collider, Is.EqualTo(state.Bodies.Colliders[id]));
            }
        }
    }
}
