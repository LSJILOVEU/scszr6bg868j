using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Content.Rwr;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // The generic hit resolver "damage": a hit takes EffectRaw of health, scaled by falloff, with no roll, so several hits
    // kill. The RWR resolver stays as it was.
    public sealed class DamageResolverTests
    {
        private const long S = SimFix.Scale;
        private static WeaponDef Gun(long effectRaw) => new WeaponDef(10000, 8, 60000, SimFix.FromInt(30), SimFix.FromInt(60), effectRaw);

        [Test]
        public void Damage_IsEffectScaledByFalloffWithoutRoll()
        {
            var r = new DamageResolver();
            Assert.That(r.PolicyKey, Is.EqualTo(0x44414D4147450001UL));
            WeaponDef gun = Gun(34 * S);
            Assert.That(r.DamageRaw(in gun, S, 0, 100 * S), Is.EqualTo(34 * S));
            Assert.That(r.DamageRaw(in gun, S, 65535, 100 * S), Is.EqualTo(34 * S), "the roll is not read");
            Assert.That(r.DamageRaw(in gun, S, 0, 1), Is.EqualTo(34 * S), "full health is not read");
            Assert.That(r.DamageRaw(in gun, S / 2, 0, 100 * S), Is.EqualTo(17 * S));
            Assert.That(r.DamageRaw(in gun, 0, 0, 100 * S), Is.EqualTo(0));
            WeaponDef small = Gun(3);
            Assert.That(r.DamageRaw(in small, S / 2, 0, 100 * S), Is.EqualTo(1), "toward zero");
            WeaponDef big = Gun(1L << 40);
            Assert.That(r.DamageRaw(in big, S, 0, 100 * S), Is.EqualTo(1L << 40), "the largest effect content allows does not overflow");
        }

        private const string Json = @"{ ""id"": ""dmg"",
            ""weapons"": [{ ""id"": ""bow"", ""fireIntervalMilli"": 30000, ""magazine"": 1, ""reloadMilli"": 30000, ""falloffStart"": 30, ""falloffEnd"": 60,
                ""effectRaw"": 2621440, ""spreadBase"": 0, ""spreadGrowth"": 0, ""spreadRecoverPerSec"": 0, ""spreadMax"": 0 }],
            ""combat"": { ""maxHealth"": 100, ""resolver"": ""RESOLVER"", ""selector"": ""nearest-visible"", ""friendlyFire"": false,
                ""friendlyFireLocked"": false, ""allyCheckMissRate"": 0, ""allyHoldMilli"": 30000 } }";

        private static ContentPack Load(string resolver) => ContentLoader.Load(new MemoryContentSource(
            new Dictionary<string, string> { ["dmg"] = Json.Replace("RESOLVER", resolver) }), "dmg");

        [Test]
        public void Damage_IsAContentPolicy()
        {
            CombatPack damage = Load("damage").BuildCombat(), rwr = Load("rwr.hit").BuildCombat();
            Assert.That(damage.Resolver, Is.TypeOf<DamageResolver>());
            Assert.That(rwr.Resolver, Is.TypeOf<RwrHitResolver>());
            Assert.That(rwr.Resolver.PolicyKey, Is.EqualTo(0x5257524849540001UL));
            Assert.That(damage.PolicyDigest, Is.Not.EqualTo(rwr.PolicyDigest));
            Assert.That(RwrPack.CreateM6().Resolver, Is.TypeOf<RwrHitResolver>(), "the RWR pack keeps its resolver");
        }

        // One armed unit 10 m from an unarmed enemy: with 40 health a hit and 100 health, the enemy loses exactly 40 per
        // landed hit, lives through two and dies on the third.
        [Test]
        public void Damage_WoundsUntilTheThirdHitKills()
        {
            var combat = new CombatPack(new[] { Gun(40 * S) }, new DamageResolver(), new NearestVisibleEnemy(), 100 * S);
            var units = new[] { M6World.U(1, 1, 20, 20, weapon: 0), M6World.U(2, 2, 30, 20) };
            var w = new M6World(M6World.Endless(M6World.RecipeOf(units, combat: combat)));
            StableId victim = w.Sim.Entities.StableOf(w.Unit(2));
            long health = w.Sim.GameplayResource(victim, 1);
            Assert.That(health, Is.EqualTo(100 * S));
            var seen = new List<long> { health };
            for (int tick = 0; tick < 3000 && !w.Dead(2); tick++)
            {
                w.Step();
                long now = w.Sim.GameplayResource(victim, 1);
                if (now != health) { Assert.That(health - now, Is.EqualTo(w.Dead(2) ? health : 40 * S), "tick " + tick); seen.Add(now); health = now; }
            }
            Assert.That(w.Dead(2), Is.True);
            Assert.That(seen, Is.EqualTo(new[] { 100 * S, 60 * S, 20 * S, 0 }));
        }
    }
}
