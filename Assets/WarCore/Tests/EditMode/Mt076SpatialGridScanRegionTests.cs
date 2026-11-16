using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    [Category("Step2bB")]
    public sealed class Mt076SpatialGridScanRegionTests
    {
        [Test]
        public void TinyGridSphereIncludesBothPartitionsAndClosedLowerBoundary()
        {
            var scene = new QueryModeFixture(cellRaw: 1, capacity: 2, memberships: 2);
            SimVec3 center = SimVec3.Metres(100, 100, 100);
            EntityId stationary = scene.Add(SimVec3.Metres(96, 100, 100), new Collider(default, default, 1, isStatic: true));
            EntityId unit = scene.Add(center);
            var hits = new EntityId[2];

            Assert.That(scene.Grid.Sphere(center, SimFix.FromInt(4), hits), Is.EqualTo(2));
            Assert.That(hits, Is.EqualTo(new[] { stationary, unit }));
            Assert.That(scene.Counters.StaticRegionNodes, Is.EqualTo(1));
            Assert.That(scene.Counters.UnitRegionNodes, Is.EqualTo(1));
            Assert.That(scene.Counters.WalkCalls, Is.Zero);
        }
    }
}
