using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class Mt216SpatialRayTests
    {
        [Test]
        public void DiagonalRayIncludesContactAtRangeEndpoint()
        {
            var entity = new EntityId(0, 1);
            var body = new SpatialBody(entity, SimVec3.Metres(6, 0, 8),
                new Collider(SimFix.FromInt(5), SimFix.FromInt(10)));
            var query = SpatialQueryTests.Grid(new[]{body});
            var hits = new RayHit[1];

            Assert.That(query.Ray(default, SimVec3.Metres(3, 0, 4), SimFix.FromInt(5), hits), Is.EqualTo(1));
            Assert.That(hits[0].Entity, Is.EqualTo(entity));
            Assert.That(hits[0].Distance, Is.EqualTo(SimFix.FromInt(5)));
        }

        [Test]
        public void DiagonalCapsuleIncludesContactAtSegmentEndpoint()
        {
            var entity = new EntityId(0, 1);
            var body = new SpatialBody(entity, SimVec3.Metres(6, 0, 8),
                new Collider(SimFix.FromInt(4), SimFix.FromInt(8)));
            var query = SpatialQueryTests.Grid(new[]{body});
            var entities = new EntityId[1];

            Assert.That(query.Capsule(default, SimVec3.Metres(3, 0, 4), SimFix.FromInt(1), entities), Is.EqualTo(1));
            Assert.That(entities[0], Is.EqualTo(entity));
        }
    }
}
