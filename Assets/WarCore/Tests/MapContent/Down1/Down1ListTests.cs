using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Combat;
using WarCore.Sim;
using static WarCore.Tests.Down1.Down1World;

namespace WarCore.Tests.Down1
{
    // DOWN-1 D9 (the combat half of C4.6 and C4.7) and D9c: target pickers, rays, projectiles and blasts on casualties, the
    // fire loop, Submit, blocks; a unit downed earlier in a step does not fire later in it.
    public sealed class Down1ListTests
    {
        // Automatic fire (the default) of unit 0 (team 1): a downed enemy 3 m east (unit 1), a living one 6 m north (unit 2).
        [TestCase(false, 2)]
        [TestCase(true, 1)]
        public void D9_TheAiPickerSkipsCasualtiesUnlessTargetable(bool targetable, int expected)
        {
            Assert.DoesNotThrow(() =>
            {
                var w = new Down1World(Pack(Rules(targetable: targetable), new ScriptedDamage { Fallback = 0 }),
                    new[] { U(0, 0, 1), U(3 * M, 0, 2), U(0, 6 * M, 2) }, auto: true);
                w.DownAt(1, 1);
                var targets = new List<int>();
                for (int i = 0; i < 4; i++)
                {
                    w.Step();
                    foreach (FireRecord f in w.Combat.Fires)
                        if (f.Shooter.Equals(w.Id(0)) && f.Outcome == FireOutcome.Hit) targets.Add(f.Target.Equals(w.Id(1)) ? 1 : f.Target.Equals(w.Id(2)) ? 2 : -1);
                }
                Assert.That(targets, Is.Not.Empty);
                Assert.That(targets, Is.All.EqualTo(expected));
                Assert.That(w.Combat.Targetable(w.Entity(1)), Is.EqualTo(targetable));
                Assert.That(w.Combat.Targetable(w.Entity(2)), Is.True);
            });
        }

        // A melee unit picks its swing's direction: the downed enemy 0.8 m east (unit 1) or the living one 1.2 m west (unit 2).
        [TestCase(false, 2)]
        [TestCase(true, 1)]
        public void D9_TheMeleePickerSkipsCasualtiesUnlessTargetable(bool targetable, int expected)
        {
            Assert.DoesNotThrow(() =>
            {
                var pack = Pack(Rules(targetable: targetable), new ScriptedDamage(), melee: new[] { Swing(3, 0) });
                var w = new Down1World(pack, new[] { U(0, 0, 1, -1, 0), U(M * 8 / 10, 0, 2, -1), U(-M * 12 / 10, 0, 2, -1) });
                w.At(1, SimSystemPhase.Queries, 26, c => w.Combat.Down(c, w.Entity(1)));
                var hit = new List<int>();
                for (int i = 0; i < 6; i++) { w.Step(); hit.AddRange(w.StrikesBy(0, StrikeKind.Landed)); }
                Assert.That(hit, Is.EqualTo(new[] { expected }));
            });
        }

        // An aimed ray at a downed enemy's feet, a projectile and a blast all reach it (finishing on: after 0 ticks of grace).
        [Test]
        public void D9_RaysProjectilesAndBlastsHitCasualties()
        {
            Assert.DoesNotThrow(() =>
            {
                var blasts = new BlastRules(new[] { new BlastSpec("t.b", 1000, 2000, 1, true, false) });
                var weapons = new[] { Gun(), Gun(30000) };
                var pack = Pack(Rules(), new ScriptedDamage { Fallback = 2 }, 100 * M, blasts, weapons: weapons, projectileSpeeds: new[] { 0, 30000 });
                var w = new Down1World(pack, new[] { U(0, 0, 1, 0), U(0, 2 * M, 1, 1), U(4 * M, 0, 2, -1) });
                w.DownAt(1, 2);
                w.At(2, SimSystemPhase.Queries, 27, c => w.Combat.Submit(c, FireIntent.Aimed(w.Id(0), Raw(4 * M, 0, 0))));
                w.StepTo(2);
                Assert.That(w.FiresOn(2), Is.EqualTo(new[] { 2L }), "the ray");
                long before = w.Health(2);
                w.At(3, SimSystemPhase.Queries, 27, c => w.Combat.Submit(c, FireIntent.AtTarget(w.Id(1), w.Id(2))));
                for (int i = 0; i < 10 && w.Health(2) == before; i++) w.Step();
                Assert.That(w.Health(2), Is.EqualTo(before - 2), "the projectile");
                before = w.Health(2);
                w.Blasts.Enqueue(Raw(4 * M, 0, 0), 0, default, 0);
                w.Step();
                Assert.That(w.Health(2), Is.EqualTo(before - 1 * M), "the blast");
                Assert.That(w.Downed(2), Is.True);
            });
        }

        // A casualty's intent is taken (no throw) and it does not fire; its swing in progress is cancelled when it goes down.
        [Test]
        public void D9_ACasualtyIntentIsTakenButNotFired_AndItsSwingIsDropped()
        {
            Assert.DoesNotThrow(() =>
            {
                var pack = Pack(Rules(), new ScriptedDamage { Fallback = 1 }, melee: new[] { Swing(10, 1) });
                var w = new Down1World(pack, new[] { U(0, 0, 1, 0, 0), U(1 * M, 0, 2, -1) });
                w.DownAt(3, 0);
                w.Every(SimSystemPhase.Queries, 27, c => { if (c.Clock.Tick > 3) w.Combat.Submit(c, FireIntent.AtTarget(w.Id(0), w.Id(1))); });
                w.StepTo(2);
                Assert.That(w.MeleeOf(0).Pending, Is.True);
                int fired = 0, struck = 0;
                while (w.Tick < 20) { w.Step(); fired += w.FiresBy(0); struck += w.StrikesBy(0, StrikeKind.Landed).Count; }
                Assert.That(w.MeleeOf(0).Pending, Is.False);
                Assert.That(fired, Is.EqualTo(0));
                Assert.That(struck, Is.EqualTo(0));
            });
        }

        // A block held by a casualty stops nothing, and Hold on a casualty lowers the block without throwing.
        [Test]
        public void D9_ACasualtyDoesNotBlock()
        {
            Assert.DoesNotThrow(() =>
            {
                var pack = Pack(Rules(ticks: 100, finishAfter: 0), new ScriptedDamage(), melee: new[] { Swing(4, 7) }, block: new[] { new BlockSpec(0, 1000) });
                var w = new Down1World(pack, new[] { U(0, 0, 1, -1, 0), U(1 * M, 0, 2, -1, -1, 0) });
                w.Combat.Hold(w.Entity(1), new SimVec3(SimFix.FromInt(-1), default, default));
                w.DownAt(1, 1);
                var blocked = new List<int>();
                var landed = new List<int>();
                for (int i = 0; i < 6; i++) { w.Step(); blocked.AddRange(w.StrikesBy(0, StrikeKind.Blocked)); landed.AddRange(w.StrikesBy(0, StrikeKind.Landed)); }
                Assert.That(blocked, Is.Empty);
                Assert.That(landed, Is.EqualTo(new[] { 1 }));
                Assert.That(w.Health(1), Is.EqualTo(100L - 7));
                Assert.That(w.Combat.IsHolding(w.Entity(1)), Is.True, "the hold state itself is left as it was");
                w.Combat.Hold(w.Entity(1), new SimVec3(SimFix.FromInt(-1), default, default));
                Assert.That(w.Combat.IsHolding(w.Entity(1)), Is.False);
                Assert.That(() => w.Combat.Hold(w.Entity(1), default), Throws.Nothing);
            });
        }

        // The same block on a living unit stops the blow (the control of the test above).
        [Test]
        public void D9_ALivingUnitBlocks()
        {
            Assert.DoesNotThrow(() =>
            {
                var pack = Pack(Rules(), new ScriptedDamage(), melee: new[] { Swing(4, 7) }, block: new[] { new BlockSpec(0, 1000) });
                var w = new Down1World(pack, new[] { U(0, 0, 1, -1, 0), U(1 * M, 0, 2, -1, -1, 0) });
                w.Combat.Hold(w.Entity(1), new SimVec3(SimFix.FromInt(-1), default, default));
                var blocked = new List<int>();
                for (int i = 0; i < 6; i++) { w.Step(); blocked.AddRange(w.StrikesBy(0, StrikeKind.Blocked)); }
                Assert.That(blocked, Is.EqualTo(new[] { 1 }));
                Assert.That(w.Health(1), Is.EqualTo(100L));
            });
        }

        // D9c: units 0 and 1 shoot each other on tick 1 with lethal shots; unit 0 comes first. With combat.downed unit 1 is down
        // before its turn and does not fire; without it both shots go off.
        [TestCase(true)]
        [TestCase(false)]
        public void D9c_AUnitDownedEarlierInTheStepDoesNotFire(bool downed)
        {
            Assert.DoesNotThrow(() =>
            {
                var w = new Down1World(Pack(downed ? Rules() : null, new ScriptedDamage().Then(100, 100)), new[] { U(0, 0, 1), U(2 * M, 0, 2) });
                w.Shoot(1, 0, 1);
                w.Shoot(1, 1, 0);
                w.Step();
                Assert.That(w.FiresBy(0), Is.EqualTo(1));
                Assert.That(w.FiresBy(1), Is.EqualTo(downed ? 0 : 1));
                Assert.That(w.Dead(0), Is.EqualTo(!downed));
                Assert.That(w.Dead(1), Is.EqualTo(!downed));
                Assert.That(w.Downed(1), Is.EqualTo(downed));
            });
        }
    }
}
