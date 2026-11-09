using NUnit.Framework;
using WarCore.Combat;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.L10Fixture;

namespace WarCore.Tests
{
    // L10 (10, A4 already in main): on the frozen demo terrain the player's aimed shot takes the input height. Uphill and downhill,
    // aiming at the enemy's feet height hits; aiming at the shooter's own height (explicitly horizontal) does not. No input height
    // and the session's ground height give the same shot. Weapons: the frozen L6 combat without spread.
    public sealed class L10AimHeightTests
    {
        private static FireRecord Shot(long playerXMm, long enemyXMm, bool useEnemyY, bool noHeight = false)
        {
            var enemy = new[] { U(3, enemyXMm, 6500, team: 2) };
            var w = Demo(playerXMm, 6500, extra: enemy, combat: M5TestWorld.NoSpread(L6.Pack().BuildCombat()), weapon: 0);
            long shooterY = At(w).Y.Raw * 1000 / S, targetY = At(w, 3).Y.Raw * 1000 / S;
            Assert.That(playerXMm < enemyXMm ? (shooterY, targetY > 0) : (targetY, shooterY > 0), Is.EqualTo((0L, true)),
                "one end on flat ground at Y 0, the other up the slope");
            long? aimY = noHeight ? (long?)null : useEnemyY ? targetY : shooterY;
            w.Session.SetPlayerInput(1, new PlayerInput(0, 0, enemyXMm, 6500, true, aimYMm: aimY));
            w.Session.StepOnce();
            Assert.That(w.Combat.Fires.Length, Is.EqualTo(1), "one player shot");
            return w.Combat.Fires[0];
        }

        [Test]
        public void L10Aim_UphillAtTheEnemyHeight_Hits_ShooterHeight_DoesNot()
        {
            Assert.That(Shot(8500, 15500, true).Outcome, Is.EqualTo(FireOutcome.Hit), "aim at the enemy's height");
            Assert.That(Shot(8500, 15500, false).Outcome, Is.Not.EqualTo(FireOutcome.Hit), "explicitly horizontal");
        }

        [Test]
        public void L10Aim_DownhillAtTheEnemyHeight_Hits_ShooterHeight_DoesNot()
        {
            Assert.That(Shot(15500, 8500, true).Outcome, Is.EqualTo(FireOutcome.Hit), "aim at the enemy's height");
            Assert.That(Shot(15500, 8500, false).Outcome, Is.Not.EqualTo(FireOutcome.Hit), "explicitly horizontal");
        }

        // The ground height of the demo map's session is 0, so no input height equals aimYMm = 0 (A4 null rule): same end, same outcome.
        [TestCase(8500, 15500), TestCase(15500, 8500)]
        public void L10Aim_NoHeightEqualsTheGroundHeight(long playerXMm, long enemyXMm)
        {
            FireRecord none = Shot(playerXMm, enemyXMm, false, noHeight: true);
            var enemy = new[] { U(3, enemyXMm, 6500, team: 2) };
            var w = Demo(playerXMm, 6500, extra: enemy, combat: M5TestWorld.NoSpread(L6.Pack().BuildCombat()), weapon: 0);
            w.Session.SetPlayerInput(1, new PlayerInput(0, 0, enemyXMm, 6500, true, aimYMm: 0));
            w.Session.StepOnce();
            FireRecord ground = w.Combat.Fires[0];
            Assert.That((ground.End, ground.Outcome), Is.EqualTo((none.End, none.Outcome)));
        }
    }
}
