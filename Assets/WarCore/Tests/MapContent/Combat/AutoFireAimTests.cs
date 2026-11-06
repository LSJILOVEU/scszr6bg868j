using NUnit.Framework;
using WarCore.Combat;
using WarCore.Sim;

namespace WarCore.Tests
{
    // The built-in decision's zero-direction rule: a target standing on the shooter gives no heading, so the shot goes along +X.
    public sealed class AutoFireAimTests
    {
        [Test]
        public void AEnemyOnTheShootersPoint_IsShotAlongPlusX()
        {
            var weapon = new WeaponDef(3000, 100, 9000, default, SimFix.FromInt(67), 0);
            var units = new[] { M5TestWorld.Spawn(1, 0, weapon: 0), M5TestWorld.Spawn(2, 0, team: 2, weapon: -1, squad: 2) };
            var w = new Wp13TestWorld(units, M5TestWorld.Pack(new ZeroResolver(), weapons: new[] { weapon }), controlled: false);
            // Same horizontal point, but the enemy stands 5 m higher, so a ray along +X from the shooter passes under it.
            var above = w.Point(1) + SimVec3.Metres(0, 5, 0);
            w.Sim.Entities.Set(w.Positions, w.Unit(1), new Position(above));
            w.Step();
            Assert.That(w.Combat.Fires.Length, Is.EqualTo(1));
            var fire = w.Combat.Fires[0];
            Assert.That(fire.End.Z, Is.EqualTo(fire.Origin.Z));
            Assert.That(fire.End.X, Is.EqualTo(fire.Origin.X + weapon.FalloffEnd), "the whole range along +X");
            Assert.That(fire.Outcome, Is.EqualTo(FireOutcome.Open));
        }
    }
}
