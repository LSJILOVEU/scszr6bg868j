using System;
using NUnit.Framework;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    [Category("Partitions")]
    public sealed class PartitionResourceTests
    {
        [TestCase(3)]
        [TestCase(8)]
        public void TwoThousandBodiesShareColumnsWithSeparateReservedIndexPayloads(int cell)
        {
            var scene = new PartitionScene(2000, cell);
            for (int i = 0; i < 2000; i++)
                scene.Add(new SimVec3(SimFix.FromInt(i % 50 * 3), SimFix.Ratio(3, 2), SimFix.FromInt(i / 50 * 3)), i < 400, i < 400 ? 2u : 1u);
            scene.Sim.CompleteSpatialBoundary();
            scene.Sim.SpatialIndex.Rebuild();
            SpatialGridState active = scene.State;
            Assert.That(active.Static.BodyCount, Is.EqualTo(400));
            Assert.That(active.Unit.BodyCount, Is.EqualTo(1600));
            Assert.That(active.Static.Table, Is.Not.SameAs(active.Unit.Table));
            Assert.That(active.Bodies.Entities.Length, Is.EqualTo(2000));
            Assert.That(active.Bodies.Positions.Length, Is.EqualTo(2000));
            Assert.That(active.Bodies.Colliders.Length, Is.EqualTo(2000));
            Assert.That(HybridStorageAudit.Payload(new byte[4096]), Is.EqualTo(4096));
            long measured = HybridStorageAudit.Payload(scene.Sim.SpatialIndex);
            Assert.That(scene.Sim.SpatialIndex.AllocatedBytes, Is.EqualTo(measured), "Independent array element sizes and reference deduplication.");
            long context = HybridStorageAudit.Payload(scene.Grid.QueryContext);
            TestContext.WriteLine("S1 memory: entities=2000; static=400; unit=1600; cell={0}m; index_allocated={1}; index_used_both_banks={2}; active_used={3}; context={4}; static_allocated_per_bank={5}; static_used={6}; unit_allocated_per_bank={7}; unit_used={8}; shared_body_columns_per_bank={9}; headers/store excluded.",
                cell, measured, scene.Sim.SpatialIndex.UsedBytes, scene.Sim.SpatialIndex.ActiveUsedBytes, context,
                active.Static.AllocatedBytes, active.Static.UsedBytes, active.Unit.AllocatedBytes, active.Unit.UsedBytes, active.Bodies.AllocatedBytes);
            GridIndexRegressionAudit.Logical(scene.Grid);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void MigrationBudgetFailurePublishesNeitherPartitionAndCanBeRetried(bool toStatic)
        {
            var scene = new PartitionScene(4, 8, unitMembers: 1, staticMembers: 1, cells: 64);
            EntityId a = scene.Add(SimVec3.Metres(1, 1, 1), true, 2, 0);
            EntityId b = scene.Add(SimVec3.Metres(17, 1, 1), false, 1, 0);
            scene.Sim.CompleteSpatialBoundary();
            string[] before = GridIndexRegressionAudit.Logical(scene.Grid);
            EntityId migrate = toStatic ? b : a;
            Collider old = scene.Sim.Entities.Read(scene.C, migrate);
            scene.Sim.Entities.Set(scene.C, migrate, old.WithStatic(toStatic));
            var error = Assert.Throws<SimBoundaryException>(() => scene.Sim.CompleteSpatialBoundary());
            Assert.That(error.Message, Does.Contain("membership capacity 1"));
            GridIndexRegressionAudit.Equal(before, GridIndexRegressionAudit.Logical(scene.Grid), "No half-migration on capacity failure.");
            EntityId removed = toStatic ? a : b;
            scene.Sim.Entities.Destroy(removed);
            scene.Sim.CompleteSpatialBoundary();
            Assert.That(scene.State.Static.BodyCount, Is.EqualTo(toStatic ? 1 : 0));
            Assert.That(scene.State.Unit.BodyCount, Is.EqualTo(toStatic ? 0 : 1));
            GridIndexRegressionAudit.Logical(scene.Grid);
        }

        [Test]
        public void WarmQueriesSyncMigrationsAndHashesHaveStrictDirectZeroAllocation()
        {
            var scene = new PartitionScene(2000, 8);
            for (int i = 0; i < 2000; i++)
                scene.Add(new SimVec3(SimFix.FromInt(i % 50 * 3), SimFix.Ratio(3, 2), SimFix.FromInt(i / 50 * 3)), i < 400, i < 400 ? 2u : 1u);
            scene.Sim.CompleteSpatialBoundary();
            var hits = new SpatialHit[512];
            var ids = new EntityId[2000];
            var rays = new RayHit[2000];
            var requests = new SpatialRequest[5];
            for (int i = 0; i < requests.Length; i++)
                requests[i] = GridOwnershipTestFixture.Request((SpatialQueryKind)i, SimVec3.Metres(30, 1, 30));
            scene.Sim.CompleteWarmup();
            for (int i = 0; i < 32; i++)
                Cycle(scene, i, requests, hits, ids, rays);
            var meter = new AllocationMeter();
            if (meter.Basis != AllocationBasis.Direct)
                Assert.Inconclusive("Strict zero requires the shared direct counter positive control.");
            Assert.That(meter.DirectControlDelta.Bytes, Is.GreaterThanOrEqualTo(32768));
            long bytes = 0;
            for (int i = 0; i < 300; i++)
            {
                AllocationSample before = meter.Read();
                Cycle(scene, i, requests, hits, ids, rays);
                AllocationSample sample = meter.Difference(before);
                AllocationAssertions.Check(sample);
                bytes += sample.Bytes;
            }
            Assert.That(bytes, Is.Zero);
            TestContext.WriteLine("S1 warm allocation: ticks=300; entities=2000; every tick static migration + position request + Step + five shapes x legacy/Any/Nearest/AllCapped + StateHash/v2; allocation=direct-bytes; allocated_bytes={0}; positive_control={1}.", bytes, meter.DirectControlDelta.Bytes);
        }

        private static void Cycle(PartitionScene scene, int tick, SpatialRequest[] requests, SpatialHit[] hits, EntityId[] ids, RayHit[] rays)
        {
            var entity = new EntityId(0, 1);
            scene.Sim.RequestColliderStatic(entity, tick % 2 == 0);
            scene.Sim.RequestPosition(entity, SimVec3.Metres(1 + tick % 2, 1, 1));
            scene.Sim.Step(default);
            foreach (SpatialRequest request in requests)
            {
                GridOwnershipTestFixture.Execute(scene.Grid, in request, ids, rays);
                scene.Grid.Any(in request);
                scene.Grid.Nearest(in request, out _);
                scene.Grid.AllCapped(in request, 512, hits);
            }
            scene.Sim.StateHash();
            scene.Sim.StateHashV2();
        }

        [Test]
        public void BothPartitionMasksClearTheirLastBitAndQueriesNeverWriteEitherIndex()
        {
            var scene = new PartitionScene(4, 8);
            EntityId a = scene.Add(SimVec3.Metres(1, 1, 1), true, 0x80000000);
            EntityId b = scene.Add(SimVec3.Metres(2, 1, 1), false, 2);
            scene.Sim.CompleteSpatialBoundary();
            var query = SpatialQueries.Sphere(default, SimFix.FromInt(40));
            var output = new SpatialHit[4];
            for (int i = 0; i < 3; i++)
            {
                string[] before = QueryReadOnlyTests.Capture(scene.Grid);
                scene.Grid.Any(in query);
                scene.Grid.Nearest(in query, out _);
                scene.Grid.AllCapped(in query, 4, output);
                GridIndexRegressionAudit.Unchanged(before, scene.Grid);
                if (i == 0)
                    scene.Sim.Entities.Set(scene.C, a, scene.Sim.Entities.Read(scene.C, a).WithLayer(0).WithStatic(false));
                else if (i == 1)
                    scene.Sim.Entities.Destroy(b);
                scene.Sim.CompleteSpatialBoundary();
                GridIndexRegressionAudit.Logical(scene.Grid);
                Assert.That(scene.State.Static.LayerUnion, Is.Zero);
            }
            Assert.That(scene.State.Unit.LayerUnion, Is.Zero);
        }
    }

    internal sealed class PartitionScene
    {
        internal readonly Simulation Sim;
        internal readonly ComponentType<Position> P;
        internal readonly ComponentType<Collider> C;
        internal readonly GridSpatialQuery Grid;
        internal SpatialGridState State => (SpatialGridState)GridIndexRegressionAudit.Field(Sim.SpatialIndex, "active");

        internal PartitionScene(int capacity, int cell, int unitMembers = 32768, int staticMembers = 16384, int cells = 0)
        {
            Sim = new Simulation(0x5100200, config: new SimConfig(2, 16, 16, capacity, 16,
                spatialMembershipCapacity: unitMembers, spatialCellCapacity: cells,
                staticSpatialMembershipCapacity: staticMembers, staticSpatialCellCapacity: cells));
            P = Sim.Entities.Register<Position>("position", new PositionSerializer());
            C = Sim.Entities.Register<Collider>("collider", new ColliderSerializer());
            var view = (SpatialQueryView)Sim.ConfigureSpatial(P, C, SimFix.FromInt(cell));
            Grid = new GridSpatialQuery(Sim.SpatialIndex, view.QueryContext);
        }

        internal EntityId Add(SimVec3 point, bool stationary, uint layer, long radius = SimFix.Scale / 4)
        {
            EntityId id = Sim.Entities.Create();
            Sim.Entities.Set(P, id, new Position(point));
            Sim.Entities.Set(C, id, new Collider(SimFix.FromRaw(radius), SimFix.FromRaw(radius == 0 ? 0 : 3 * SimFix.Scale), layer).WithStatic(stationary));
            return id;
        }
    }
}
