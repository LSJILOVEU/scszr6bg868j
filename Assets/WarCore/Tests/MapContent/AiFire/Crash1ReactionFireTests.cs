using NUnit.Framework;
using WarCore.Combat;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class Crash1ReactionFireTests
    {
        [Test]
        public void TargetLosesPositionBetweenSamples_ClearsTheEntryWithoutFiring()
        {
            const long Tick = 61;
            M6World w = P2.World(P2.Cast(), P2.Ai(15, 10));
            for (long tick = 1; tick < Tick; tick++)
                w.Sim.Step(P2.Drive(w, tick, P1.Walk));
            Assert.That(P1.Selected(w, P2.KeyA), Is.EqualTo(P2.Id(w, P2.KeyB).Value));
            Assert.That(P2.Count(w), Is.EqualTo(1));
            Assert.That(P2.Samples(P2.Id(w, P2.KeyA).Value, Tick, 10), Is.False);
            Combatant before = w.Read<Combatant>(P2.KeyA);
            Assert.That((before.Ammo, before.DueMilli), Is.EqualTo((3, 0L)));

            w.Sim.Entities.Remove<Position>(w.Unit(P2.KeyB));
            Assert.That(P2.Present(w, P2.KeyB), Is.True);
            Assert.That(w.Dead(P2.KeyB), Is.False);
            Assert.That(w.Has<Position>(P2.KeyB), Is.False);
            Assert.DoesNotThrow(() => w.Sim.Step(P2.Drive(w, Tick, P1.Walk)));
            Assert.That(P2.Count(w), Is.EqualTo(0));
            Assert.That(P1.Fired(w, P2.KeyA), Is.False);
            Combatant after = w.Read<Combatant>(P2.KeyA);
            Assert.That((after.Ammo, after.DueMilli), Is.EqualTo((3, 0L)));
        }
    }
}
