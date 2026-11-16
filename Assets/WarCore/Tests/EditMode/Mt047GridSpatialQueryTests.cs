using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class Mt047GridSpatialQueryTests
    {
        [Test]
        public void DefaultBudgetsReserve4096EntitiesAnd32768Memberships()
        {
            var scene = new GridOwnershipTestFixture();
            var grid = new GridSpatialQuery(scene.Store, scene.Positions, scene.Colliders, SimFix.FromInt(8));
            Assert.That(grid.Index.Capacity, Is.EqualTo(4096));
            Assert.That(grid.Index.MembershipCapacity, Is.EqualTo(32768));
        }

        [Test]
        public void DefaultContextCapacityEqualsMaxEntities()
        {
            var scene = new GridOwnershipTestFixture(7);
            var grid = new GridSpatialQuery(scene.Store, scene.Positions, scene.Colliders,
                SimFix.FromInt(8), maxEntities: 7, maxMemberships: 1);
            Assert.That(grid.QueryContext.Capacity, Is.EqualTo(7));
        }
    }
}
