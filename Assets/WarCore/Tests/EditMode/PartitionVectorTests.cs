using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    [Category("Partitions")]
    public sealed class PartitionVectorTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void EveryFrozenSpatialVectorRunsThroughEitherPartition(bool stationary)
        {
            var inputs = new A2SpatialInputs(0x5100733);
            VectorJson rows = VectorJson.Load("spatial-narrowphase-vectors.json")["cases"];
            for (int i = 0; i < rows.Count; i++)
            {
                A2SpatialInputs.Sample sample = inputs.Vector(rows[i]);
                var store = new EntityStore(1);
                var p = store.Register<Position>("position");
                var c = store.Register<Collider>("collider");
                EntityId entity = store.Create();
                store.Set(p, entity, new Position(sample.Body.Position));
                store.Set(c, entity, sample.Body.Collider.WithStatic(stationary));
                var index = new GridSpatialIndex(store, p, c, SimFix.FromInt(8), 1, 64, readLiveLayers: false);
                index.Rebuild();
                var query = new GridSpatialQuery(index, new GridQueryContext(1));
                var counters = new SpatialQueryDiagnostics();
                query.Diagnostics = counters;
                var ids = new EntityId[1];
                var rays = new RayHit[1];
                int expected = rows[i]["expectHit"].Boolean ? 1 : 0;
                Assert.That(GridOwnershipTestFixture.Execute(query, sample.Request, ids, rays), Is.EqualTo(expected), sample.Source);
                if (expected != 0)
                {
                    Assert.That(sample.Request.Kind == SpatialQueryKind.Ray ? rays[0].Entity : ids[0], Is.EqualTo(entity));
                    if (sample.Request.Kind == SpatialQueryKind.Ray)
                        Assert.That(rays[0].Distance.Raw, Is.EqualTo(rows[i]["expectDistanceRaw"].Integer), sample.Source);
                }
                Assert.That(query.Any(sample.Request), Is.EqualTo(expected != 0));
                Assert.That(query.Nearest(sample.Request, out SpatialHit nearest), Is.EqualTo(expected != 0));
                var hits = new SpatialHit[1];
                Assert.That(query.AllCapped(sample.Request, 1, hits).TotalHits, Is.EqualTo(expected));
                if (expected != 0 && sample.Request.Kind == SpatialQueryKind.Ray)
                {
                    Assert.That(nearest.Distance.Raw, Is.EqualTo(rows[i]["expectDistanceRaw"].Integer), sample.Source);
                    Assert.That(hits[0].Distance.Raw, Is.EqualTo(nearest.Distance.Raw));
                }
                Assert.That(stationary ? counters.UnitMembers : counters.StaticMembers, Is.Zero);
            }
            Assert.That(rows.Count, Is.EqualTo(733));
            TestContext.WriteLine("S1 spatial vectors: static={0}; rows=733; legacy/Any/Nearest/AllCapped; frozen JSON unchanged; mismatches=0.", stationary);
        }

        [Test]
        public void PreviousSnapshotVersionIsRejectedBeforeAuthorityChanges()
        {
            var scene = new PartitionScene(2, 8);
            scene.Add(default, true, 2);
            byte[] bytes = scene.Sim.SaveSnapshot();
            ulong before = scene.Sim.StateHash();
            bytes[4] = 3;
            var error = Assert.Throws<SimSnapshotException>(() => scene.Sim.RestoreSnapshot(bytes));
            Assert.That(error.Message, Is.EqualTo("Snapshot version mismatch: expected 4, received 3."));
            Assert.That(scene.Sim.StateHash(), Is.EqualTo(before));
            scene.Sim.Step(default);
        }

        [TestCase(-1, 0)]
        [TestCase(int.MaxValue, 0)]
        [TestCase(1, -1)]
        [TestCase(1, 3)]
        [TestCase(1, int.MaxValue)]
        public void StaticBudgetsRejectInvalidArithmeticBeforeAllocation(int members, int cells)
        {
            Assert.Throws<SimBoundaryException>(() => new SimConfig(staticSpatialMembershipCapacity: members, staticSpatialCellCapacity: cells));
        }
    }
}
