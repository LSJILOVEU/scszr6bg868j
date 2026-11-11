using NUnit.Framework;
using WarCore.Combat;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class Crash1Critic1Tests
    {
        [Test, Explicit("Known CRASH-1 regression; run by full name."), Category("Manual")]
        public void TargetLosesPosition_BeforeThinkingTick_DoesNotFault()
        {
            M6World w = P2.World(P2.Cast(), P2.Ai(15, 10));
            for (long tick = 1; tick <= 55; tick++)
                w.Sim.Step(P2.Drive(w, tick, P1.Walk));
            Assert.That(P2.Count(w), Is.EqualTo(1));
            Assert.That(w.Read<Combatant>(P2.KeyA).Ammo, Is.EqualTo(3));
            Assert.That(w.Recipe.Match.ThinkInterval, Is.EqualTo(10));
            Assert.That(P2.Samples(P2.Id(w, P2.KeyA).Value, 56, 10), Is.True);

            EntityId target = w.Unit(P2.KeyB);
            StableId id = P2.Id(w, P2.KeyB);
            w.Sim.Entities.Remove<Position>(target);
            Assert.That(w.Sim.Entities.TryResolve(id, out _), Is.True);
            Assert.That(w.Dead(P2.KeyB), Is.False);
            Assert.That(w.Has<Position>(P2.KeyB), Is.False);

            Assert.DoesNotThrow(() => w.Sim.Step(default), "tick 56");
            Assert.That(P2.Count(w), Is.EqualTo(0));
            Assert.That(P1.Fired(w, P2.KeyA), Is.False);
            Assert.That(w.Read<Combatant>(P2.KeyA).Ammo, Is.EqualTo(3));
        }
    }
}
