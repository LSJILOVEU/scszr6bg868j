using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    [Category("NarrowPhase")]
    public sealed class Mt085SpatialSectorTests
    {
        [TestCase((1L << 20) - 1, true)]
        [TestCase(1L << 20, true)]
        [TestCase((1L << 20) + 1, false)]
        public void ReachGuardIncludesLocalLimit(long reach, bool handled)
        {
            var inputs = new A2SpatialInputs(0);
            var sample = inputs.Make(SpatialQueryKind.Sector, A2SpatialInputs.V(2, 0, 0), 1, 2,
                default, A2SpatialInputs.V(1, 0, 0), reach - 1, 0, 0, 1000, "reach-guard");

            Assert.That(SpatialSector.TryIntersects(in sample.Body, in sample.Request, out bool hit), Is.EqualTo(handled));
            if (handled)
                Assert.That(hit, Is.True);
        }

        [TestCase(-1000, true)]
        [TestCase(-999, false)]
        public void BackwardPointRequiresFullAngle(int cosine, bool expected)
        {
            var inputs = new A2SpatialInputs(0);
            var sample = inputs.Make(SpatialQueryKind.Sector, A2SpatialInputs.V(-65536, 0, 0), 0, 0,
                default, A2SpatialInputs.V(65536, 0, 0), 65536, 0, 0, cosine, "full-angle-boundary");

            Assert.That(SpatialGeometry.Intersects(in sample.Body, in sample.Request, out _), Is.EqualTo(expected));
        }

        [Test]
        public void HalfPlaneRejectsRearDiskBeforeEdgeDirectionGuard()
        {
            var inputs = new A2SpatialInputs(0);
            var sample = inputs.Make(SpatialQueryKind.Sector, A2SpatialInputs.V(-2, 0, 0), 1, 2,
                default, A2SpatialInputs.V((1L << 17) + 1, 0, 0), 4, 0, 0, 0, "half-plane-rejection");

            Assert.That(SpatialSector.TryIntersects(in sample.Body, in sample.Request, out bool hit), Is.True);
            Assert.That(hit, Is.False);
        }
    }
}
