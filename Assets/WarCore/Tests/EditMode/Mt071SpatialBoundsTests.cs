using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    [Category("PartitionPerf")]
    public sealed class Mt071SpatialBoundsTests
    {
        [TestCase(536870911L, true)]
        [TestCase(536870912L, true)]
        [TestCase(536870913L, false)]
        public void ScalarSlabGuardUsesFixedBoundary(long raw, bool expected)
        {
            var direction = A2SpatialInputs.V(raw, 0, 0);
            Assert.That(SpatialBounds.CanUseScalarSlab(0, 0, 0, 0, 0, in direction), Is.EqualTo(expected));
        }
    }
}
