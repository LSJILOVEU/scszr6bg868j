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
        private const string Json = "{\"id\":\"swords\",\"melee\":[{\"id\":\"sword\",\"windupTicks\":6,\"activeTicks\":2,"
            + "\"recoveryTicks\":9,\"radius\":2,\"cosHalfAnglePermille\":500,\"halfHeight\":1,\"damage\":10,\"maxTargets\":1,\"engageRange\":1.8}],"
            + "\"unitTypes\":[{\"id\":\"fighter\",\"weapon\":\"\",\"melee\":\"sword\",\"speed\":3000}],"
            + "\"combat\":{\"maxHealth\":100,\"selector\":\"nearest-visible\",\"friendlyFire\":false,\"friendlyFireLocked\":false,"
            + "\"allyCheckMissRate\":0,\"allyHoldMilli\":30000}}";
        private const string Weapons = "\"weapons\":[{\"id\":\"bow\",\"fireIntervalMilli\":1000,\"magazine\":1,\"reloadMilli\":1000,"
            + "\"falloffStart\":1,\"falloffEnd\":2,\"effectRaw\":65536,\"spreadBase\":0,\"spreadGrowth\":0,\"spreadRecoverPerSec\":0,\"spreadMax\":0}],";

        private static ContentPack Load(string json) => ContentLoader.Load(new MemoryContentSource(
            new Dictionary<string, string> { ["swords"] = json }), "swords");

        private static ContentPack MeleePack()
        {
            ContentPack pack = null;
            Assert.DoesNotThrow(() => pack = Load(Json));
            return pack;
        }

        [Test]
        public void MeleeOnlyPackWithoutRwrIdsFightsFor300TicksDeterministically()
        {
            Assert.That(Json, Does.Not.Contain("rwr."));
            ContentPack pack = MeleePack();
            Assert.That(pack.WeaponCount, Is.Zero);
            Assert.That(pack.MeleeCount, Is.EqualTo(1));
            var units = new[]
            {
                UnitSpawn.OfType(1, pack, "fighter", 1, 1, 20000, 20000),
                UnitSpawn.OfType(2, pack, "fighter", 2, 2, 21000, 20000)
            };
            var a = new MeleeWorld(pack.BuildCombat(), units);
            var b = new MeleeWorld(MeleePack().BuildCombat(), units);
            a.Step(6); b.Step(6);
            Assert.That(a.Health(1), Is.EqualTo(100 * SimFix.Scale));
            Assert.That(a.Health(2), Is.EqualTo(100 * SimFix.Scale));
            a.Step(); b.Step();
            Assert.That(a.Health(1), Is.EqualTo(90 * SimFix.Scale));
            Assert.That(a.Health(2), Is.EqualTo(90 * SimFix.Scale));
            Assert.That(a.Count(StrikeKind.Landed), Is.EqualTo(2));
            a.Step(293); b.Step(293);
            Assert.That(a.Tick, Is.EqualTo(300));
            Assert.That(b.Tick, Is.EqualTo(300));
            Assert.That(a.W.Hash, Is.EqualTo(b.W.Hash));
        }

        [Test]
        public void OmittedResolverDoesNoRangedDamageAndHasADistinctDigest()
        {
            CombatPack melee = MeleePack().BuildCombat();
            CombatPack legacy = Load(Json.Replace("\"combat\":{", "\"combat\":{\"resolver\":\"rwr.hit\",")).BuildCombat();
            var weapon = new WeaponDef(1000, 1, 1000, SimFix.FromInt(1), SimFix.FromInt(2), SimFix.Scale);
            Assert.That(melee.Resolver.DamageRaw(weapon, SimFix.Scale, 0, melee.MaxHealthRaw), Is.Zero);
            Assert.That(legacy.Resolver.DamageRaw(weapon, SimFix.Scale, 0, legacy.MaxHealthRaw), Is.EqualTo(100 * SimFix.Scale));
            Assert.That(melee.PolicyDigest, Is.Not.EqualTo(legacy.PolicyDigest));
        }

        [Test]
        public void ADeclaredRangedWeaponStillRequiresAResolver()
        {
            var error = Assert.Throws<ContentException>(() => Load(Json.Replace("\"melee\":[", Weapons + "\"melee\":[")));
            Assert.That(error.Message, Is.EqualTo("content pack 'swords' layer 'swords' combat is missing field 'resolver'"));
        }

        [TestCase("")]
        [TestCase("unknown")]
        public void ExplicitInvalidResolverIsRejectedWithoutRangedWeapons(string id)
        {
            var error = Assert.Throws<ContentException>(() => Load(Json.Replace("\"combat\":{", "\"combat\":{\"resolver\":\"" + id + "\",")));
            Assert.That(error.Message, Is.EqualTo("content pack 'swords' layer 'swords' combat field 'resolver': unknown policy '" + id + "', allowed: rwr.hit"));
        }
    }
}
