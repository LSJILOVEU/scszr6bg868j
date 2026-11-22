using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Combat;
using WarCore.Sim;
using static WarCore.Tests.Down1.Down1World;

namespace WarCore.Tests.Down1
{
    // DOWN-1 D8 (the combat half) and D8b: stuns, their length and stacking, the gate, the cancelled swing, blast stuns.
    public sealed class Down1StunTests
    {
        internal const UnitAction All = (UnitAction)(2 * (int)UnitAction.Capture - 1);

        private static Down1World Pair(DownedRules rules = null) =>
            new Down1World(Pack(rules ?? Rules(), new ScriptedDamage { Fallback = 1 }), new[] { U(0, 0, 1), U(2 * M, 0, 2) });

        // Stunned 60 on tick 5: no shot on ticks 5..64, a shot on tick 65; the gate denies everything until tick 65.
        [Test]
        public void D8_AStunnedUnitDoesNotFireUntilItWakes()
        {
            Assert.DoesNotThrow(() =>
            {
                Down1World w = Pair();
                w.Every(SimSystemPhase.Queries, 27, c => w.Combat.Submit(c, FireIntent.AtTarget(w.Id(0), w.Id(1))));
                bool stunned = false;
                w.At(5, SimSystemPhase.Queries, 26, c => stunned = w.Combat.Stun(c, w.Entity(0), 60));
                var fired = new List<long>();
                while (w.Tick < 70)
                {
                    w.Step();
                    if (w.FiresBy(0) > 0) fired.Add(w.Tick);
                    if (w.Tick == 64)
                    {
                        Assert.That(w.Stunned(0), Is.True);
                        Assert.That(w.Gate.Denied(w.Entity(0)), Is.EqualTo(All));
                        Assert.That(w.Combat.Allows(w.Entity(0), UnitAction.Move), Is.False);
                        Assert.That(w.Combat.CannotAct(w.Entity(0)), Is.True);
                    }
                }
                Assert.That(stunned, Is.True);
                Assert.That(fired, Is.EqualTo(new long[] { 1, 2, 3, 4, 65, 66, 67, 68, 69, 70 }));
                Assert.That(w.Stunned(0), Is.False);
                Assert.That(w.Combat.Allows(w.Entity(0), UnitAction.Fire), Is.True);
                Assert.That(w.TryCasualty(0, out _), Is.False, "removed on the wake tick");
            });
        }

        // Stun 60 on tick 5, then 30 the same tick (no shorter), 90 the same tick (wakes at 95), 90 on tick 10 (wakes at 100);
        // the start stays 5. A downed unit takes no stun.
        [Test]
        public void D8_StunsStackToTheLaterWake()
        {
            Assert.DoesNotThrow(() =>
            {
                Down1World w = Pair();
                var answers = new List<bool>();
                w.At(5, SimSystemPhase.Queries, 26, c =>
                {
                    answers.Add(w.Combat.Stun(c, w.Entity(0), 60));
                    answers.Add(w.Combat.Stun(c, w.Entity(0), 30));
                });
                w.StepTo(5);
                Assert.That(w.TryCasualty(0, out Casualty a), Is.True);
                Assert.That((a.Start, a.Due), Is.EqualTo((5L, 65L)));
                w.At(6, SimSystemPhase.Queries, 26, c => answers.Add(w.Combat.Stun(c, w.Entity(0), 89)));
                w.At(10, SimSystemPhase.Queries, 26, c => answers.Add(w.Combat.Stun(c, w.Entity(0), 90)));
                w.StepTo(6);
                Assert.That(w.TryCasualty(0, out Casualty b), Is.True);
                Assert.That((b.Start, b.Due), Is.EqualTo((5L, 95L)));
                w.StepTo(10);
                Assert.That(w.TryCasualty(0, out Casualty d), Is.True);
                Assert.That((d.Start, d.Due), Is.EqualTo((5L, 100L)));
                Assert.That(answers, Is.All.True);
            });
        }

        [Test]
        public void D8_ADownedOrDeadUnitTakesNoStun_AndAStunNeedsATick()
        {
            Down1World w = new Down1World(Pack(Rules(), new ScriptedDamage().Then(100)), new[] { U(0, 0, 1), U(2 * M, 0, 2) });
            w.Shoot(1, 0, 1);
            bool downedAnswer = true;
            Exception zero = null;
            w.At(2, SimSystemPhase.Queries, 26, c =>
            {
                downedAnswer = w.Combat.Stun(c, w.Entity(1), 30);
                try { w.Combat.Stun(c, w.Entity(0), 0); } catch (Exception e) { zero = e; }
            });
            Assert.DoesNotThrow(() => w.StepTo(2));
            Assert.That(downedAnswer, Is.False);
            Assert.That(w.TryCasualty(1, out Casualty c1) && c1.Kind == Casualty.Downed, Is.True);
            Assert.That(zero, Is.InstanceOf<ArgumentOutOfRangeException>());
        }

        // A stun that ran out but was not removed yet (the stun of tick 5 ends on tick 15) is replaced by a new one.
        [Test]
        public void D8_AnEndedStunIsReplaced()
        {
            Assert.DoesNotThrow(() =>
            {
                Down1World w = Pair();
                bool again = false;
                w.At(5, SimSystemPhase.Queries, 26, c => w.Combat.Stun(c, w.Entity(0), 10));
                w.At(15, SimSystemPhase.Queries, 26, c => again = w.Combat.Stun(c, w.Entity(0), 10));
                w.StepTo(15);
                Assert.That(again, Is.True);
                Assert.That(w.TryCasualty(0, out Casualty c), Is.True);
                Assert.That((c.Start, c.Due), Is.EqualTo((15L, 25L)));
            });
        }

        // A swing started on tick 1 (windup 10, lands on 11) is cancelled by a stun on tick 5 (until 15): it never lands; the
        // unit swings anew once awake.
        [Test]
        public void D8_ASwingCutByAStunNeverLands()
        {
            Assert.DoesNotThrow(() =>
            {
                var pack = Pack(Rules(), new ScriptedDamage(), melee: new[] { Swing(10, 5) });
                var w = new Down1World(pack, new[] { U(0, 0, 1, -1, 0), U(1 * M, 0, 2, -1) });
                w.At(5, SimSystemPhase.Queries, 26, c => w.Combat.Stun(c, w.Entity(0), 10));
                var landed = new List<long>();
                var started = new List<long>();
                while (w.Tick < 30)
                {
                    w.Step();
                    if (w.StrikesBy(0, StrikeKind.Landed).Count > 0) landed.Add(w.Tick);
                    if (w.StrikesBy(0, StrikeKind.Started).Count > 0) started.Add(w.Tick);
                    if (w.Tick == 5) Assert.That(w.MeleeOf(0).Pending, Is.False);
                }
                Assert.That(started[0], Is.EqualTo(1L));
                Assert.That(started[1], Is.EqualTo(15L));
                Assert.That(landed, Is.EqualTo(new long[] { 25 }));
            });
        }

        // D8b: rwr.frag-like blast (inner 2 m, outer 6 m, 100 health, stun 60) at (0, 0) on tick 1, health 100 health units:
        // unit 0 at 1 m goes down (no stun), unit 1 at 4 m is hurt and stunned, unit 2 at 7 m is out of reach, unit 3 at 3 m is
        // shielded, unit 4 at 3 m stands behind a wall.
        [Test]
        public void D8b_ABlastStunsWhomItHurtsButDoesNotDown()
        {
            Assert.DoesNotThrow(() =>
            {
                var blasts = new BlastRules(new[] { new BlastSpec("t.frag", 2000, 6000, 100, true, true, 60) });
                var shield = new Down1Shield();
                var walls = new[] { new Ml3World.Wall(-2 * M, -2 * M - M / 2, 2 * M, -2 * M, 0, 3 * M) };
                var units = new[] { U(1 * M, 0, 2, -1), U(4 * M, 0, 2, -1), U(7 * M, 0, 2, -1), U(0, 3 * M, 2, -1), U(0, -3 * M, 2, -1) };
                var w = new Down1World(Pack(Rules(), new ScriptedDamage(), 100 * M, blasts), units, walls, shield);
                shield.Add(w.Id(3));
                w.Blasts.Enqueue(Raw(0, 0, 0), 0, default, 0);
                w.Step();
                Assert.That(w.Downed(0), Is.True);
                Assert.That(w.TryCasualty(0, out Casualty c0) && c0.Kind == Casualty.Downed, Is.True);
                Assert.That(w.Stunned(1), Is.True);
                Assert.That(w.TryCasualty(1, out Casualty c1), Is.True);
                Assert.That((c1.Kind, c1.Start, c1.Due), Is.EqualTo((Casualty.Stunned, 1L, 61L)));
                Assert.That(w.Health(1), Is.LessThan(100 * M));
                for (int i = 2; i < 5; i++) Assert.That(w.TryCasualty(i, out _), Is.False, "unit " + i);
                Assert.That(w.Health(3), Is.EqualTo(100 * M));
                Assert.That(w.Health(4), Is.EqualTo(100 * M));
            });
        }

        // A pack whose blasts all have stunTicks 0 and no combat.downed keeps no casualties: its world has no component.
        [Test]
        public void D8b_NoStunNoCasualties()
        {
            var plain = new BlastRules(new[] { new BlastSpec("t.frag", 2000, 6000, 100, true, true) });
            Assert.That(plain.Stuns, Is.False);
            Assert.That(Pack(null, new ScriptedDamage(), 100 * M, plain).UsesCasualties, Is.False);
            var stun = new BlastRules(new[] { new BlastSpec("t.a", 2000, 6000, 100, true, true), new BlastSpec("t.b", 2000, 6000, 100, true, true, 1) });
            Assert.That(stun.Stuns, Is.True);
            Assert.That(Pack(null, new ScriptedDamage(), 100 * M, stun).UsesCasualties, Is.True);
            Down1World w = new Down1World(Pack(null, new ScriptedDamage(), 100 * M, plain), new[] { U(0, 0, 1) });
            Assert.That(() => w.Sim.Entities.Resolve<Casualty>(), Throws.Exception);
            Assert.That(w.Combat.HasCasualties, Is.False);
        }

        // A stun-only world (blast stuns, no combat.downed): stuns work; Down refuses.
        [Test]
        public void D8b_AStunOnlyWorldDownsNobody()
        {
            Assert.DoesNotThrow(() =>
            {
                var blasts = new BlastRules(new[] { new BlastSpec("t.frag", 2000, 6000, 100, true, true, 60) });
                var w = new Down1World(Pack(null, new ScriptedDamage(), 100 * M, blasts), new[] { U(4 * M, 0, 2, -1), U(1 * M, 0, 2, -1) });
                bool down = true;
                w.At(1, SimSystemPhase.Queries, 26, c => down = w.Combat.Down(c, w.Entity(0)));
                w.Blasts.Enqueue(Raw(0, 0, 0), 0, default, 0);
                w.Step();
                Assert.That(down, Is.False);
                Assert.That(w.Stunned(0), Is.True);
                Assert.That(w.Dead(1), Is.True, "a lethal blast kills without combat.downed");
            });
        }
    }
}
