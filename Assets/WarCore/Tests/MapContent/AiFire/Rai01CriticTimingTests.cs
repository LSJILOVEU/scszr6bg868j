using NUnit.Framework;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class Rai01CriticTimingTests
    {
        [Test]
        public void LongBound_TargetSwitchKeepsUnelapsedPause()
        {
            const long tick = long.MaxValue - 20;
            M6World w = P3.RunTo(P1.Ai(1000), 40);
            var cadence = new BurstCadence(w.Combat);
            var fire = new ReactionFire(w.Combat, w.Combat.BuiltInAutoFire, 0, 1, 1, 40, cadence);
            EntityId shooter = w.Unit(P3.KeyA);
            Combatant unit = w.Read<Combatant>(P3.KeyA);
            Position at = w.Read<Position>(P3.KeyA);
            WeaponDef weapon = w.Combat.Pack.Weapons[unit.Weapon];
            Assert.That(P1.Selected(w, P3.KeyA), Is.EqualTo(P2.Id(w, P3.KeyB).Value));
            fire.Sense(w.Combat, shooter, in unit, in at, tick);
            Assert.That(fire.TryAim(w.Combat, shooter, in unit, in at, in weapon, tick, out _), Is.EqualTo(AutoFireAction.Fire));

            w.Set(P3.KeyC, new Position(A2.Cell(16, 10)));
            w.Sim.CompleteSpatialBoundary();
            Assert.That(P1.Selected(w, P3.KeyA), Is.EqualTo(P2.Id(w, P3.KeyC).Value));
            fire.Sense(w.Combat, shooter, in unit, in at, tick + 1);
            Assert.That(fire.TryAim(w.Combat, shooter, in unit, in at, in weapon, tick + 1, out _),
                Is.EqualTo(AutoFireAction.Pass), "one tick elapsed in the 40-tick pause");
        }
    }
}
