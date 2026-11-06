using NUnit.Framework;
using WarCore.Combat;
using WarCore.Sim;

namespace WarCore.Tests
{
    // Where Sense is called: once per tick for each living, uncontrolled unit without a fire intent, before the melee step and the cooldown guard.
    public sealed class AutoFireSenseTests
    {
        [Test]
        public void TwoShootersOnCooldown_EachSenseOncePerTick()
        {
            var w = AutoFireFixtures.Pair();
            var recorder = new RecordingAutoFire(w.Combat.BuiltInAutoFire);
            w.Combat.UseAutoFire(recorder);
            for (int tick = 1; tick <= AutoFireFixtures.PairTicks; tick++)
            {
                recorder.NewTick();
                w.Step();
                Assert.That(w.Dead(0), Is.False); Assert.That(w.Dead(1), Is.False);
                Assert.That(recorder.ThisTick(w.Id(0)), Is.EqualTo(1), "tick " + tick);
                Assert.That(recorder.ThisTick(w.Id(1)), Is.EqualTo(1), "tick " + tick);
            }
            Assert.That(recorder.Senses, Is.EqualTo(2 * AutoFireFixtures.PairTicks));
            Assert.That(recorder.Aims, Is.GreaterThanOrEqualTo(1));
            Assert.That(recorder.CooldownSenses, Is.GreaterThanOrEqualTo(1), "Sense must also run while the weapon is cooling down");
        }

        [Test]
        public void ADeadUnit_IsNotSensedAgain()
        {
            // Shooter 0 kills the unarmed unit 1 with a lethal hit; unit 1 is sensed exactly on the ticks it started alive.
            var w = AutoFireFixtures.Pair(resolver: new LethalResolver(), secondWeapon: -1);
            var recorder = new RecordingAutoFire(w.Combat.BuiltInAutoFire);
            w.Combat.UseAutoFire(recorder);
            int aliveAtStart = 0, deadTicks = 0;
            for (int tick = 1; tick <= 20; tick++)
            {
                if (w.Dead(1)) deadTicks++; else aliveAtStart++;
                w.Step();
            }
            Assert.That(deadTicks, Is.GreaterThanOrEqualTo(1), "the fixture must kill unit 1");
            Assert.That(recorder.Total(w.Id(1)), Is.EqualTo(aliveAtStart));
            Assert.That(recorder.Total(w.Id(0)), Is.EqualTo(20));
        }

        [Test]
        public void ThePlayerControlledUnit_IsNeverSensed()
        {
            var w = AutoFireFixtures.Pair(controlled: true);
            var recorder = new RecordingAutoFire(w.Combat.BuiltInAutoFire);
            w.Combat.UseAutoFire(recorder);
            for (int tick = 1; tick <= 20; tick++) w.Step();
            Assert.That(recorder.Total(w.Id(0)), Is.Zero, "unit 0 is the player");
            Assert.That(recorder.Total(w.Id(1)), Is.EqualTo(20));
        }

        [Test]
        public void ATickWithAnExplicitIntent_IsNotSensedForThatUnit()
        {
            var w = AutoFireFixtures.Pair();
            var recorder = new RecordingAutoFire(w.Combat.BuiltInAutoFire);
            w.Combat.UseAutoFire(recorder);
            w.Produce = c =>
            {
                if (c.Clock.Tick == 5) w.Aim(c, 0, SimVec3.Metres(0, 0, 10));
                if (c.Clock.Tick == 7) w.Target(c, 0, 1);
            };
            for (int tick = 1; tick <= 10; tick++) w.Step();
            Assert.That(recorder.Total(w.Id(0)), Is.EqualTo(8), "ten ticks minus the aimed tick 5 and the targeted tick 7");
            Assert.That(recorder.Total(w.Id(1)), Is.EqualTo(10));
        }

        [Test]
        public void AUnitInTheMiddleOfAMeleeSwing_IsStillSensed()
        {
            var pack = MeleeWorld.Pack(new[] { MeleeWorld.Spec(damage: 1, maxTargets: 4) });
            var w = new MeleeWorld(pack, new[] { MeleeWorld.Unit(1, 1, 20000, 20000, melee: 0), MeleeWorld.Unit(2, 2, 21000, 20000) });
            var recorder = new RecordingAutoFire(w.W.Combat.BuiltInAutoFire);
            w.W.Combat.UseAutoFire(recorder);
            w.Step(7);
            Assert.That(w.Health(2), Is.LessThan(MeleeWorld.Full), "the swing must have landed, so Sense ran through it");
            Assert.That(recorder.Total(w.W.Sim.Entities.StableOf(w.W.Unit(1))), Is.EqualTo(7));
            Assert.That(recorder.Total(w.W.Sim.Entities.StableOf(w.W.Unit(2))), Is.EqualTo(7));
        }
    }
}
