using System;
using System.Security.Cryptography;
using NUnit.Framework;
using WarCore.Combat;
using WarCore.Sim;

namespace WarCore.Tests
{
    // WP-20 acceptance 6: a pure-gun world that is not built from the sample pack must save the very same snapshot bytes as before
    // the melee package. The expected digests were produced by this same test file run on the commit before the package (82ddafc).
    // The sample-pack worlds (M5 ticks 20 and 100, M6 / M6-reverse / M6-narrow tick 300) are covered the same way by the Wp13 golden.
    public sealed class Wp20OldWorldBytesTests
    {
        private sealed class FixedHit : IHitResolver
        {
            public ulong PolicyKey => 0x57503230000000F1;
            public long DamageRaw(in WeaponDef weapon, long falloffRaw, int rollRaw, long maxHealthRaw) => maxHealthRaw / 20;
        }

        private static string Digest(byte[] bytes)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void AGunOnlyWorldOutsideTheSamplePackSavesTheBytesItSavedBeforeMelee(bool reverse)
        {
            var weapons = new[]
            {
                new WeaponDef(2500, 12, 30000, SimFix.FromInt(20), SimFix.FromInt(60), 40000, new SpreadSpec(1500, 800, 4000, 7000)),
                new WeaponDef(4100, 6, 20000, SimFix.FromInt(10), SimFix.FromInt(50), 30000, new SpreadSpec(500, 1500, 2500, 5000)),
            };
            var pack = new CombatPack(weapons, new FixedHit(), new NearestVisibleEnemy(), 100 * SimFix.Scale);
            var units = new[]
            {
                M5TestWorld.Spawn(1, 0, 0, team: 1, weapon: 0), M5TestWorld.Spawn(2, 0, 3000, team: 1, weapon: 1), M5TestWorld.Spawn(3, 0, 6000, team: 1, weapon: 0),
                M5TestWorld.Spawn(4, 14000, 500, team: 2, weapon: 1, squad: 2), M5TestWorld.Spawn(5, 14000, 3500, team: 2, weapon: 0, squad: 2),
                M5TestWorld.Spawn(6, 14000, 6500, team: 2, weapon: 1, squad: 2),
            };
            var w = new M5TestWorld(units, pack, reverse);
            w.Order(0, 6000, 1500);
            for (int i = 0; i < 45; i++) w.Step();
            string at45 = Digest(w.Sim.SaveRetainedSnapshot());
            for (int i = 45; i < 150; i++) w.Step();
            string at150 = Digest(w.Sim.SaveRetainedSnapshot());
            Assert.That(at45, Is.EqualTo("8CBDE379E877243DA6A3CBC65F9D605B67E59D7A9BC1052A9F7D405B31A15680"), "tick 45");
            Assert.That(at150, Is.EqualTo("361F5B114277C3772E3ED4BA008720B31EB0C93BEE6F0BA121976E3DE58C7B59"), "tick 150");
        }
    }
}
