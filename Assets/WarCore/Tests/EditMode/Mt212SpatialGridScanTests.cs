using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class Mt212SpatialGridScanTests
    {
        [TestCase(16, 2)]
        [TestCase(17, 1)]
        public void SphereCandidatesApplyBoundsFilterOnlyForCoarseCells(int radius, int candidates)
        {
            var fixture = new QueryModeFixture(8 * SimFix.Scale, 2, 64);
            EntityId inside = fixture.Add(SimVec3.Metres(1, 1, 1));
            fixture.Add(SimVec3.Metres(18, 1, 1));
            var output = new EntityId[2];

            // Radius 16 scans 8-unit cells; radius 17 scans 128-unit cells.
            Assert.That(fixture.Grid.Sphere(default, SimFix.FromInt(radius), output), Is.EqualTo(1));
            Assert.That(output[0], Is.EqualTo(inside));
            Assert.That(fixture.Grid.LastCandidateCount, Is.EqualTo(candidates));
        }
    }
}
