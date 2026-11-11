using NUnit.Framework;
using WarCore.Combat;

namespace WarCore.Tests
{
    public sealed class Mt016CombatSystemTests
    {
        [Test]
        public void NewTickSubmissionClearsPendingShootersAfterSkippedCombatStep()
        {
            var w = new Wp13TestWorld();
            // Leave this system unregistered so its combat step is skipped across ticks.
            var combat = new CombatSystem(w.Sim, w.Positions, w.Combatants, w.Pack, w.WallMask, 1, 2);
            w.Produce = c => combat.Submit(c, FireIntent.AtTarget(w.Id(0), w.Id(1)));
            w.Step();
            w.Produce = c => combat.Submit(c, FireIntent.AtTarget(w.Id(1), w.Id(0)));
            w.Step();

            Assert.That((int)Wp13IntentTests.Field("intentCount").GetValue(combat), Is.EqualTo(1));
        }
    }
}
