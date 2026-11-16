using NUnit.Framework;
using WarCore.Combat;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class Mt017DefaultAutoFireTests
    {
        [Test]
        public void AimAt_AllyRollEqualsMissThreshold_HoldsFire()
        {
            var rng = Wp13AimedTests.ReplayRandom();
            int roll = rng.NextInt(rng.Stream("combat.ally-check"), 65536);
            var pack = M5TestWorld.Rules(M5TestWorld.Pack(new ZeroResolver()), false, roll);
            var w = new M5TestWorld(new[] { M5TestWorld.Spawn(1, 0), M5TestWorld.Spawn(2, 3000, weapon: -1),
                M5TestWorld.Spawn(3, 10000, team: 2, weapon: -1) }, pack);
            var fire = (DefaultAutoFire)w.Combat.BuiltInAutoFire;
            var position = new Position(w.Point(0));

            Assert.That(fire.AimAt(w.Combat, w.Unit(0), in position, w.Unit(2), out _), Is.EqualTo(AutoFireAction.HoldFire));
        }
    }
}
