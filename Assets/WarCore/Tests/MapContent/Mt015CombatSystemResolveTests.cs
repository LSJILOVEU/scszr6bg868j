using NUnit.Framework;
using WarCore.Combat;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class Mt015CombatSystemResolveTests
    {
        [Test]
        public void SpreadKeepsTheQuantizedShotEndpoint()
        {
            var weapon = new WeaponDef(3390, 30, 60000, SimFix.FromInt(34), SimFix.FromInt(67), 32768,
                new SpreadSpec(17953, 0, 0, 17953));
            var w = new Wp13TestWorld(new[] { M5TestWorld.Spawn(1, 0) },
                M5TestWorld.Pack(new ZeroResolver(), weapons: new[] { weapon }));
            w.Produce = c => w.Aim(c, 0, SimVec3.Metres(1, 0, 0)); w.Step();
            Assert.That(w.Combat.Fires.Length, Is.EqualTo(1));
            Assert.That(w.Combat.Fires[0].Outcome, Is.EqualTo(FireOutcome.Open));
            // Seed 41 draws 27624: -2818 millidegrees becomes -513 raw turns.
            Assert.That(w.Combat.Fires[0].End, Is.EqualTo(new SimVec3(
                SimFix.FromRaw(4385552), default, SimFix.FromRaw(-215807))));
        }

        [Test]
        public void AtTargetFiresAtMaximumRange()
        {
            var w = new Wp13TestWorld(M5TestWorld.Pair(distanceMm: 67000));
            w.Produce = c => w.Target(c, 0, 1); w.Step();
            Assert.That(w.Combat.Fires.Length, Is.EqualTo(1));
            Assert.That(w.Combat.Fires[0].Target, Is.EqualTo(w.Id(1)));
            Assert.That(w.Combat.Fires[0].Distance, Is.EqualTo(SimFix.FromInt(67)));
            Assert.That(w.State(0).Ammo, Is.EqualTo(29));
            Assert.That(w.EventCount(GameplayEvents.Damage), Is.EqualTo(1));
        }
    }
}
