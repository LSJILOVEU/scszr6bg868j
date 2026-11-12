using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    [Category("PartitionPerf")]
    public sealed class Mt213SpatialGridScanTests
    {
        [Test]
        public void TinyGridSphereReturnsBothPartitionRootsAndCountsEachOnce()
        {
            var store = new EntityStore(2);
            var positions = store.Register<Position>("position");
            var colliders = store.Register<Collider>("collider");
            EntityId stationary = HybridStorageTests.Add(store, positions, colliders, SimVec3.Metres(1, 0, 0));
            EntityId unit = HybridStorageTests.Add(store, positions, colliders, SimVec3.Metres(2, 0, 0));
            store.Set(colliders, stationary, new Collider(default, default).WithStatic(true));
            var grid = new GridSpatialQuery(store, positions, colliders, SimFix.FromRaw(1), 2, 2);
            grid.Rebuild();
            grid.Diagnostics = new SpatialQueryDiagnostics();
            var output = new EntityId[2];

            // Each point occupies one top-level cell; the wide sphere requires a region scan.
            Assert.That(grid.Sphere(default, SimFix.FromInt(8), output), Is.EqualTo(2));
            Assert.That(output, Is.EquivalentTo(new[] { stationary, unit }));
            Assert.That(grid.Diagnostics.StaticRegionNodes, Is.EqualTo(1));
            Assert.That(grid.Diagnostics.UnitRegionNodes, Is.EqualTo(1));
            Assert.That(grid.Diagnostics.RegionNodes, Is.EqualTo(2));
        }
    }
}
