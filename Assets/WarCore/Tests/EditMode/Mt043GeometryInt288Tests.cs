using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    [Category("NarrowPhase")]
    public sealed class Mt043GeometryInt288Tests
    {
        [TestCase(0UL, 1UL << 32)]
        [TestCase(ulong.MaxValue, ulong.MaxValue)]
        public void Magnitude128PreservesFourWordValues(ulong low, ulong high)
        {
            var value = GeometryInt288.From128(new FixedUInt128(low, high));
            FixedUInt128 actual = default;
            Assert.DoesNotThrow(() => actual = value.Magnitude128());
            Assert.That(actual.Low, Is.EqualTo(low));
            Assert.That(actual.High, Is.EqualTo(high));
        }
    }
}
