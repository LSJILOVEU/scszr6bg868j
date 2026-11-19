using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    // ML-3 acceptance 12: a ray through a viewpoint of a foot-anchored body hits on the fast and the slow path alike.
    [Category("NarrowPhase")]
    public sealed class Ml3FastSlowTests
    {
        private const long M = SimFix.Scale;
        private const long R = M / 4;

        // Shooter offsets from the target's foot, in raw: level, from above (a floor), from below, close and steep.
        private static readonly long[][] Shooters =
        {
            new[] { -20 * M, 98304L, 0L },
            new[] { -9 * M, 3 * M + 98304, 2 * M },
            new[] { 7 * M, -3 * M + 98304, -5 * M },
            new[] { M, 6 * M, 0L },
            new[] { -2 * M, -4 * M, M / 3 },
        };

        // Even and odd heights; the eye at the lowest and highest allowed points, the middle, and 1.5 m in a 1.8 m body.
        [TestCase(114688L, R)]
        [TestCase(114688L, 114688L - R)]
        [TestCase(114688L, 57344L)]
        [TestCase(117964L, 98304L)]
        [TestCase(114689L, 114689L - R)]
        [TestCase(114689L, R)]
        [TestCase(2 * R, R)]
        [TestCase(2 * M, 2 * M - R)]
        public void RayThroughTheViewpointHitsOnBothPaths(long heightRaw, long eyeRaw)
        {
            var foot = new SimVec3(SimFix.FromInt(40), SimFix.FromInt(3), SimFix.FromInt(40));
            var collider = new Collider(SimFix.FromRaw(R), SimFix.FromRaw(heightRaw), 1u);
            var center = new SimVec3(foot.X, SimFix.FromRaw(foot.Y.Raw + heightRaw / 2), foot.Z);
            var body = new SpatialBody(new EntityId(1, 1), center, collider);
            var eye = new SimVec3(foot.X, SimFix.FromRaw(foot.Y.Raw + eyeRaw), foot.Z);
            foreach (long[] s in Shooters)
            {
                var origin = new SimVec3(SimFix.FromRaw(foot.X.Raw + s[0]), SimFix.FromRaw(foot.Y.Raw + s[1]), SimFix.FromRaw(foot.Z.Raw + s[2]));
                SpatialRequest ray = SpatialQueries.Ray(origin, eye - origin, SimFix.FromInt(64));
                bool fast = SpatialGeometry.Intersects(in body, in ray, out SimFix fastDistance, false);
                bool slow = SpatialGeometry.Intersects(in body, in ray, out SimFix slowDistance, true);
                string at = $"H {heightRaw}, E {eyeRaw}, shooter ({s[0]},{s[1]},{s[2]})";
                Assert.That((fast, slow), Is.EqualTo((true, true)), at);
                Assert.That(fastDistance.Raw, Is.EqualTo(slowDistance.Raw).Within(1), at);
                Assert.That(fastDistance <= (eye - origin).Length, Is.True, at + ": the hit is not past the viewpoint");
            }
        }

        // The legacy centred body: the fixed 0.5 m eye lies on the axis at the centre and both paths agree.
        [Test]
        public void LegacyCentredBodyHitsOnBothPaths()
        {
            var center = new SimVec3(SimFix.FromInt(10), SimFix.FromRaw(M / 2), SimFix.FromInt(10));
            var body = new SpatialBody(new EntityId(1, 1), center, new Collider(SimFix.FromRaw(R), SimFix.FromInt(1), 1u));
            var origin = new SimVec3(SimFix.FromInt(2), SimFix.FromRaw(M / 2), SimFix.FromInt(4));
            SpatialRequest ray = SpatialQueries.Ray(origin, center - origin, SimFix.FromInt(64));
            Assert.That(SpatialGeometry.Intersects(in body, in ray, out SimFix a, false), Is.True);
            Assert.That(SpatialGeometry.Intersects(in body, in ray, out SimFix b, true), Is.True);
            Assert.That(a.Raw, Is.EqualTo(b.Raw).Within(1));
        }
    }
}
