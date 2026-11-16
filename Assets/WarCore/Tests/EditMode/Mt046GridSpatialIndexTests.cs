using System;
using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class Mt046GridSpatialIndexTests
    {
        [Test]
        public void ConstructorRejectsZeroCellSize()
        {
            var scene = new GridOwnershipTestFixture(1);
            Assert.Throws<ArgumentOutOfRangeException>(() => new GridSpatialIndex(
                scene.Store, scene.Positions, scene.Colliders, default, 1, 8));
        }

        [Test]
        public void RebuildRejectsBodyAtEntityCapacity()
        {
            var scene = new GridOwnershipTestFixture(2);
            scene.Store.Create();
            EntityId entity = scene.CreateBody(default);
            var index = new GridSpatialIndex(scene.Store, scene.Positions, scene.Colliders,
                SimFix.FromInt(8), 1, 8);
            Assert.That(entity.Id, Is.EqualTo(1));
            Assert.Throws<SimBoundaryException>(() => index.Rebuild());
        }
    }
}
