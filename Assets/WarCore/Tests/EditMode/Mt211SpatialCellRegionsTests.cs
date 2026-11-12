using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    [Category("Step2bB")]
    public sealed class Mt211SpatialCellRegionsTests
    {
        [TestCase(1, 0, 0)]
        [TestCase(0, 1, 0)]
        [TestCase(0, 0, 1)]
        public void SphereFindsBodyBeyondOtherTopCells(int x, int y, int z)
        {
            var store = new EntityStore(3);
            var positions = store.Register<Position>("position");
            var colliders = store.Register<Collider>("collider");
            HybridStorageTests.Add(store, positions, colliders, default);
            HybridStorageTests.Add(store, positions, colliders, SimVec3.Metres(16 * x, 16 * y, 16 * z));
            var center = SimVec3.Metres(32 * x, 32 * y, 32 * z);
            EntityId target = HybridStorageTests.Add(store, positions, colliders, center);
            var grid = new GridSpatialQuery(store, positions, colliders, SimFix.FromRaw(1), 3, 3);
            grid.Rebuild();

            // The sphere spans 33 top cells per axis, which selects region traversal.
            var output = new EntityId[3];
            Assert.That(grid.Sphere(center, SimFix.FromInt(4), output), Is.EqualTo(1));
            Assert.That(output[0], Is.EqualTo(target));
        }
    }
}
