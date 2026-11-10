using NUnit.Framework;
using WarCore.Control;
using WarCore.Sim;
using static WarCore.Tests.Wp16TestWorld;

namespace WarCore.Tests
{
    public sealed class Mt022PlayerFallbackTests
    {
        [Test]
        public void FinishedMatch_DirectStepKeepsDeadPlayer()
        {
            var w = new M6World(Recipe(new[] {
                U(1), U(2, 14000), U(3, 70000, 35000, team: 2)
            }, match: Match(max: 1)));
            KillAt(w, 1, 1); w.Step();
            Assert.That(w.Sim.IsFinished, Is.True);
            Assert.That(w.Dead(1), Is.True); Assert.That(w.Dead(2), Is.False);
            var events = (SimEventBuffer)w.Sim.Events;
            long tick = w.Sim.Clock.Tick + 1;
            events.BeginTick(tick);
            w.Sim.Context.Begin(default);
            try { w.System<PlayerFallback>().Step(w.Sim.Context); }
            finally { w.Sim.Context.End(); events.EndTick(); }
            Assert.That(w.Session.ControlledKey, Is.EqualTo(1UL));
            Assert.That(events.Read(tick).Length, Is.Zero);
        }

        [Test]
        public void LivingPlayer_OverlappingLowerStableIdAllyDoesNotTakeControl()
        {
            ulong player = StableEntityKey.Placed(1, 1).Id.Value > StableEntityKey.Placed(1, 2).Id.Value ? 1UL : 2UL;
            var w = new M6World(Recipe(new[] {
                U(1), U(2), U(3, 70000, 35000, team: 2)
            }, player: player));
            w.Step();
            Assert.That(w.Dead(1), Is.False); Assert.That(w.Dead(2), Is.False);
            Assert.That(w.Session.ControlledKey, Is.EqualTo(player));
            Assert.That(Switches(w), Is.Zero);
        }
    }
}
