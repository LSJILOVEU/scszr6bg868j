using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // Acceptance 7 and 7b: a held shot (an ally on the line) counts no round, and the pause draws no random number.
    public sealed class P3AllyTests
    {
        private const int B = 3, Q = 20;

        // 7: A2 straight behind A (P2.A2Behind); R = 0, B = 3, Q = 20, run to 61. A fired 40, 45, 50 and pauses; A2 picks B
        // on 60, is held (DueMilli 90000) and has no shot. The payload is 108 bytes: R, the count, two entries in stable-id
        // order, K (1, written in version 3), B, Q and the two burst records (3, 50) and (0, 0).
        [Test]
        public void HeldShot_CountsNoRound_AndThePayload()
        {
            P3.Trace r = P3.Run(P2.Cast(P2.A2Behind), P1.Walk, P3.Ai(0, null, B, Q), 61);
            ulong a = r.Id(P3.KeyA), a2 = r.Id(P3.KeyA2), b = r.Id(P3.KeyB);
            Assert.That(a, Is.LessThan(a2), "A's stable id sorts first");
            Assert.That(r.A.FireTicks(61), Is.EqualTo(new long[] { 40, 45, 50 }));
            Assert.That(r.A2.FirstSelect(b), Is.EqualTo(60L));
            Assert.That(r.A2.FireTicks(61), Is.Empty, "A2 is held");
            Assert.That(r.A2.Due[60], Is.EqualTo(60 * 1000 + P3.AllyHoldMilli), "the hold on 60");
            Assert.That(r.Counts[61], Is.EqualTo(2));
            ReactionFire fire = r.W.System<ReactionFire>();
            Assert.That(fire.StateVersion, Is.EqualTo(3));
            byte[] bytes = P3.Payload(fire);
            Assert.That(bytes.Length, Is.EqualTo(108), "R 4 + count 4 + 2 x 32 + K 4 + B 4 + Q 4 + 2 x (4 + 8)");
            P3.Saved s = P3.Parse(bytes);
            Assert.That(s.R, Is.EqualTo(0));
            Assert.That(s.Count, Is.EqualTo(2));
            Assert.That(s.Entries, Is.EqualTo(new[] { (a, b, 40L, 61L), (a2, b, 60L, 61L) }));
            Assert.That(s.RemainingBeforeK, Is.EqualTo(36));
            Assert.That((s.K, s.B, s.Q), Is.EqualTo((1, B, Q)));
            Assert.That(s.RemainingBeforeBursts, Is.EqualTo(24));
            Assert.That(s.Bursts, Is.EqualTo(new[] { (3, 50L), (0, 0L) }), "A paused after 50; A2's held shot counts nothing");
            Assert.That(s.RemainingAtEnd, Is.EqualTo(0));

            var expected = new StateHash();
            expected.Add("battle.ai-fire"); expected.Add(3); expected.Add(0); expected.Add(2);
            expected.Add(a); expected.Add(b); expected.Add(40L); expected.Add(61L);
            expected.Add(a2); expected.Add(b); expected.Add(60L); expected.Add(61L);
            expected.Add(1); expected.Add(B); expected.Add(Q);
            expected.Add(3); expected.Add(50L);
            expected.Add(0); expected.Add(0L);
            var actual = new StateHash();
            fire.WriteState(actual);
            Assert.That(actual.Value, Is.EqualTo(expected.Value), "WriteState by hand");
        }

        // 7b: a fire decision made here (R = 0, K = 1, B = 3, Q = 20) on the world after tick 40 (B selectable, A2 aside),
        // driven by hand on A from 40 to 63 without stepping. 40..42 fire (the hand drive checks no cooldown); A2 is moved
        // onto A's line before 50; 43..61 pass without drawing a random number (the random stream's hash is unchanged by the
        // call) and the record stays (3, 42); on 62 the pause is over, the count is cleared, the aim is held by the ally
        // check (a number is drawn) and nothing is counted: (0, 42); A2 is moved away before 63 and 63 fires: (1, 63).
        [Test]
        public void PauseDrawsNoNumber_HeldShotAtTheExpiryCountsNothing()
        {
            M6World w = P3.World(P2.Cast(P2.A2Aside), null);
            for (long tick = 1; tick <= 40; tick++) w.Sim.Step(P2.Drive(w, tick, P1.Walk));
            Assert.That(P1.Selected(w, P3.KeyA), Is.EqualTo(P2.Id(w, P3.KeyB).Value), "B selectable on 40");
            Assert.That(w.System<ReactionFire>(), Is.Null, "nothing registered: the decision under test is made here");
            var fire = new ReactionFire(w.Combat, w.Combat.BuiltInAutoFire, 0, 1, B, Q);
            EntityId a = w.Unit(P3.KeyA);
            Combatant unit = w.Read<Combatant>(P3.KeyA);
            Position at = w.Read<Position>(P3.KeyA);
            WeaponDef weapon = w.Combat.Pack.Weapons[unit.Weapon];
            SimVec3 onTheLine = A2.Cell(20, 10);
            var actions = new List<AutoFireAction>();
            for (long t = 40; t <= 63; t++)
            {
                if (t == 50) Place(w, onTheLine);
                if (t == 63) Place(w, P2.A2Aside);
                fire.Sense(w.Combat, a, in unit, in at, t);
                ulong before = P3.Rng(w);
                AutoFireAction action = fire.TryAim(w.Combat, a, in unit, in at, in weapon, t, out _);
                bool drew = P3.Rng(w) != before;
                actions.Add(action);
                if (t <= 42) Assert.That(action, Is.EqualTo(AutoFireAction.Fire), "tick " + t);
                if (t == 42) Assert.That(P3.Burst(fire), Is.EqualTo((3, 42L)), "after the third shot");
                if (t >= 43 && t <= 61)
                {
                    Assert.That(action, Is.EqualTo(AutoFireAction.Pass), "the pause, tick " + t);
                    Assert.That(drew, Is.False, "no random number in the pause, tick " + t);
                    Assert.That(P3.Burst(fire), Is.EqualTo((3, 42L)), "the record in the pause, tick " + t);
                }
                if (t == 62)
                {
                    Assert.That(action, Is.EqualTo(AutoFireAction.HoldFire), "held at the expiry");
                    Assert.That(drew, Is.True, "the ally check drew a number");
                    Assert.That(P3.Burst(fire), Is.EqualTo((0, 42L)), "cleared first, then held, nothing counted");
                }
                if (t == 63)
                {
                    Assert.That(action, Is.EqualTo(AutoFireAction.Fire));
                    Assert.That(P3.Burst(fire), Is.EqualTo((1, 63L)), "the first shot of the next burst");
                }
            }
            Assert.That(actions.Count, Is.EqualTo(24));
            // The positive control: AimAt itself, with A2 on the line, is held and draws a number.
            Place(w, onTheLine);
            ulong b0 = P3.Rng(w);
            var builtIn = (DefaultAutoFire)w.Combat.BuiltInAutoFire;
            Assert.That(builtIn.AimAt(w.Combat, a, in at, w.Unit(P3.KeyB), out _), Is.EqualTo(AutoFireAction.HoldFire));
            Assert.That(P3.Rng(w), Is.Not.EqualTo(b0), "the hash sees the roll");
        }

        private static void Place(M6World w, SimVec3 at)
        {
            w.Set(P3.KeyA2, new Position(at));
            w.Sim.CompleteSpatialBoundary();
        }
    }
}
