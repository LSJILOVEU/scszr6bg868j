using NUnit.Framework;

namespace WarCore.Tests
{
    [TestFixture, Category("M0c2S3")]
    public sealed class Mt109GameplayAbilitiesTests
    {
        [Test]
        public void UnboundGameplayHasNoCasts()
        {
            var sim = Fixtures.Empty(1);
            Assert.That(sim.GameplayCastCount, Is.Zero);
        }

        [Test]
        public void UnboundGameplayHasNoInputResults()
        {
            var sim = Fixtures.Empty(1);
            Assert.That(sim.GameplayInputResultCount, Is.Zero);
        }
    }
}
