using System;
using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class Mt215SpatialQueryModesTests
    {
        [Test]
        public void CylinderPreservesRadiusAndHalfHeight()
        {
            SpatialRequest request = SpatialQueries.Cylinder(default, SimFix.FromInt(2), SimFix.FromInt(5));
            Assert.That(request.Radius.Raw, Is.EqualTo(2 * SimFix.Scale));
            Assert.That(request.HalfHeight.Raw, Is.EqualTo(5 * SimFix.Scale));
        }

        [Test]
        public void SectorRejectsCosineBelowMinimum()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => SpatialQueries.Sector(
                default, SimFix.FromInt(2), SimVec3.Metres(1, 0, 0), -1001, SimFix.FromInt(1)));
        }

        [Test]
        public void CapsuleDirectionRunsFromStartToEnd()
        {
            SpatialRequest request = SpatialQueries.Capsule(
                SimVec3.Metres(3, 4, 5), SimVec3.Metres(7, 6, 8), SimFix.FromInt(1));
            Assert.That(request.Origin, Is.EqualTo(SimVec3.Metres(3, 4, 5)));
            Assert.That(request.Direction, Is.EqualTo(SimVec3.Metres(4, 2, 3)));
        }
    }
}
