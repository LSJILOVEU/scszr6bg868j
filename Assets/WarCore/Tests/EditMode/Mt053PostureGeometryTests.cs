using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class Mt053PostureGeometryTests
    {
        [TestCase(0)]
        [TestCase(2)]
        public void ZeroBaselineReturnsZero(int posture)
        {
            var f = new PostureFixture();
            f.Set(posture);
            SimFix distance = default;
            Assert.DoesNotThrow(() => distance = f.Geometry.DetectionDistance(f.Unit, default));
            Assert.That(distance.Raw, Is.Zero);
        }
    }
}
