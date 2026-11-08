using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Combat;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // Acceptance 2-4: when A first fires at a target, against the tick t its selector first picks it (read after each step;
    // checked against the combat step by the R = 0 run, where A, ready, fires on t itself).
    public sealed class P1TimingTests
    {
        private static void NoFire(P1.Trace r, long from, long to, string what)
        {
            for (long t = from; t <= to; t++) Assert.That(r.FireA[(int)t], Is.False, what + ", tick " + t);
        }

        // The selector picks B first on T = 40 in every run of the walk; with R = 0, A (ready) fires on T itself.
        [Test]
        public void TestSideSelection_MatchesTheCombatStep()
        {
            P1.Trace r = P1.Run(P1.Cast(), P1.Walk, null, 60);
            Assert.That(r.FirstSelect(P1.KeyA, P1.KeyB), Is.EqualTo(P1.T));
            Assert.That(r.SelA[(int)P1.T - 1], Is.EqualTo(0UL), "nothing selectable on t - 1");
            Assert.That(r.DueA[(int)P1.T - 1], Is.LessThanOrEqualTo(P1.T * 1000), "A is ready on t");
            Assert.That(r.FirstFire(P1.KeyA), Is.EqualTo(P1.T));
        }

        // 2: A ready on t: the first shot comes exactly on t + R, and none before.
        [TestCase(1)]
        [TestCase(15)]
        public void FirstShot_IsTPlusR(int reaction)
        {
            P1.Trace r = P1.Run(P1.Cast(), P1.Walk, P1.Ai(reaction), P1.T + reaction + 5);
            Assert.That(r.FirstSelect(P1.KeyA, P1.KeyB), Is.EqualTo(P1.T));
            Assert.That(r.SelA[(int)P1.T - 1], Is.EqualTo(0UL));
            Assert.That(r.DueA[(int)P1.T - 1], Is.LessThanOrEqualTo(P1.T * 1000), "A is ready on t");
            NoFire(r, 1, P1.T + reaction - 1, "inside the reaction time");
            Assert.That(r.FireA[(int)(P1.T + reaction)], Is.True, "first shot on t + R");
            Assert.That(r.TargetA[(int)(P1.T + reaction)], Is.EqualTo(r.Id(P1.KeyB)));
        }

        // 3: A fires once at C (on s; 3 -> 2 rounds, ready again on s + 30), C then walks out of sight, and B steps out of
        // the grass and is first picked on t = s + 25, with 5 ticks of cooldown left. R = 15: the shot at B comes on t + 15
        // (not t + 5, not t + 5 + 15). R = 1: on t + 5, when the cooldown ends. C walks from cell (0, 4) to (3, 4) (19.9 m,
        // just inside the 20 m sight) from tick 20 and back to (0, 4) from s + 1, out of sight that same tick; B starts in
        // the grass at cell (20, 25) (14.7 m: concealed) and walks to (20, 19) from the tick in the table (43 ticks to leave it).
        [TestCase(15, 46L, 49L, 64L)]
        [TestCase(1, 32L, 49L, 50L)]
        public void Cooldown_ReactionAndCooldownRunTogether(int reaction, long bMove, long cSeen, long s)
        {
            var script = new[]
            {
                new P1.Move(20, 3, A2.Cell(3, 4)), new P1.Move(bMove, 2, A2.Cell(20, 19)), new P1.Move(s + 1, 3, A2.Cell(0, 4)),
            };
            long t = s + 25;
            P1.Trace r = P1.Run(P1.Cast(A2.Cell(0, 4), A2.Cell(20, 25)), script, P1.Ai(reaction), t + 40);
            Assert.That(r.FirstSelect(P1.KeyA, P1.KeyC), Is.EqualTo(cSeen));
            Assert.That(r.FirstFire(P1.KeyA), Is.EqualTo(s), "the shot at C");
            Assert.That(r.TargetA[(int)s], Is.EqualTo(r.Id(P1.KeyC)));
            Assert.That(r.AmmoA[(int)s], Is.EqualTo(2));
            Assert.That(r.DueA[(int)s], Is.EqualTo((s + 30) * 1000));
            for (long k = cSeen; k <= s; k++) Assert.That(r.SelA[(int)k], Is.EqualTo(r.Id(P1.KeyC)), "C selected, tick " + k);
            for (long k = s + 1; k < t; k++) Assert.That(r.SelA[(int)k], Is.EqualTo(0UL), "C out of sight, B hidden, tick " + k);
            Assert.That(r.SelA[(int)t], Is.EqualTo(r.Id(P1.KeyB)), "B first picked on t = s + 25");
            Assert.That(r.DueA[(int)t], Is.EqualTo((t + 5) * 1000), "5 ticks of cooldown left on t");
            long expected = reaction == 15 ? t + 15 : t + 5;
            NoFire(r, s + 1, expected - 1, "between the shots");
            Assert.That(r.FireA[(int)expected], Is.True, "the shot at B");
            Assert.That(r.TargetA[(int)expected], Is.EqualTo(r.Id(P1.KeyB)));
        }

        // 4(i): B is picked on t, gone on t + 5 = 45 (it turns back on 43), picked again on u = 74 (> t + 6): the clock starts
        // again, the shot comes on u + 15; on the first tick without a target A has no entry.
        [Test]
        public void LostAndFound_StartsAgain()
        {
            var script = new[] { new P1.Move(20, 2, A2.Cell(8, 10)), new P1.Move(43, 2, A2.Cell(0, 10)), new P1.Move(60, 2, A2.Cell(8, 10)) };
            const long Gone = P1.T + 5, U = 74;
            Assert.That(U, Is.GreaterThan(P1.T + 6));
            var counts = new List<int>();
            P1.Trace r = P1.Run(P1.Cast(), script, P1.Ai(15), U + 20);
            Assert.That(r.FirstSelect(P1.KeyA, P1.KeyB), Is.EqualTo(P1.T));
            for (long k = P1.T; k < Gone; k++) Assert.That(r.SelA[(int)k], Is.EqualTo(r.Id(P1.KeyB)));
            for (long k = Gone; k < U; k++) Assert.That(r.SelA[(int)k], Is.EqualTo(0UL), "nothing selectable, tick " + k);
            Assert.That(r.SelA[(int)U], Is.EqualTo(r.Id(P1.KeyB)));
            NoFire(r, 1, U + 14, "before u + 15");
            Assert.That(r.FireA[(int)(U + 15)], Is.True);
        }

        // 4(i): the entry is cleared by Sense on the first tick nothing is selectable (before any Step could prune it).
        [Test]
        public void LostTarget_ClearsTheEntryAtOnce()
        {
            var script = new[] { new P1.Move(20, 2, A2.Cell(8, 10)), new P1.Move(43, 2, A2.Cell(0, 10)) };
            M6World w = P1.World(P1.Cast(), P1.Ai(15));
            for (long tick = 1; tick <= P1.T + 5; tick++)
            {
                w.Sim.Step(P1.Drive(w, tick, script));
                if (tick == P1.T + 4) Assert.That(P1.Count(w), Is.EqualTo(1), "A's entry while B is picked");
            }
            Assert.That(P1.Selected(w, P1.KeyA), Is.EqualTo(0UL));
            Assert.That(P1.Count(w), Is.EqualTo(0), "end of tick t + 5");
        }

        // 4(ii): A picks B on t; C leaves the grass on t' = 52 and, nearer, is picked: the shot goes to C on t' + 15, nothing
        // is fired at B before.
        [Test]
        public void NewTarget_StartsAgain()
        {
            var script = new[] { new P1.Move(9, 3, A2.Cell(20, 19)), new P1.Move(20, 2, A2.Cell(8, 10)) };
            const long T2 = 52;
            P1.Trace r = P1.Run(P1.Cast(A2.Cell(20, 25)), script, P1.Ai(15), T2 + 20);
            Assert.That(r.FirstSelect(P1.KeyA, P1.KeyB), Is.EqualTo(P1.T));
            for (long k = P1.T; k < T2; k++) Assert.That(r.SelA[(int)k], Is.EqualTo(r.Id(P1.KeyB)), "B picked, tick " + k);
            Assert.That(r.SelA[(int)T2], Is.EqualTo(r.Id(P1.KeyC)));
            Assert.That(T2, Is.LessThan(P1.T + 15), "C comes inside B's reaction time");
            NoFire(r, 1, T2 + 14, "before t' + 15");
            Assert.That(r.FireA[(int)(T2 + 15)], Is.True);
            Assert.That(r.TargetA[(int)(T2 + 15)], Is.EqualTo(r.Id(P1.KeyC)));
        }

        // 4(iii): a test system submits an aimed intent for A on t + 2 .. t + 5. A fires the intent on t + 2 only (ready
        // again on t + 32); the ticks with an intent break the clock, so the automatic first shot comes on t + 6 + 40.
        [Test]
        public void Intent_BreaksTheClock()
        {
            var probe = new P1.IntentProbe(P1.T + 2, P1.T + 5, A2.Cell(22, 0));
            P1.Trace r = P1.Run(P1.Cast(), P1.Walk, P1.Ai(40), P1.T + 50, false, probe);
            Assert.That(P1.Systems(r.W), Does.Contain("test.p1-intent|0|25;"));
            Assert.That(r.FirstSelect(P1.KeyA, P1.KeyB), Is.EqualTo(P1.T));
            NoFire(r, 1, P1.T + 1, "before the intent");
            Assert.That(r.FireA[(int)P1.T + 2], Is.True, "the intent's shot");
            Assert.That(r.TargetA[(int)P1.T + 2], Is.EqualTo(0UL), "aimed at the ground");
            Assert.That(r.DueA[(int)P1.T + 2], Is.EqualTo((P1.T + 32) * 1000));
            NoFire(r, P1.T + 3, P1.T + 45, "after the intent, inside the new reaction time");
            Assert.That(r.FireA[(int)P1.T + 46], Is.True, "automatic first shot on t + 6 + R");
            Assert.That(r.TargetA[(int)P1.T + 46], Is.EqualTo(r.Id(P1.KeyB)));
            Assert.That(P1.T + 46, Is.GreaterThan(P1.T + 32), "decided by the reaction time, not the cooldown");
        }

        // 4(iv): continuity without the state system's Step. A ReactionFire made here (R = 15, not registered) is driven by
        // hand on A while B is selectable: Sense on k, none on k + 1, then on every tick k + 2 .. k + 17 Sense followed by TryAim;
        // TryAim passes up to k + 16 and hands over to the built-in decision on k + 17 (the clock started on k + 2, not k).
        [Test]
        public void Continuity_WithoutStep()
        {
            M6World w = P1.World(P1.Cast(), null);
            for (long tick = 1; tick <= 45; tick++) w.Sim.Step(P1.Drive(w, tick, P1.Walk));
            Assert.That(P1.Selected(w, P1.KeyA), Is.EqualTo(P1.Id(w, P1.KeyB).Value));
            var fire = new WarCore.Battle.ReactionFire(w.Combat, w.Combat.BuiltInAutoFire, 15);
            EntityId a = w.Unit(P1.KeyA);
            Combatant unit = w.Read<Combatant>(P1.KeyA);
            Position at = w.Read<Position>(P1.KeyA);
            WeaponDef weapon = w.Combat.Pack.Weapons[unit.Weapon];
            const long K = 1000;
            fire.Sense(w.Combat, a, in unit, in at, K);
            SimVec3 d = default;
            for (long tick = K + 2; tick <= K + 17; tick++)
            {
                fire.Sense(w.Combat, a, in unit, in at, tick);
                AutoFireAction action = fire.TryAim(w.Combat, a, in unit, in at, in weapon, tick, out SimVec3 direction);
                if (tick < K + 17) Assert.That(action, Is.EqualTo(AutoFireAction.Pass), "tick k + " + (tick - K));
                else { Assert.That(action, Is.EqualTo(AutoFireAction.Fire), "k + 17"); d = direction; }
            }
            Assert.That(d.LengthSquared.Sign, Is.Not.EqualTo(0));
        }
    }
}
