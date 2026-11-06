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
    }
}
