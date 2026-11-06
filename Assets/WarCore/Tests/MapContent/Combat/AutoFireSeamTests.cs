using NUnit.Framework;
using WarCore.Combat;

namespace WarCore.Tests
{
    // The auto-fire seam: the built-in decision reproduces the recorded counts, and a replacement decides alone.
    public sealed class AutoFireSeamTests
    {
        [Test]
        public void ForwardingTheBuiltInDecision_KeepsEveryRecordedCount()
        {
            int ai = AutoFireFixtures.AiFires(c => c.UseAutoFire(new RecordingAutoFire(c.BuiltInAutoFire)));
            int player = AutoFireFixtures.PlayerFires(c => c.UseAutoFire(new RecordingAutoFire(c.BuiltInAutoFire)));
            int intent = AutoFireFixtures.IntentFires(out _, c => c.UseAutoFire(new RecordingAutoFire(c.BuiltInAutoFire)));
            Assert.That(ai, Is.EqualTo(88));
            Assert.That(player, Is.EqualTo(18));
            Assert.That(intent, Is.EqualTo(18));
        }

        [Test]
        public void ANeverFiringReplacement_StopsTheAiButNotThePlayerOrAnIntent()
        {
            var forAi = new SilentAutoFire(); var forPlayer = new SilentAutoFire(); var forIntent = new SilentAutoFire();
            Assert.That(AutoFireFixtures.AiFires(c => c.UseAutoFire(forAi)), Is.Zero);
            Assert.That(forAi.Aims, Is.GreaterThanOrEqualTo(1), "the replacement must have been consulted");
            Assert.That(AutoFireFixtures.PlayerFires(c => c.UseAutoFire(forPlayer)), Is.EqualTo(18));
            Assert.That(AutoFireFixtures.IntentFires(out _, c => c.UseAutoFire(forIntent)), Is.EqualTo(18));
        }

        [Test]
        public void PassingNull_RestoresTheBuiltInDecision()
        {
            int shots = AutoFireFixtures.AiFires(c => { c.UseAutoFire(new SilentAutoFire()); c.UseAutoFire(null); });
            Assert.That(shots, Is.EqualTo(88));
        }

        [Test]
        public void TheReplacementIsConsultedOnlyForReadyUnits_AndTheShotCountDoesNotChange()
        {
            var w = AutoFireFixtures.Pair();
            var recorder = new RecordingAutoFire(w.Combat.BuiltInAutoFire);
            w.Combat.UseAutoFire(recorder);
            int shots = 0;
            for (int tick = 1; tick <= AutoFireFixtures.PairTicks; tick++) { w.Step(); shots += w.Combat.Fires.Length; }
            Assert.That(shots, Is.EqualTo(60));
            Assert.That(recorder.Aims, Is.GreaterThanOrEqualTo(60), "at least every shot began with a ready unit");
        }

        [TestCase(AutoFireAction.Fire)]
        [TestCase(AutoFireAction.HoldFire)]
        public void AnIdlePlayer_IsNeverOfferedToTheReplacement_WhateverItReturns(AutoFireAction action)
        {
            var w = AutoFireFixtures.Pair(controlled: true);
            var replacement = new ConstantAutoFire(action);
            w.Combat.UseAutoFire(replacement);
            var before = w.State(0);
            int playerShots = 0, otherShots = 0;
            for (int tick = 1; tick <= 20; tick++)
            {
                w.Step();
                foreach (var fire in w.Combat.Fires) { if (fire.Shooter.Equals(w.Id(0))) playerShots++; else otherShots++; }
                Assert.That(w.State(0), Is.EqualTo(before), "an idle player keeps its weapon state, tick " + tick);
            }
            Assert.That(playerShots, Is.Zero);
            Assert.That(replacement.Offered.Count, Is.GreaterThanOrEqualTo(1), "the other unit is offered");
            Assert.That(replacement.Offered, Has.None.EqualTo(w.Id(0)), "the player is never offered");
            if (action == AutoFireAction.Fire) Assert.That(otherShots, Is.GreaterThanOrEqualTo(1), "the replacement does decide for the other unit");
            else Assert.That(otherShots, Is.Zero);
        }
    }
}
