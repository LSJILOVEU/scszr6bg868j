using NUnit.Framework;
using WarCore.Presentation;

namespace WarCore.Tests
{
    public sealed class Mt169PresentationRandomTests
    {
        [Test]
        public void NextUnitMatchesSuppliedSeedSequence()
        {
            var expected = new System.Random(123);
            var actual = new PresentationRandom(123);
            for (int i = 0; i < 3; i++)
                Assert.That(actual.NextUnit(), Is.EqualTo(expected.NextDouble()));
        }
    }
}
