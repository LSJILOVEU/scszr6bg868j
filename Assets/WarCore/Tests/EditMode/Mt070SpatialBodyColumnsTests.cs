using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class Mt070SpatialBodyColumnsTests
    {
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(10)]
        public void ConstructorAllocatesEachColumnAtRequestedCapacity(int capacity)
        {
            var columns = new SpatialBodyColumns(capacity);

            Assert.That(columns.Entities.Length, Is.EqualTo(capacity));
            Assert.That(columns.Positions.Length, Is.EqualTo(capacity));
            Assert.That(columns.Colliders.Length, Is.EqualTo(capacity));
            Assert.That(columns.Geometry.Length, Is.EqualTo(capacity));
        }
    }
}
