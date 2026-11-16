using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class Mt076SpatialGridScanBoundsTests
    {
        [TestCase(5, 1, 1)]
        [TestCase(1, 5, 1)]
        [TestCase(1, 1, 5)]
        public void SphereExcludesCandidatesInAdjacentUpperCells(int x, int y, int z)
        {
            var fixture = new QueryModeFixture(4 * SimFix.Scale, 2, 16);
            var center = SimVec3.Metres(1, 1, 1);
            EntityId hit = fixture.Add(center);
            fixture.Add(SimVec3.Metres(x, y, z));
            var output = new EntityId[2];

            Assert.That(fixture.Grid.Sphere(center, SimFix.FromInt(1), output), Is.EqualTo(1));
            Assert.That(output[0], Is.EqualTo(hit));
            Assert.That(fixture.Grid.LastCandidateCount, Is.EqualTo(1));
        }
    }
}
