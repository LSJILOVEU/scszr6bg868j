using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class Mt074SpatialGeometryTests
    {
        [Test]
        public void RayBoundsRejectAnExpandedHitBeyondTheRequestBounds()
        {
            var body = new SpatialBody(new EntityId(1, 1), SimVec3.Metres(9, 0, 0), default);
            SpatialRequest request = LayerQueryFixture.Request(SpatialQueryKind.Ray);

            Assert.That(SpatialGeometry.Intersects(in body, in request, out SimFix distance, out bool enteredNarrowPhase), Is.False);
            Assert.That(enteredNarrowPhase, Is.False);
            Assert.That(distance.Raw, Is.Zero);
        }

        [Test]
        public void CylinderHitsBehindTheRequestDirectionRegardlessOfCosine()
        {
            var body = new SpatialBody(new EntityId(1, 1), SimVec3.Metres(-1, 0, 0), default);
            SpatialRequest request = LayerQueryFixture.Request(SpatialQueryKind.Cylinder);

            Assert.That(SpatialGeometry.Intersects(in body, in request, out SimFix distance), Is.True);
            Assert.That(distance.Raw, Is.Zero);
        }
    }
}
