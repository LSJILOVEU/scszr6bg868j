using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    [Category("NarrowPhase")]
    public sealed class Mt044GeometryUInt192Tests
    {
        [Test]
        public void SquareOfMaximum96BitValueIsExact()
        {
            GeometryUInt192 square = default;
            Assert.DoesNotThrow(() => square = GeometryUInt192.Square(new FixedUInt128(ulong.MaxValue, uint.MaxValue)));
            Assert.That(square.Low, Is.EqualTo(1UL));
            Assert.That(square.Middle, Is.EqualTo(0xFFFFFFFE00000000UL));
            Assert.That(square.High, Is.EqualTo(ulong.MaxValue));
        }

        [Test]
        public void DivideWithEqualWordCountsReturnsNonzeroQuotient()
        {
            var quotient = new GeometryUInt192(7, 2).Divide(new FixedUInt128(3, 1));
            Assert.That(quotient.Low, Is.EqualTo(2UL));
            Assert.That(quotient.Middle, Is.EqualTo(0UL));
            Assert.That(quotient.High, Is.EqualTo(0UL));
        }
    }
}
