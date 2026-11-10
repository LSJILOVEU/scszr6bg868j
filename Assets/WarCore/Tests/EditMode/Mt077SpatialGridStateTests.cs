using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class Mt077SpatialGridStateTests
    {
        [Test]
        public void ZeroCoordinateBelongsToCellZero()
        {
            Assert.That(SpatialGridBounds.Floor(0, 8 * SimFix.Scale), Is.EqualTo(0));
        }

        [Test]
        public void XWidthEqualToBudgetKeepsExactVolume()
        {
            var collider = Collider.Box(SimFix.FromInt(252), SimFix.FromInt(2),
                SimFix.FromInt(1), BoxYaw.Identity);
            var body = new SpatialBody(new EntityId(0, 1), SimVec3.Metres(256, 4, 4), collider);
            var bounds = new SpatialGridBounds(in body, 8 * SimFix.Scale);

            Assert.That(bounds.Volume(64), Is.EqualTo(64));
            Assert.That(bounds.Volume(63), Is.EqualTo(-1));
        }

        [Test]
        public void ResetClearsLayerUnionBeforePublication()
        {
            var partition = new SpatialPartitionState(1, 1, 16, false)
            {
                LayerUnion = 0x80000000u
            };

            partition.Reset();

            Assert.That(partition.LayerUnion, Is.EqualTo(0u));
        }
    }
}
