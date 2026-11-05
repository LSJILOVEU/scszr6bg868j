using System;
using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    [Category("S12")]
    public sealed class MathSmallContractTests
    {
        [TestCase(-1L)]
        [TestCase(long.MinValue)]
        public void NegativeScalarSquareRootNamesItsPublicInput(long raw)
        {
            var error = Assert.Throws<ArgumentOutOfRangeException>(() => SimMath.Sqrt(SimFix.FromRaw(raw)));
            Assert.That(error.ParamName, Is.EqualTo("value"));
        }

        [Test]
        public void NegativeWideSquareRootNamesTheValueInsteadOfRemovedOracleType()
        {
            var x = new SimVec3(SimFix.FromRaw(-1), default, default);
            var y = new SimVec3(SimFix.FromRaw(1), default, default);
            var error = Assert.Throws<ArgumentOutOfRangeException>(() => SimVec3.Dot(x, y).Sqrt());
            Assert.That(error.ParamName, Is.EqualTo("value"));
        }

        [Test]
        public void NegativeGeometrySquareRootNamesTheValue()
        {
            var error = Assert.Throws<ArgumentOutOfRangeException>(() => GeometryInt288.From(long.MinValue).Sqrt());
            Assert.That(error.ParamName, Is.EqualTo("value"));
        }

        [Test]
        public void OddColliderHeightIsRetainedAndBothCapsUseFloorHalfHeight()
        {
            var collider = new Collider(SimFix.FromRaw(1), SimFix.FromRaw(5));
            Assert.That(collider.Height.Raw, Is.EqualTo(5));
            var grid = SpatialQueryTests.Grid(new[] { new SpatialBody(new EntityId(1, 1), default, collider) });
            var results = new EntityId[1];
            foreach (int sign in new[] { -1, 1 })
            {
                var cap = new SimVec3(default, SimFix.FromRaw(sign * 2), default);
                var outside = new SimVec3(default, SimFix.FromRaw(sign * 3), default);
                Assert.That(grid.Sphere(cap, default, results), Is.EqualTo(1));
                Assert.That(grid.Sphere(outside, default, results), Is.Zero);
            }
        }

        [Test]
        public void DirectionlessAtan2ReturnsExactlyZero()
            => Assert.That(SimMath.Atan2(default, default).Raw, Is.Zero);
    }
}
