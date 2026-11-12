using NUnit.Framework;
using WarCore.Content.MapTools;

namespace WarCore.Tests
{
    public sealed class Mt137MapDescEqualityTests
    {
        [TestCase(2, 4)]
        [TestCase(4, 3)]
        public void GridEqualityRequiresBothAxes(int x, int z)
        {
            Assert.That(new MapXZ(2, 3).Equals(new MapXZ(x, z)), Is.False);
        }

        [TestCase(2000L, 4000L)]
        [TestCase(4000L, 3000L)]
        public void MillimetreEqualityRequiresBothAxes(long x, long z)
        {
            Assert.That(new MapMm(2000, 3000).Equals(new MapMm(x, z)), Is.False);
        }
    }
}
