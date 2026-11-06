using NUnit.Framework;

namespace WarCore.Tests
{
    // Recorded on the unchanged code before the auto-fire seam existed; the seam must leave every number as it was.
    public sealed class AutoFireLockTests
    {
        private const int Ai = 88, Player = 18, Intent = 18, Pair = 60;

        [Test]
        public void AiShotsOverSixHundredTicks_AreTheRecordedCount()
        {
            int shots = AutoFireFixtures.AiFires();
            Assert.That(shots, Is.GreaterThanOrEqualTo(1), "the fixture must reach the automatic fire branch");
            Assert.That(shots, Is.EqualTo(Ai));
        }

        [Test]
        public void PlayerShotsInTheHeldWindow_AreTheRecordedCount()
        {
            int shots = AutoFireFixtures.PlayerFires();
            Assert.That(shots, Is.GreaterThanOrEqualTo(1));
            Assert.That(shots, Is.EqualTo(Player));
        }

        [Test]
        public void ExplicitIntentShotsInTheHeldWindow_AreTheRecordedCount()
        {
            int inside = AutoFireFixtures.IntentFires(out int total);
            Assert.That(inside, Is.GreaterThanOrEqualTo(1));
            Assert.That(inside, Is.EqualTo(Intent));
            Assert.That(total, Is.EqualTo(inside), "with no enemy in the world nothing fires outside the intent window");
        }

        [Test]
        public void TwoShootersWithACooldown_FireOnTheirCooldownSchedule()
        {
            var w = AutoFireFixtures.Pair();
            int shots = 0, cooldownTicks = 0;
            for (int tick = 1; tick <= AutoFireFixtures.PairTicks; tick++)
            {
                w.Step();
                shots += w.Combat.Fires.Length;
                Assert.That(w.Dead(0) || w.Dead(1), Is.False);
                if (w.Combat.Fires.Length == 0) cooldownTicks++;
            }
            Assert.That(shots, Is.EqualTo(Pair));
            Assert.That(cooldownTicks, Is.GreaterThanOrEqualTo(1));
        }
    }
}
