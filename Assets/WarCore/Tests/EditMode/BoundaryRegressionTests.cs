using System;
using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class BoundaryRegressionTests
    {
        [TestCase(1000)]
        [TestCase(2000)]
        [TestCase(3000)]
        [TestCase(4000)]
        [TestCase(5000)]
        public void EveryQueryHasKnownAnswersAtEachKilometre(int distance)
        {
            var grid = SpatialQueryTests.Grid(new[]{new SpatialBody(new EntityId(1, 1), SimVec3.Metres(distance, 0, 3), new Collider(SimFix.FromInt(5), SimFix.FromInt(12))), new SpatialBody(new EntityId(2, 1), SimVec3.Metres(distance + 6, 0, 0), new Collider(SimFix.FromInt(1), SimFix.FromInt(2)))}, 128);
            var entities = new EntityId[2];
            var hits = new RayHit[2];
            Assert.That(grid.Sphere(default, SimFix.FromInt(distance), entities), Is.EqualTo(1));
            Assert.That(entities[0].Id, Is.EqualTo(1));
            Assert.That(grid.Sector(default, SimFix.FromInt(distance), SimVec3.Metres(1, 0, 0), 1000, default, entities), Is.EqualTo(1));
            Assert.That(entities[0].Id, Is.EqualTo(1));
            Assert.That(grid.Capsule(default, SimVec3.Metres(distance - 4, 0, 0), default, entities), Is.EqualTo(1));
            Assert.That(entities[0].Id, Is.EqualTo(1));
            Assert.That(grid.Ray(default, SimVec3.Metres(1, 0, 0), SimFix.FromInt(distance - 4), hits), Is.EqualTo(1));
            Assert.That(hits[0].Entity.Id, Is.EqualTo(1));
            Assert.That(hits[0].Distance, Is.EqualTo(SimFix.FromInt(distance - 4)));
            Assert.That(grid.Ray(default, SimVec3.Metres(1, 0, 0), SimFix.FromRaw((distance - 4) * SimFix.Scale - 1), hits), Is.Zero);
        }

        [Test]
        public void GridCanReuseNewCellsWithoutAllocatingAndCapacityFailureKeepsOldIndex()
        {
            var windows = new AllocationWindow();
            do
            {
                var store = new EntityStore();
                var positions = store.Register<Position>("p");
                var colliders = store.Register<Collider>("c");
                EntityId id = store.Create();
                store.Set(positions, id, default);
                store.Set(colliders, id, new Collider(SimFix.FromInt(1), SimFix.FromInt(2)));
                var grid = new GridSpatialQuery(store, positions, colliders, SimFix.FromInt(8), 4, 16);
                grid.Rebuild();
                var results = new EntityId[1];
                grid.Sphere(default, SimFix.FromInt(2), results);
                var meter = new AllocationMeter();
                windows.Begin(meter);
                int matches = 0;
                for (int i = 0; i < 500; i++)
                {
                    SimVec3 location = SimVec3.Metres(i % 2 == 0 ? -2000 : 2000, 0, 0);
                    store.Get(positions, id).Value = location;
                    grid.Upsert(id);
                    matches += grid.Sphere(location, SimFix.FromInt(2), results);
                }

                AllocationSample allocation = windows.End();
                if (!windows.NeedsRetry)
                    AllocationAssertions.Zero(allocation);
                Assert.That(matches, Is.EqualTo(500));
                store.Set(colliders, id, new Collider(SimFix.FromInt(8), SimFix.FromInt(16)));
                Assert.Throws<SimBoundaryException>(() => grid.Upsert(id));
                Assert.That(grid.Sphere(SimVec3.Metres(2004, 0, 0), default, results), Is.Zero, "Rejected collider must not replace the published radius.");
                Assert.That(grid.Sphere(SimVec3.Metres(2000, 0, 0), default, results), Is.EqualTo(1));
            }
            while (windows.NeedsRetry);
        }

        [Test]
        public void TinyRayDirectionHasSameHandComputedDistanceAsLargeDirection()
        {
            var grid = SpatialQueryTests.Grid(new[]{new SpatialBody(new EntityId(1, 1), SimVec3.Metres(3, 0, 4), default)});
            var tiny = new SimVec3(SimFix.FromRaw(3), default, SimFix.FromRaw(4));
            var hits = new RayHit[1];
            Assert.That(grid.Ray(default, tiny, SimFix.FromInt(5), hits), Is.EqualTo(1));
            Assert.That(hits[0].Distance, Is.EqualTo(SimFix.FromInt(5)));
            Assert.That(grid.Ray(default, SimVec3.Metres(3, 0, 4), SimFix.FromInt(5), hits), Is.EqualTo(1));
            Assert.That(hits[0].Distance, Is.EqualTo(SimFix.FromInt(5)));
            var diagonal = SpatialQueryTests.Grid(new[]{new SpatialBody(new EntityId(1, 1), SimVec3.Metres(1, 0, 1), default)});
            Assert.That(diagonal.Ray(default, new SimVec3(SimFix.FromRaw(1), default, SimFix.FromRaw(1)), SimFix.FromInt(2), hits), Is.EqualTo(1));
            Assert.That(hits[0].Distance.Raw, Is.EqualTo(92681));
        }

        [Test]
        public void FarSectorCornerWithLargeDirectionAndColliderDoesNotOverflow()
        {
            var grid = SpatialQueryTests.Grid(new[]{new SpatialBody(new EntityId(1, 1), SimVec3.Metres(16000, 0, 5000), new Collider(SimFix.FromInt(5000), SimFix.FromInt(10000)))}, 4096);
            var direction = new SimVec3(SimFix.FromRaw(16384 * SimFix.Scale - 1), default, SimFix.FromRaw(1));
            var results = new EntityId[1];
            Assert.That(grid.Sector(default, SimFix.FromInt(16000), direction, 999, default, results), Is.EqualTo(1));
            Assert.That(results[0].Id, Is.EqualTo(1));
        }

        [Test]
        public void SubUnitCapsuleCrossSectionMustNotRoundDownBeforeIntersection()
        {
            var center = new SimVec3(SimFix.FromRaw(1), SimFix.FromRaw(2), SimFix.FromRaw(1));
            var grid = SpatialQueryTests.Grid(new[]{new SpatialBody(new EntityId(1, 1), center, new Collider(SimFix.FromRaw(2), SimFix.FromRaw(4)))});
            var entities = new EntityId[1];
            Assert.That(grid.Cylinder(default, default, SimFix.FromRaw(1), entities), Is.EqualTo(1));
            Assert.That(grid.Sector(default, default, SimVec3.Metres(1, 0, 0), 1000, SimFix.FromRaw(1), entities), Is.EqualTo(1));
        }

        [Test]
        public void CylinderRimTangencyUsesCapsuleSurfaceAndOneRawUnitOutsideMisses()
        {
            var bodies = new[]{new SpatialBody(new EntityId(1, 1), SimVec3.Metres(6, 6, 0), new Collider(SimFix.FromInt(5), SimFix.FromInt(10))), new SpatialBody(new EntityId(2, 1), new SimVec3(SimFix.FromRaw(6 * SimFix.Scale + 1), SimFix.FromInt(6), default), new Collider(SimFix.FromInt(5), SimFix.FromInt(10)))};
            var grid = SpatialQueryTests.Grid(bodies);
            var output = new EntityId[2];
            Assert.That(grid.Cylinder(default, SimFix.FromInt(3), SimFix.FromInt(2), output), Is.EqualTo(1));
            Assert.That(output[0].Id, Is.EqualTo(1));
        }

        [Test]
        public void PositionRefWritesAlsoEnforceWorldBounds()
        {
            var store = new EntityStore();
            var type = store.Register<Position>("p");
            EntityId entity = store.Create();
            store.Set(type, entity, default);
            Assert.Throws<ArgumentOutOfRangeException>(() => store.Get(type, entity).Value = SimVec3.Metres(0, -16385, 0));
            Assert.That(store.Get(type, entity).Value, Is.EqualTo(default(SimVec3)));
        }

        [Test]
        public void CumulativeDropsAloneChangeHashAfterAllPayloadsExpire()
        {
            var a = new SimEventBuffer(1, 2, 2);
            var b = new SimEventBuffer(1, 2, 2);
            var item = new SimEvent(new EventTypeId(10));
            for (int tick = 1; tick <= 4; tick++)
            {
                a.BeginTick(tick);
                b.BeginTick(tick);
                if (tick == 1)
                {
                    a.Publish(item);
                    a.Publish(item);
                    a.Publish(item);
                }

                if (tick <= 2)
                {
                    b.Publish(item);
                    b.Publish(item);
                }

                a.EndTick();
                b.EndTick();
            }

            Assert.That(a.Read(4).Length, Is.Zero);
            Assert.That(b.Read(4).Length, Is.Zero);
            Assert.That(a.NextSequence, Is.EqualTo(b.NextSequence));
            Assert.That(a.EventsDroppedThisTick, Is.EqualTo(b.EventsDroppedThisTick));
            Assert.That(a.EventsDroppedTotal, Is.EqualTo(1));
            Assert.That(b.EventsDroppedTotal, Is.Zero);
            Assert.That(Fixtures.Hash(a), Is.Not.EqualTo(Fixtures.Hash(b)));
            Assert.That(typeof(SimContext).GetProperty("Events"), Is.Null);
        }
    }
}
