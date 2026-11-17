using NUnit.Framework;

namespace WarCore.Tests
{
    public sealed class SpawnBiasTests
    {
        [TestCase("control", 41UL, 2, 2050L)]
        [TestCase("control", 42UL, 2, 2094L)]
        [TestCase("control", 43UL, 2, 2130L)]
        [TestCase("control", 44UL, 2, 2103L)]
        [TestCase("control", 45UL, 1, 2170L)]
        [TestCase("weapons", 41UL, 2, 2041L)]
        [TestCase("weapons", 42UL, 2, 2116L)]
        [TestCase("weapons", 43UL, 2, 2027L)]
        [TestCase("weapons", 44UL, 2, 2117L)]
        [TestCase("weapons", 45UL, 2, 2039L)]
        [TestCase("same-weapon", 41UL, 2, 2085L)]
        [TestCase("same-weapon", 42UL, 2, 2074L)]
        [TestCase("same-weapon", 43UL, 2, 2060L)]
        [TestCase("same-weapon", 44UL, 2, 2136L)]
        [TestCase("same-weapon", 45UL, 2, 2037L)]
        [TestCase("keys", 41UL, 1, 2147L)]
        [TestCase("keys", 42UL, 1, 2120L)]
        [TestCase("keys", 43UL, 1, 2098L)]
        [TestCase("keys", 44UL, 2, 2151L)]
        [TestCase("keys", 45UL, 1, 2104L)]
        [TestCase("sides", 41UL, 2, 2145L)]
        [TestCase("sides", 42UL, 2, 2128L)]
        [TestCase("sides", 43UL, 1, 2150L)]
        [TestCase("sides", 44UL, 2, 2088L)]
        [TestCase("sides", 45UL, 2, 2063L)]
        public void SpawnBias_WinnerAndEndTick(string combination, ulong seed, int winner, long tick)
        {
            var result = SpawnBiasFixture.Run(combination, seed);
            Assert.That(result.winner, Is.EqualTo(winner));
            Assert.That(result.tick, Is.EqualTo(tick));
        }

        [Test]
        public void SpawnBias_RepeatedCombinationAndSeedKeepEndTickAndHash()
        {
            var first = SpawnBiasFixture.Run("control", 41);
            var second = SpawnBiasFixture.Run("control", 41);
            Assert.That(first.winner, Is.EqualTo(2));
            Assert.That(first.tick, Is.EqualTo(2050));
            Assert.That(second.winner, Is.EqualTo(2));
            Assert.That(second.tick, Is.EqualTo(first.tick));
            Assert.That(second.hash, Is.EqualTo(first.hash));
        }
    }
}
