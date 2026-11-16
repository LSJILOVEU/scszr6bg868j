using System;
using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class Mt200GridSpatialIndexTests
    {
        [TestCase(true)]
        [TestCase(false)]
        public void ConstructorRejectsForeignComponentHandles(bool foreignPositions)
        {
            var scene = new GridOwnershipTestFixture(capacity: 1);
            var foreign = new GridOwnershipTestFixture(capacity: 1);
            var positions = foreignPositions ? foreign.Positions : scene.Positions;
            var colliders = foreignPositions ? scene.Colliders : foreign.Colliders;

            Assert.Throws<ArgumentException>(() => new GridSpatialQuery(scene.Store, positions, colliders,
                SimFix.FromInt(8), maxEntities: 1, maxMemberships: 16));
        }

        [Test]
        public void RebuildRejectsBodyAtEntityCapacity()
        {
            var scene = new GridOwnershipTestFixture(capacity: 2);
            scene.CreateBody(default);
            EntityId overflow = scene.CreateBody(default);
            var grid = new GridSpatialQuery(scene.Store, scene.Positions, scene.Colliders,
                SimFix.FromInt(8), maxEntities: 1, maxMemberships: 16);

            Assert.That(overflow.Id, Is.EqualTo(1));
            Assert.Throws<SimBoundaryException>(() => grid.Rebuild());
        }

        [TestCase(-1)]
        [TestCase(1)]
        public void UpsertRejectsEntityOutsideCapacity(int id)
        {
            var scene = new GridOwnershipTestFixture(capacity: 1);

            Assert.Throws<ArgumentOutOfRangeException>(() => scene.Grid.Upsert(new EntityId(id, 1)));
        }
    }
}
