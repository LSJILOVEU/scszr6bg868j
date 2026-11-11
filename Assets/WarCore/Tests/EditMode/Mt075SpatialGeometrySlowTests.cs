using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    [Category("NarrowPhase")]
    public sealed class Mt075SpatialGeometrySlowTests
    {
        [TestCase(-1000, true)]
        [TestCase(-999, false)]
        public void SlowSectorIncludesPointDirectlyBehindOnlyAtFullAngle(int cosine, bool expected)
        {
            var body = new SpatialBody(new EntityId(1, 1), SimVec3.Metres(-1, 0, 0), default);
            SpatialRequest request = SpatialQueries.Sector(default, SimFix.FromInt(2), SimVec3.Metres(1, 0, 0), cosine, default);

            Assert.That(SpatialGeometry.Intersects(in body, in request, out _, true), Is.EqualTo(expected));
        }
    }
}
