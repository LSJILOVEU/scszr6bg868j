using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    [TestFixture,Category("M0c2S1")]
    public sealed class Mt188GameplayMathTests
    {
        [Test]
        public void ChangeResourceRejectsReversedBounds()
        {
            long count=0;
            Assert.Throws<SimBoundaryException>(() => GameplayMath.ChangeResource(1,0,2,1,ref count));
        }

        [Test]
        public void CanSpendRejectsValueBelowLowerBound()
        {
            Assert.Throws<SimBoundaryException>(() => GameplayMath.CanSpend(4,5,0));
        }

        [Test]
        public void SaturatingAddIncreasesValueForPositiveAddition()
        {
            long count=0;
            Assert.That(GameplayMath.SaturatingAdd(10,3,ref count),Is.EqualTo(13));
            Assert.That(count,Is.Zero);
        }
    }
}
