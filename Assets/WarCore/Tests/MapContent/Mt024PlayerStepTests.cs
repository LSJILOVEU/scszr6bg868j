using NUnit.Framework;
using WarCore.Control;
using WarCore.Interim;
using WarCore.Sim;
using static WarCore.Tests.Wp16TestWorld;

namespace WarCore.Tests
{
    public sealed class Mt024PlayerStepTests
    {
        [Test]
        public void DeathTickRejectsCorpseWithRetainedMoveIntent()
        {
            var w = new M6World(Recipe());
            KillAt(w, 1, 1);
            w.Sim.Step(Input(1, 0));
            EntityId corpse = w.Unit(1);
            Assert.That(w.Sim.Clock.Tick, Is.EqualTo(1));
            Assert.That(w.Sim.Entities.IsAlive(corpse), Is.True);
            Assert.That(w.Has<MoveIntent>(1), Is.True);
            Assert.That(w.Sim.GameplayLife(w.Sim.Entities.StableOf(corpse)).State, Is.EqualTo(GameplayLifeState.Dead));

            var step = new PlayerStep(w.Sim, w.System<PlayerControl>());
            Assert.That(step.TryGetStep(w.Sim.Context, out EntityId unit, out SimVec3 displacement), Is.False);
            Assert.That(unit, Is.EqualTo(corpse));
            Assert.That(displacement, Is.EqualTo(default(SimVec3)));
        }
    }
}
