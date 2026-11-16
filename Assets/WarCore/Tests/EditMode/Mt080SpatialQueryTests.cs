using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    [TestFixture]
    public sealed class Mt080SpatialQueryTests
    {
        [TestCase(false, 2), TestCase(true, 0)]
        public void WithHeightAcceptsExactMinimum(bool box, int minimumHeight)
        {
            Collider source = box
                ? Collider.Box(SimFix.FromInt(1), SimFix.FromInt(3), SimFix.FromInt(2), BoxYaw.Identity)
                : new Collider(SimFix.FromInt(1), SimFix.FromInt(3));
            SimFix minimum = SimFix.FromInt(minimumHeight);
            Collider copy = default;

            Assert.DoesNotThrow(() => copy = source.WithHeight(minimum));
            Assert.That(copy.Height, Is.EqualTo(minimum));
        }

        [Test]
        public void StableWorldFullRayOrdersHitsByIncreasingDistance()
        {
            var store = new EntityStore(2, IdentityMode.StableWorld);
            var positions = store.Register<Position>("p");
            var colliders = store.Register<Collider>("c");
            EntityId far = store.Create(StableEntityKey.Placed(1, 0));
            EntityId near = store.Create(StableEntityKey.Placed(1, 1));
            store.Set(positions, far, new Position(SimVec3.Metres(6, 0, 0)));
            store.Set(positions, near, new Position(SimVec3.Metres(3, 0, 0)));
            var collider = new Collider(SimFix.FromInt(1), SimFix.FromInt(2));
            store.Set(colliders, far, collider);
            store.Set(colliders, near, collider);
            var grid = new GridSpatialQuery(store, positions, colliders, SimFix.FromInt(8), 2, 64);
            grid.Rebuild();
            var hits = new RayHit[2];

            Assert.That(grid.Ray(default, SimVec3.Metres(1, 0, 0), SimFix.FromInt(8), hits), Is.EqualTo(2));
            Assert.That(hits[0].Distance, Is.EqualTo(SimFix.FromInt(2)));
            Assert.That(hits[1].Distance, Is.EqualTo(SimFix.FromInt(5)));
            Assert.That(hits[0].Entity, Is.EqualTo(near));
            Assert.That(hits[1].Entity, Is.EqualTo(far));
        }
    }
}
