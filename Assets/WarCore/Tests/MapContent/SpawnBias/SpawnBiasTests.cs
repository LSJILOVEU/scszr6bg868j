using NUnit.Framework;

namespace WarCore.Tests
{
    public sealed class SpawnBiasTests
    {
        [TestCase("control", 41UL, 2, 2030L)]
        [TestCase("control", 42UL, 2, 2100L)]
        [TestCase("control", 43UL, 2, 2184L)]
        [TestCase("control", 44UL, 2, 2090L)]
        [TestCase("control", 45UL, 2, 2054L)]
        [TestCase("weapons", 41UL, 2, 2010L)]
        [TestCase("weapons", 42UL, 2, 2137L)]
        [TestCase("weapons", 43UL, 2, 2059L)]
        [TestCase("weapons", 44UL, 2, 2078L)]
        [TestCase("weapons", 45UL, 2, 2159L)]
        [TestCase("same-weapon", 41UL, 2, 2102L)]
        [TestCase("same-weapon", 42UL, 2, 2100L)]
        [TestCase("same-weapon", 43UL, 2, 2117L)]
        [TestCase("same-weapon", 44UL, 2, 2138L)]
        [TestCase("same-weapon", 45UL, 2, 2050L)]
        [TestCase("keys", 41UL, 1, 2219L)]
        [TestCase("keys", 42UL, 1, 2084L)]
        [TestCase("keys", 43UL, 1, 2114L)]
        [TestCase("keys", 44UL, 1, 4440L)]
        [TestCase("keys", 45UL, 1, 2086L)]
        [TestCase("sides", 41UL, 2, 2067L)]
        [TestCase("sides", 42UL, 2, 2082L)]
        [TestCase("sides", 43UL, 2, 2094L)]
        [TestCase("sides", 44UL, 2, 2148L)]
        [TestCase("sides", 45UL, 1, 2173L)]
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
            Assert.That(first.tick, Is.EqualTo(2030));
            Assert.That(second.winner, Is.EqualTo(2));
            Assert.That(second.tick, Is.EqualTo(first.tick));
            Assert.That(second.hash, Is.EqualTo(first.hash));
        }
    }
}
