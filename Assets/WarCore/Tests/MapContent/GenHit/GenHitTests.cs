using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Content.Rwr;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class GenHitTests
    {
        private const string MeleeJson = @"{
            ""id"": ""ancient"",
            ""melee"": [{ ""id"": ""sword"", ""windupTicks"": 6, ""activeTicks"": 2, ""recoveryTicks"": 9,
                ""radius"": 2, ""cosHalfAnglePermille"": 500, ""halfHeight"": 1, ""damage"": 10,
                ""maxTargets"": 1, ""engageRange"": 1.8 }],
            ""unitTypes"": [{ ""id"": ""soldier"", ""weapon"": """", ""melee"": ""sword"", ""speed"": 3000 }],
            ""combat"": { ""maxHealth"": 100, ""selector"": ""nearest-visible"", ""friendlyFire"": false,
                ""friendlyFireLocked"": false, ""allyCheckMissRate"": 0, ""allyHoldMilli"": 30000 }
        }";

        private static ContentPack Load(string json) => ContentLoader.Load(new MemoryContentSource(
            new Dictionary<string, string> { ["ancient"] = json }), "ancient");

        [Test]
        public void GenHit_MeleeOnlyPackWithoutResolverRuns300TicksDeterministically()
        {
            ContentPack pack = null;
            Assert.DoesNotThrow(() => pack = Load(MeleeJson));
            Assert.That(MeleeJson, Does.Not.Contain("rwr."));
            Assert.That(pack.WeaponCount, Is.Zero);
            Assert.That(pack.MeleeCount, Is.EqualTo(1));
            Assert.That(pack.BuildCombat().Resolver, Is.Null);
            var units = new[]
            {
                UnitSpawn.OfType(1, pack, "soldier", 1, 1, 20000, 20000),
                UnitSpawn.OfType(2, pack, "soldier", 2, 2, 21000, 20000)
            };
            var first = new MeleeWorld(pack.BuildCombat(), units);
            var second = new MeleeWorld(pack.BuildCombat(), units);
            first.Step(7); second.Step(7);
            Assert.That(first.Health(1), Is.EqualTo(90 * SimFix.Scale));
            Assert.That(first.Health(2), Is.EqualTo(90 * SimFix.Scale));
            first.Step(293); second.Step(293);
            Assert.That(first.Tick, Is.EqualTo(300));
            Assert.That(second.Tick, Is.EqualTo(300));
            Assert.That(first.W.Hash, Is.EqualTo(second.W.Hash));
        }

        [Test]
        public void GenHit_RangedWeaponsStillRequireResolver()
        {
            const string weapon = @"""weapons"": [{ ""id"": ""bow"", ""fireIntervalMilli"": 1000, ""magazine"": 1,
                ""reloadMilli"": 1000, ""falloffStart"": 1, ""falloffEnd"": 10, ""effectRaw"": 65536,
                ""spreadBase"": 0, ""spreadGrowth"": 0, ""spreadRecoverPerSec"": 0, ""spreadMax"": 0 }],";
            var error = Assert.Throws<ContentException>(() => Load(MeleeJson.Replace("\"melee\": [", weapon + "\"melee\": [")));
            Assert.That(error.Message, Is.EqualTo("content pack 'ancient' layer 'ancient' combat is missing field 'resolver'"));
        }

        [TestCase("\"unknown\"", "content pack 'ancient' layer 'ancient' combat field 'resolver': unknown policy 'unknown', allowed: rwr.hit")]
        [TestCase("\"\"", "content pack 'ancient' layer 'ancient' combat field 'resolver': unknown policy '', allowed: rwr.hit")]
        public void GenHit_ExplicitInvalidResolverIsRejected(string value, string expected)
        {
            var error = Assert.Throws<ContentException>(() => Load(MeleeJson.Replace("\"maxHealth\":", "\"resolver\":" + value + ",\"maxHealth\":")));
            Assert.That(error.Message, Is.EqualTo(expected));
        }

        [Test]
        public void GenHit_AbsentResolverHasDistinctDigestFromExplicitRwr()
        {
            var absent = Load(MeleeJson).BuildCombat();
            var explicitRwr = Load(MeleeJson.Replace("\"maxHealth\":", "\"resolver\":\"rwr.hit\",\"maxHealth\":")).BuildCombat();
            Assert.That(explicitRwr.Resolver.PolicyKey, Is.EqualTo(0x5257524849540001UL));
            Assert.That(absent.PolicyDigest, Is.Not.EqualTo(explicitRwr.PolicyDigest));
        }

        [TestCase("rwr", 0x8BE7D7DFA58533CDUL)]
        [TestCase("rwr-m6", 0x934F8562D85447D2UL)]
        [TestCase("rwr-melee", 0x74B9EA11CFF86DF2UL)]
        [TestCase("rwr-terrain-demo", 0x8BE7D7DFA58533CDUL)]
        public void GenHit_RwrPolicyDigestsMatchOriginMain(string layer, ulong expected)
        {
            var combat = RwrPack.Load(layer).BuildCombat();
            Assert.That(combat.Resolver.PolicyKey, Is.EqualTo(0x5257524849540001UL));
            Assert.That(combat.PolicyDigest, Is.EqualTo(expected));
        }
    }
}
