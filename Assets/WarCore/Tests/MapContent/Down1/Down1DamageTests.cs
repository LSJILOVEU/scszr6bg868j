using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Combat;
using WarCore.Sim;
using static WarCore.Tests.Down1.Down1World;

namespace WarCore.Tests.Down1
{
    // DOWN-1 D2-D5, D12: the casualty rule of the damage entry, the step's ledger, finishing, bleeding out and giving up.
    // Health 100 raw, damage raw (a scripted resolver); unit 0 (team 1) shoots unit 1 (team 2) 2 m away unless said otherwise.
    public sealed class Down1DamageTests
    {
        private static Down1World Pair(ScriptedDamage damage, DownedRules rules) =>
            new Down1World(Pack(rules, damage), new[] { U(0, 0, 1), U(2 * M, 0, 2) });

        // Two shooters (units 0, 1, team 1) and a victim (unit 2, team 2).
        private static Down1World Trio(ScriptedDamage damage, DownedRules rules) =>
            new Down1World(Pack(rules, damage), new[] { U(0, 0, 1), U(0, 1 * M, 1), U(2 * M, 0, 2) });

        [TestCase(100L, true, 99L)]
        [TestCase(99L, false, 99L)]
        [TestCase(101L, true, 99L)]
        public void D2_ALethalShotLeavesOneRawAndDowns(long damage, bool downs, long recorded)
        {
            Assert.DoesNotThrow(() =>
            {
                Down1World w = Pair(new ScriptedDamage().Then(damage), Rules());
                w.Shoot(1, 0, 1);
                w.Step();
                Assert.That(w.FiresOn(1), Is.EqualTo(new[] { recorded }));
                Assert.That(w.Health(1), Is.EqualTo(1L));
                Assert.That(w.Dead(1), Is.False);
                Assert.That(w.LifeNotices(1), Is.EqualTo(0));
                Assert.That(w.Downed(1), Is.EqualTo(downs));
                if (!downs) return;
                Assert.That(w.TryCasualty(1, out Casualty c), Is.True);
                Assert.That(c.Kind, Is.EqualTo(Casualty.Downed));
                Assert.That(c.Start, Is.EqualTo(1L));
                Assert.That(c.Due, Is.EqualTo(1801L));
            });
        }

        // The helper sees a death: without combat.downed the same shot kills and publishes life notices.
        [Test]
        public void D2_WithoutDowned_TheSameShotKills()
        {
            Assert.DoesNotThrow(() =>
            {
                Down1World w = Pair(new ScriptedDamage().Then(100), null);
                w.Shoot(1, 0, 1);
                w.Step();
                Assert.That(w.Dead(1), Is.True);
                Assert.That(w.LifeNotices(1), Is.GreaterThan(0));
                Assert.That(w.FiresOn(1), Is.EqualTo(new[] { 100L }));
            });
        }

        [Test]
        public void D3_TwoLethalShotsInOneStep_Down()
        {
            Assert.DoesNotThrow(() =>
            {
                Down1World w = Trio(new ScriptedDamage().Then(100, 100), Rules());
                w.Shoot(1, 0, 2);
                w.Shoot(1, 1, 2);
                w.Step();
                Assert.That(w.FiresOn(2), Is.EqualTo(new[] { 99L, 0L }));
                Assert.That(w.Dead(2), Is.False);
                Assert.That(w.Downed(2), Is.True);
                Assert.That(w.Health(2), Is.EqualTo(1L));
            });
        }

        [TestCase(60L, 100L, 39L, true)]
        [TestCase(60L, 39L, 39L, false)]
        public void D3b_TheSecondShotSeesTheLedger(long first, long second, long recorded, bool downs)
        {
            Assert.DoesNotThrow(() =>
            {
                Down1World w = Trio(new ScriptedDamage().Then(first, second), Rules());
                w.Shoot(1, 0, 2);
                w.Shoot(1, 1, 2);
                w.Step();
                Assert.That(w.FiresOn(2), Is.EqualTo(new[] { first, recorded }));
                Assert.That(w.Health(2), Is.EqualTo(1L));
                Assert.That(w.Downed(2), Is.EqualTo(downs));
                Assert.That(w.LifeNotices(2), Is.EqualTo(0));
            });
        }

        // A blast of 60 health (order 28), then a shot of 40 health (order 30), health 100: down at 1 raw.
        [Test]
        public void D3b_ABlastIsInTheLedger()
        {
            Assert.DoesNotThrow(() =>
            {
                var blasts = new BlastRules(new[] { new BlastSpec("t.b", 5000, 6000, 60, true, false) });
                var w = new Down1World(Pack(Rules(), new ScriptedDamage().Then(40 * M), 100 * M, blasts), new[] { U(0, 0, 1), U(20 * M, 0, 2) });
                w.Blasts.Enqueue(Raw(20 * M, 0, 0), 0, default, 0);
                w.Shoot(1, 0, 1);
                w.Step();
                Assert.That(w.FiresOn(1), Is.EqualTo(new[] { 40 * M - 1 }));
                Assert.That(w.Downed(1), Is.True);
                Assert.That(w.Health(1), Is.EqualTo(1L));
            });
        }

        // 60 on tick 1, 39 on tick 2: the ledger of tick 1 is gone, the health it left (40) is read.
        [Test]
        public void D3b_TheLedgerIsPerStep()
        {
            Assert.DoesNotThrow(() =>
            {
                Down1World w = Pair(new ScriptedDamage().Then(60, 39), Rules());
                w.Shoot(1, 0, 1);
                w.Shoot(2, 0, 1);
                w.StepTo(2);
                Assert.That(w.FiresOn(1), Is.EqualTo(new[] { 39L }));
                Assert.That(w.Health(1), Is.EqualTo(1L));
                Assert.That(w.Downed(1), Is.False);
            });
        }

        // Down on tick 1, finishAfterTicks 15: tick 16 is still zero, tick 17 takes the shot.
        [TestCase(true)]
        [TestCase(false)]
        public void D4_Finishing(bool finish)
        {
            Assert.DoesNotThrow(() =>
            {
                Down1World w = Pair(new ScriptedDamage().Then(100, 50, 50), Rules(finishAfter: 15, finish: finish));
                w.Shoot(1, 0, 1);
                w.Shoot(16, 0, 1);
                w.Shoot(17, 0, 1);
                w.StepTo(16);
                Assert.That(w.FiresOn(1), Is.EqualTo(new[] { 0L }));
                Assert.That(w.Health(1), Is.EqualTo(1L));
                Assert.That(w.Downed(1), Is.True);
                w.Step();
                Assert.That(w.FiresOn(1), Is.EqualTo(new[] { finish ? 50L : 0L }));
                Assert.That(w.Dead(1), Is.EqualTo(finish));
                Assert.That(w.Downed(1), Is.EqualTo(!finish));
                if (!finish) Assert.That(w.Health(1), Is.EqualTo(1L));
            });
        }

        // Down on tick 1, 1800 ticks: alive after tick 1800, dead after tick 1801; nothing records a shot on him.
        [Test]
        public void D5_BleedsOutOnTheDueTick()
        {
            Assert.DoesNotThrow(() =>
            {
                Down1World w = Pair(new ScriptedDamage().Then(100), Rules());
                w.Shoot(1, 0, 1);
                w.Step();
                int records = 0;
                while (w.Tick < 1800) { w.Step(); records += w.FiresOn(1).Count; }
                Assert.That(w.Dead(1), Is.False);
                Assert.That(w.Downed(1), Is.True);
                w.Step();
                records += w.FiresOn(1).Count;
                Assert.That(w.Dead(1), Is.True);
                Assert.That(w.Downed(1), Is.False);
                Assert.That(records, Is.EqualTo(0));
                w.Step();
                Assert.That(w.TryCasualty(1, out _), Is.False);
            });
        }

        // Given up inside a step (Requests, order 3): dead in that step. Between steps: dead in the next one.
        [Test]
        public void D5_GiveUp()
        {
            Assert.DoesNotThrow(() =>
            {
                Down1World w = Pair(new ScriptedDamage().Then(100), Rules());
                w.Shoot(1, 0, 1);
                bool inside = false;
                w.At(5, SimSystemPhase.Requests, 3, c => inside = w.Combat.GiveUp(c, w.Entity(1)));
                w.StepTo(4);
                Assert.That(w.Combat.GiveUp(w.Entity(0)), Is.False, "not downed");
                w.Step();
                Assert.That(inside, Is.True);
                Assert.That(w.Dead(1), Is.True);
                Assert.That(w.Combat.GiveUp(w.Entity(1)), Is.False, "dead");

                Down1World v = Pair(new ScriptedDamage().Then(100), Rules());
                v.Shoot(1, 0, 1);
                v.StepTo(5);
                Assert.That(v.Combat.GiveUp(v.Entity(1)), Is.True);
                Assert.That(v.Dead(1), Is.False);
                Assert.That(v.TryCasualty(1, out Casualty c1), Is.True);
                Assert.That(c1.Due, Is.EqualTo(6L));
                v.Step();
                Assert.That(v.Dead(1), Is.True);
            });
        }

        // After the combat step (Queries, order 31) the in-step give-up throws; between steps Down and Stun throw.
        [Test]
        public void D5_WritesOutsideTheirPhaseThrow()
        {
            Down1World w = Pair(new ScriptedDamage().Then(100), Rules());
            w.Shoot(1, 0, 1);
            Exception late = null;
            w.At(2, SimSystemPhase.Queries, 31, c => { try { w.Combat.GiveUp(c, w.Entity(1)); } catch (Exception e) { late = e; } });
            Assert.DoesNotThrow(() => w.StepTo(2));
            Assert.That(late, Is.InstanceOf<InvalidOperationException>());
            Assert.That(() => w.Combat.Down(w.Sim.Context, w.Entity(0)), Throws.InstanceOf<InvalidOperationException>());
            Assert.That(() => w.Combat.Stun(w.Sim.Context, w.Entity(0), 10), Throws.InstanceOf<InvalidOperationException>());
            Assert.That(() => w.Combat.Down(null, w.Entity(0)), Throws.InstanceOf<InvalidOperationException>());
        }

        // Down (health 100) on tick 1 for 10 ticks; on tick 11 a 30 shot finishes and the bleed takes the other 70: one death.
        [Test]
        public void D5_BleedAndFinishInOneStep_DieOnce()
        {
            Assert.DoesNotThrow(() =>
            {
                Down1World w = Pair(new ScriptedDamage().Then(30), Rules(ticks: 10));
                w.DownAt(1, 1);
                w.Shoot(11, 0, 1);
                w.StepTo(10);
                Assert.That(w.Health(1), Is.EqualTo(100L));
                Assert.That(w.Downed(1), Is.True);
                w.Step();
                Assert.That(w.FiresOn(1), Is.EqualTo(new[] { 30L }));
                Assert.That(w.Dead(1), Is.True);
            });
        }

        // when = gear: a lethal shot kills; a unit downed by Down follows the same rules.
        [Test]
        public void D12_Gear_ALethalShotKills_ADownedUnitFollowsTheRules()
        {
            Assert.DoesNotThrow(() =>
            {
                Down1World w = Pair(new ScriptedDamage().Then(100), Rules(DownedWhen.Gear));
                w.Shoot(1, 0, 1);
                w.Step();
                Assert.That(w.Dead(1), Is.True);
                Assert.That(w.Downed(1), Is.False);

                Down1World v = Pair(new ScriptedDamage().Then(40, 50, 50), Rules(DownedWhen.Gear, ticks: 100, finishAfter: 15));
                v.DownAt(1, 1);
                v.Shoot(1, 0, 1);
                v.Shoot(16, 0, 1);
                v.Shoot(17, 0, 1);
                v.Step();
                Assert.That(v.Downed(1), Is.True);
                Assert.That(v.FiresOn(1), Is.EqualTo(new[] { 0L }));
                Assert.That(v.Health(1), Is.EqualTo(100L));
                v.StepTo(16);
                Assert.That(v.FiresOn(1), Is.EqualTo(new[] { 0L }));
                v.Step();
                Assert.That(v.FiresOn(1), Is.EqualTo(new[] { 50L }));
                Assert.That(v.Health(1), Is.EqualTo(50L));
                Assert.That(v.Combat.GiveUp(v.Entity(1)), Is.True);
                v.Step();
                Assert.That(v.Dead(1), Is.True);
            });
        }

        // Down refuses a unit already down (the bleed tick does not restart) and works only inside a step.
        [Test]
        public void D3_ADownedUnitIsNotDownedAgain()
        {
            Assert.DoesNotThrow(() =>
            {
                Down1World w = Pair(new ScriptedDamage().Then(100), Rules());
                w.Shoot(1, 0, 1);
                bool again = true;
                w.At(5, SimSystemPhase.Queries, 26, c => again = w.Combat.Down(c, w.Entity(1)));
                w.StepTo(5);
                Assert.That(again, Is.False);
                Assert.That(w.TryCasualty(1, out Casualty c1), Is.True);
                Assert.That(c1.Start, Is.EqualTo(1L));
                Assert.That(c1.Due, Is.EqualTo(1801L));
            });
        }
    }
}
