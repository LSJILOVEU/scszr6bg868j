using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    [Category("G25")]
    public sealed class Mt063SimIntegerTests
    {
        [Test]
        public void AddRetainsMaximumResult()
        {
            long result = 0;
            Assert.DoesNotThrow(() => result = SimInteger.Add(long.MaxValue - 1, 1));
            Assert.That(result, Is.EqualTo(long.MaxValue));
        }
    }
}
