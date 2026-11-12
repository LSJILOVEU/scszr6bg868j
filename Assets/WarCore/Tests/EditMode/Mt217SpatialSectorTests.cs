using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class Mt217SpatialSectorTests
    {
        [TestCase(-1)]
        [TestCase(1)]
        public void SectorIncludesVerticalSurfaceTangency(int y)
        {
            var body = new SpatialBody(new EntityId(0, 1), SimVec3.Metres(1, y, 0),
                new Collider(SimFix.FromInt(1), SimFix.FromInt(2)));
            var query = SpatialQueryTests.Grid(new[]{body});
            var output = new EntityId[1];

            Assert.That(query.Sector(default, SimFix.FromInt(2), SimVec3.Metres(1, 0, 0), 600, default, output), Is.EqualTo(1));
            Assert.That(output[0], Is.EqualTo(body.Entity));
        }

        [TestCase(-4)]
        [TestCase(4)]
        public void SectorRejectsBodyBeyondReducedCrossSection(int y)
        {
            var body = new SpatialBody(new EntityId(0, 1), SimVec3.Metres(8, y, 0),
                new Collider(SimFix.FromInt(5), SimFix.FromInt(10)));
            var query = SpatialQueryTests.Grid(new[]{body});
            var output = new EntityId[1];

            // At height four, the cross-section radius is three: 4 + 3 < 8.
            Assert.That(query.Sector(default, SimFix.FromInt(4), SimVec3.Metres(1, 0, 0), 600, default, output), Is.EqualTo(0));
        }
    }
}
