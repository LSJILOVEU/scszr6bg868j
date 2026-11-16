using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class Mt082SpatialQueryModesTests
    {
        [Test]
        public void SphereDefaultsCosineToZero()
        {
            var request = SpatialQueries.Sphere(default, SimFix.FromInt(1));
            Assert.That(request.Cosine, Is.EqualTo(0));
        }

        [Test]
        public void SectorAcceptsCosineAtPositiveLimit()
        {
            SpatialRequest request = default;
            Assert.DoesNotThrow(() => request = SpatialQueries.Sector(default, SimFix.FromInt(1),
                SimVec3.Metres(1, 0, 0), 1000, SimFix.FromInt(1)));
            Assert.That(request.Cosine, Is.EqualTo(1000));
        }

        [Test]
        public void CapsuleAcceptsSweepAtLengthLimit()
        {
            SpatialRequest request = default;
            Assert.DoesNotThrow(() => request = SpatialQueries.Capsule(
                SimVec3.Metres(-8192, 0, 0), SimVec3.Metres(8192, 0, 0), SimFix.FromInt(1)));
            Assert.That(request.Direction, Is.EqualTo(SimVec3.Metres(16384, 0, 0)));
        }
    }
}
