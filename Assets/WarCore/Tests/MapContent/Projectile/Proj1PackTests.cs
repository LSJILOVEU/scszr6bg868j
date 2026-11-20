using System;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Sim;

namespace WarCore.Tests.Projectile
{
    // PROJ-1 A1 (data) and A18: projectileSpeed in content and in the combat pack.
    public sealed class Proj1PackTests
    {
        private const string Weapon = "rwr.weapon-1";

        // rwr with weapon-1 given a projectile speed (and optionally a falloff range), with or without the RWR placeholder body.
        private static ContentPack Load(string speed, bool body = true, string start = null, string end = null)
        {
            string weapon = "{ \"id\": \"" + Weapon + "\", \"projectileSpeed\": " + speed
                + (start == null ? "" : ", \"falloffStart\": " + start) + (end == null ? "" : ", \"falloffEnd\": " + end) + " }";
            string json = "\"weapons\": [ " + weapon + " ]" + (body ? ", \"combat\": { " + Ml3Content.BodyJson + " }" : "");
            return Ml3Content.Load("proj1", Ml3Content.Layer("proj1", "rwr", json));
        }

        private static void Refused(Func<ContentPack> load)
        {
            var ex = Assert.Throws<ContentException>(() => load());
            StringAssert.Contains("proj1", ex.Message);
            StringAssert.Contains(Weapon, ex.Message);
            StringAssert.Contains("projectileSpeed", ex.Message);
        }

        [Test]
        public void WrittenZeroKeepsTheDigest()
        {
            ContentPack zero = Ml3Content.Load("proj1", Ml3Content.Layer("proj1", "rwr",
                "\"weapons\": [ { \"id\": \"" + Weapon + "\", \"projectileSpeed\": 0 } ]"));
            Assert.That(zero.BuildCombat().PolicyDigest, Is.EqualTo(Proj1DigestTests.Shipped("rwr").BuildCombat().PolicyDigest));
            Assert.That(zero.BuildCombat().HasProjectiles, Is.False);
        }

        [TestCase("rwr")]
        [TestCase("rwr-m6")]
        [TestCase("ancient")]
        public void ShippedPacksHaveNoProjectiles(string id) => Assert.That(Proj1DigestTests.Shipped(id).BuildCombat().HasProjectiles, Is.False);

        // A falloffEnd of 20 m keeps every flight short (999 mm/s: 601 ticks), so only the speed itself can be refused.
        [TestCase("999")]
        [TestCase("1000001")]
        [TestCase("-1")]
        public void SpeedOutsideItsRangeIsRefused(string speed) => Refused(() => Load(speed, start: "10", end: "20"));

        [Test]
        public void SpeedBoundsLoad()
        {
            CombatPack slow = Load("1000", start: "10", end: "29").BuildCombat();
            Assert.That(slow.IsProjectile(0), Is.True, "the speed reaches the combat pack");
            Assert.That(slow.Projectiles[0].StepRaw, Is.EqualTo(2184));
            Assert.That(slow.Projectiles[0].Flights, Is.EqualTo(871));
            CombatPack fast = Load("1000000").BuildCombat();
            Assert.That(fast.Projectiles[0].StepRaw, Is.EqualTo(2184533));
            Assert.That(fast.Projectiles[0].Flights, Is.EqualTo(3));
            Assert.That(fast.IsProjectile(0), Is.True);
            Assert.That(fast.IsProjectile(1), Is.False, "weapon-2 stays instant");
            Assert.That(fast.Projectiles[1].IsInstant, Is.True);
        }

        // 30 000 mm/s is exactly 1 m a tick, so a falloffEnd of 900 m is 900 ticks and 901 m is 901.
        [Test]
        public void FlightOf900TicksLoadsAnd901IsRefused()
        {
            Assert.That(Load("30000", end: "900").BuildCombat().Projectiles[0].Flights, Is.EqualTo(900));
            Refused(() => Load("30000", end: "901"));
        }

        [Test]
        public void ProjectileWeaponWithoutBodyIsRefused() => Refused(() => Load("100000", body: false));

        [Test]
        public void PackRefusesWhatContentRefuses()
        {
            Assert.Throws<ArgumentException>(() => Proj1World.Pack(100000, noBody: true));
            Assert.Throws<ArgumentOutOfRangeException>(() => Proj1World.Pack(999, Proj1World.Weapon(falloffEnd: 20)));
            Assert.DoesNotThrow(() => Proj1World.Pack(1000, Proj1World.Weapon(falloffEnd: 20)));
            Assert.Throws<ArgumentOutOfRangeException>(() => Proj1World.Pack(1_000_001));
            Assert.Throws<ArgumentOutOfRangeException>(() => Proj1World.Pack(30000, Proj1World.Weapon(falloffEnd: 901)));
            Assert.DoesNotThrow(() => Proj1World.Pack(30000, Proj1World.Weapon(falloffEnd: 900)));
            Assert.DoesNotThrow(() => Proj1World.Pack(0, noBody: true), "all instant needs no body");
            Assert.Throws<ArgumentException>(() => new CombatPack(new[] { Proj1World.Weapon() }, new FalloffDamage(), new NearestVisibleEnemy(),
                100 * Proj1World.M, body: Ml3World.Rwr, projectileSpeeds: new[] { 30000, 30000 }), "one speed per weapon");
        }

        // The section is appended only when some weapon is a projectile, and it carries every weapon's step.
        [Test]
        public void DigestCarriesTheSteps()
        {
            ulong instant = Proj1World.Pack(0).PolicyDigest;
            Assert.That(new CombatPack(new[] { Proj1World.Weapon() }, new FalloffDamage(), new NearestVisibleEnemy(), 100 * Proj1World.M,
                body: Ml3World.Rwr).PolicyDigest, Is.EqualTo(instant), "zero speeds hash as no speeds");
            ulong slow = Proj1World.Pack(30000).PolicyDigest, fast = Proj1World.Pack(60000).PolicyDigest;
            Assert.That(slow, Is.Not.EqualTo(instant));
            Assert.That(fast, Is.Not.EqualTo(slow));
        }

        // A18: WithSelector passes the projectile section on; only the selector changes the digest.
        [Test]
        public void WithSelectorKeepsTheProjectiles()
        {
            CombatPack pack = Proj1World.Pack(new[] { Proj1World.Weapon(), Proj1World.Weapon() }, new[] { 30000, 0 });
            CombatPack copy = pack.WithSelector(new SightLimited(new NearestVisibleEnemy(), SimFix.FromInt(20)));
            Assert.That(copy.HasProjectiles, Is.True);
            Assert.That(copy.Projectiles.ToArray(), Is.EqualTo(pack.Projectiles.ToArray()));
            Assert.That(copy.PolicyDigest, Is.Not.EqualTo(pack.PolicyDigest));
            Assert.That(copy.WithSelector(pack.Selector).PolicyDigest, Is.EqualTo(pack.PolicyDigest));
            CombatPack instant = Proj1World.Pack(0);
            Assert.That(instant.WithSelector(new NearestVisibleEnemy()).HasProjectiles, Is.False);
        }

        // P1 numbers: step = speed * 65536 / 30000 truncated; flights = ceil(range / step).
        [TestCase(30000, 65536L, 60, 60)]
        [TestCase(15000, 32768L, 60, 120)]
        [TestCase(100000, 218453L, 67, 21)]
        [TestCase(1000, 2184L, 10, 301)]
        public void StepAndFlights(int speed, long step, int metres, int flights)
        {
            var spec = new ProjectileSpec(speed, SimFix.FromInt(metres));
            Assert.That((spec.StepRaw, spec.Flights), Is.EqualTo((step, flights)));
        }
    }
}
