using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class GenHitTests
    {
        private const string MeleePack = "{\"id\":\"ancient\","
            + "\"melee\":[{\"id\":\"sword\",\"windupTicks\":6,\"activeTicks\":2,\"recoveryTicks\":9,"
            + "\"radius\":2,\"cosHalfAnglePermille\":500,\"halfHeight\":1,\"damage\":10,\"maxTargets\":1,\"engageRange\":1.8}],"
            + "\"unitTypes\":[{\"id\":\"swordsman\",\"weapon\":\"\",\"melee\":\"sword\",\"speed\":3000}],"
            + "\"combat\":{\"maxHealth\":100,\"selector\":\"nearest-visible\",\"friendlyFire\":false,"
            + "\"friendlyFireLocked\":false,\"allyCheckMissRate\":0,\"allyHoldMilli\":30000}}";

        private static ContentPack Load(string json) => ContentLoader.Load(
            new MemoryContentSource(new Dictionary<string, string> { ["ancient"] = json }), "ancient");

        [Test]
        public void MeleeOnlyPackWithoutResolverFightsFor300TicksDeterministically()
        {
            ContentPack content = null;
            CombatPack combat = null;
            Assert.DoesNotThrow(() => { content = Load(MeleePack); combat = content.BuildCombat(); });
            Assert.That(MeleePack, Does.Not.Contain("rwr."));
            Assert.That(content.WeaponCount, Is.Zero);
            Assert.That(content.MeleeCount, Is.EqualTo(1));
            Assert.That(combat.Resolver, Is.Null);
            var units = new[] {
                UnitSpawn.OfType(1, content, "swordsman", 1, 1, 20000, 20000),
                UnitSpawn.OfType(2, content, "swordsman", 2, 2, 21000, 20000)
            };
            var a = new MeleeWorld(combat, units);
            var b = new MeleeWorld(combat, units);
            for (int tick = 1; tick <= 300; tick++)
            {
                a.Step(); b.Step();
                Assert.That(b.W.Hash, Is.EqualTo(a.W.Hash), "tick " + tick);
                if (tick == 7)
                {
                    Assert.That(a.Health(1), Is.EqualTo(90L * SimFix.Scale));
                    Assert.That(a.Health(2), Is.EqualTo(90L * SimFix.Scale));
                }
            }
            Assert.That(a.Tick, Is.EqualTo(300));
            Assert.That(b.Tick, Is.EqualTo(300));
            Assert.That(a.Health(1), Is.Zero);
            Assert.That(a.Health(2), Is.Zero);
        }

        [Test]
        public void RangedWeaponStillRequiresResolver()
        {
            string json = MeleePack.Replace("\"melee\":[", "\"weapons\":[{\"id\":\"bow\",\"fireIntervalMilli\":1000,"
                + "\"magazine\":1,\"reloadMilli\":1000,\"falloffStart\":1,\"falloffEnd\":2,\"effectRaw\":65536,"
                + "\"spreadBase\":0,\"spreadGrowth\":0,\"spreadRecoverPerSec\":0,\"spreadMax\":0}],\"melee\":[");
            Assert.That(Assert.Throws<ContentException>(() => Load(json)).Message,
                Is.EqualTo("content pack 'ancient' layer 'ancient' combat is missing field 'resolver'"));
        }

        [TestCase("\"made.up\"", "content pack 'ancient' layer 'ancient' combat field 'resolver': unknown policy 'made.up', allowed: rwr.hit")]
        [TestCase("\"\"", "content pack 'ancient' layer 'ancient' combat field 'resolver': unknown policy '', allowed: rwr.hit")]
        [TestCase("null", "content layer 'ancient' line 1 (combat.resolver): unexpected 'n' (null and exponent numbers are not supported)")]
        public void ExplicitInvalidResolverIsRejectedWithoutRangedWeapons(string value, string expected)
        {
            string json = MeleePack.Replace("\"selector\":", "\"resolver\":" + value + ",\"selector\":");
            Assert.That(Assert.Throws<ContentException>(() => Load(json)).Message,
                Is.EqualTo(expected));
        }

        [Test]
        public void AbsentResolverHasDistinctPolicyDigest()
        {
            CombatPack omitted = null;
            Assert.DoesNotThrow(() => omitted = Load(MeleePack).BuildCombat());
            var explicitResolver = Load(MeleePack.Replace("\"selector\":", "\"resolver\":\"rwr.hit\",\"selector\":")).BuildCombat();
            Assert.That(omitted.PolicyDigest, Is.Not.EqualTo(explicitResolver.PolicyDigest));
        }

        [TestCase("rwr", 0x8BE7D7DFA58533CDUL)]
        [TestCase("rwr-m6", 0x934F8562D85447D2UL)]
        [TestCase("rwr-melee", 0x74B9EA11CFF86DF2UL)]
        [TestCase("rwr-terrain-demo", 0x8BE7D7DFA58533CDUL)]
        public void RwrPolicyDigestMatchesOriginalMain(string id, ulong expected)
        {
            Assert.That(ContentLoader.Load(FileContentSource.Rwr(), id).BuildCombat().PolicyDigest, Is.EqualTo(expected));
        }
    }
}
