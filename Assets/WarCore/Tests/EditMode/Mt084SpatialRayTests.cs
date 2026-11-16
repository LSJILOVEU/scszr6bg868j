using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    [Category("NarrowPhase")]
    public sealed class Mt084SpatialRayTests
    {
        [TestCase(SpatialQueryKind.Ray, 131072L)]
        [TestCase(SpatialQueryKind.Capsule, 0L)]
        public void SingletonSideIntervalHitsZeroRadiusCapsule(SpatialQueryKind kind, long expectedDistance)
        {
            var inputs = new A2SpatialInputs(0);
            var sample = inputs.Make(kind, default, 0, 2,
                A2SpatialInputs.V(-1, -131072, 0), A2SpatialInputs.V(1, 131072, 0),
                0, 131073, 0, 0, "singleton-side");

            // The side interval rounds to [65536, 65536]; neither point cap intersects.
            Assert.That(SpatialRay.Intersects(in sample.Body, in sample.Request, out SimFix distance), Is.True);
            Assert.That(distance.Raw, Is.EqualTo(expectedDistance));
        }
    }
}
