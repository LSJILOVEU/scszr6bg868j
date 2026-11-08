using NUnit.Framework;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // Acceptance 2-5 and 11: the first shot on the first sample tick, two shooters staggered, what happens between samples
    // (the kept target, a destroyed target, a dead target, a nearer target), the selector query count, the long bound.
    // Sample ticks are asserted twice: against the fixture's formula and as literals measured on this fixture.
    public sealed class P2TimingTests
    {
        private const int K = 10;
        // A's stable id gives phase 4 under K = 10: its sample ticks are 46, 56, 66, ... (A first picks B on P1.T = 40).
        internal const long PhaseA = 4, TsA = 46;

        private static void NoFire(P2.Shots s, long from, long to, string what)
        {
            for (long t = from; t <= to; t++) Assert.That(s.Fire[(int)t], Is.False, what + ", tick " + t);
        }
        private static void Selected(P2.Shots s, ulong target, long from, long to, string what)
        {
            for (long t = from; t <= to; t++) Assert.That(s.Sel[(int)t], Is.EqualTo(target), what + ", tick " + t);
        }

        // 2: B is first selectable on t = 40 and A is ready; the first shot comes on ts + R, where ts is A's first sample
        // tick from t (46), none before. With R = 15 the shot falls on 61, which is not a sample tick: the kept target fires.
        [TestCase(0)]
        [TestCase(15)]
        public void FirstShot_OnTheFirstSampleTickPlusR(int reaction)
        {
            P2.Trace r = P2.Run(P2.Cast(), P1.Walk, P2.Ai(reaction, K), 120);
            ulong b = r.Id(P2.KeyB);
            Assert.That(r.A.FirstSelect(b), Is.EqualTo(P1.T));
            Assert.That(r.A.Sel[(int)P1.T - 1], Is.EqualTo(0UL), "nothing selectable on t - 1");
            Assert.That(r.A.Due[(int)P1.T - 1], Is.LessThanOrEqualTo(P1.T * 1000), "A is ready on t");
            Assert.That(r.A.Ammo[(int)P1.T - 1], Is.EqualTo(3));
            Assert.That(P2.Phase(r.Id(P2.KeyA), K), Is.EqualTo(PhaseA));
            long ts = P2.FirstSample(r.Id(P2.KeyA), P1.T, K);
            Assert.That(ts, Is.EqualTo(TsA));
            Assert.That(ts - P1.T, Is.InRange(0, K - 1));
            long first = ts + reaction;
            NoFire(r.A, 1, first - 1, "before ts + R");
            Assert.That(r.A.Fire[(int)first], Is.True, "the first shot on ts + R");
            Assert.That(r.A.Target[(int)first], Is.EqualTo(b));
            if (reaction == 15) Assert.That(P2.Samples(r.Id(P2.KeyA), first, K), Is.False, "61 is not a sample tick of A");
        }

        // 3: A2 (same team, 3 m north of A, off A's line to B) first picks B on 42; under K = 10 the phases differ (4 and 6),
        // so the two first shots fall on different sample ticks: A on 46, A2 on 44 (R = 0, both ready).
        [Test]
        public void TwoShooters_AreStaggered()
        {
            const long TA2 = 42, FireA = TsA, FireA2 = 44;
            P2.Trace r = P2.Run(P2.Cast(P2.A2Aside), P1.Walk, P2.Ai(0, K), 120);
            ulong a = r.Id(P2.KeyA), a2 = r.Id(P2.KeyA2), b = r.Id(P2.KeyB);
            Assert.That(P2.Phase(a, K), Is.EqualTo(PhaseA));
            Assert.That(P2.Phase(a2, K), Is.EqualTo(6L));
            Assert.That(P2.Phase(a, K), Is.Not.EqualTo(P2.Phase(a2, K)), "the phases differ");
            Assert.That(r.A.FirstSelect(b), Is.EqualTo(P1.T));
            Assert.That(r.A2.FirstSelect(b), Is.EqualTo(TA2));
            Assert.That(r.A.Due[(int)P1.T - 1], Is.LessThanOrEqualTo(P1.T * 1000), "A ready");
            Assert.That(r.A2.Due[(int)TA2 - 1], Is.LessThanOrEqualTo(TA2 * 1000), "A2 ready");
            Assert.That(P2.FirstSample(a, P1.T, K), Is.EqualTo(FireA));
            Assert.That(P2.FirstSample(a2, TA2, K), Is.EqualTo(FireA2));
            Assert.That(r.A.FirstFire(), Is.EqualTo(FireA));
            Assert.That(r.A2.FirstFire(), Is.EqualTo(FireA2));
            Assert.That(FireA, Is.Not.EqualTo(FireA2));
        }

        // 4(i): K = 40, R = 0. A samples on 26, 66, 106, ...; B is selectable from 40, A fires on its sample tick 66 and B
        // turns back on 55, so B is unselectable from g = 69 (not a sample tick). A keeps B: the next ready tick x = 96
        // (DueMilli 96000 on g, 2 rounds left) fires at B although the selector picks nothing; the entry lives until the
        // next sample tick ns = 106 clears it, and nothing is fired afterwards (B stops 22 m away, never picked again).
        [Test]
        public void KeptTarget_FiresBetweenSamples()
        {
            const int K40 = 40;
            const long G = 69, Ns = 106, X = 96, FirstSample = 66;
            const int AmmoG = 2; const long DueG = 96000;
            var script = new[] { new P1.Move(20, 2, A2.Cell(8, 10)), new P1.Move(55, 2, A2.Cell(0, 10)) };
            P2.Trace r = P2.Run(P2.Cast(), script, P2.Ai(0, K40), 200);
            ulong a = r.Id(P2.KeyA), b = r.Id(P2.KeyB);
            Assert.That(r.A.FirstSelect(b), Is.EqualTo(P1.T));
            Selected(r.A, b, P1.T, G - 1, "B selectable");
            Assert.That(r.A.Sel[(int)G], Is.EqualTo(0UL), "B gone on g");
            Assert.That(P2.FirstSample(a, P1.T, K40), Is.EqualTo(FirstSample));
            Assert.That(r.A.FirstFire(), Is.EqualTo(FirstSample), "the first shot on A's first sample tick");
            Assert.That(P2.Samples(a, G, K40), Is.False, "g is not a sample tick");
            Assert.That(G - FirstSample, Is.InRange(1, 9));
            long ns = P2.FirstSample(a, G + 1, K40);
            Assert.That(ns, Is.EqualTo(Ns));
            Assert.That(ns - G, Is.GreaterThanOrEqualTo(31));
            Assert.That(r.A.Ammo[(int)G], Is.EqualTo(AmmoG));
            Assert.That(r.A.Due[(int)G], Is.EqualTo(DueG));
            long x = (DueG + 999) / 1000;
            Assert.That(x, Is.EqualTo(X));
            Assert.That(x, Is.GreaterThan(G).And.LessThan(ns));
            NoFire(r.A, FirstSample + 1, x - 1, "between the sample shot and x");
            Assert.That(r.A.Fire[(int)x], Is.True, "the kept target is fired at on x");
            Assert.That(r.A.Target[(int)x], Is.EqualTo(b));
            Assert.That(r.A.Sel[(int)x], Is.EqualTo(0UL), "the selector picks nothing on x");
            Assert.That(r.Counts[(int)x], Is.EqualTo(1), "the entry lives at the end of x");
            Assert.That(r.Counts[(int)ns - 1], Is.EqualTo(1));
            Assert.That(r.Counts[(int)ns], Is.EqualTo(0), "the sample tick clears it");
            Selected(r.A, 0, G, 200, "B is never picked again");
            NoFire(r.A, x + 1, 200, "after x");
        }

        // 4(ii): K = 10, R = 0. B is destroyed (a Requests system, Order 25) on d = 48, after the first shot (46) and not on a
        // sample tick: the entry held at the end of 47 is gone at the end of 48 and A fires nothing up to the next sample (56)
        // nor after (nothing is left to pick).
        [Test]
        public void DestroyedTarget_ClearsTheEntry()
        {
            const long D = 48;
            P2.Trace r = P2.Run(P2.Cast(), P1.Walk, P2.Ai(0, K), 80, false, new P1.DestroyProbe(D, P2.KeyB));
            ulong a = r.Id(P2.KeyA);
            Assert.That(P2.Systems(r.W), Does.Contain("test.p1-destroy|0|25;"));
            Assert.That(r.A.FirstFire(), Is.EqualTo(TsA));
            Assert.That(P2.Samples(a, D, K), Is.False, "d is not a sample tick");
            Assert.That(D, Is.GreaterThan(TsA));
            Assert.That(r.Counts[(int)D - 1], Is.EqualTo(1));
            Assert.That(r.Counts[(int)D], Is.EqualTo(0));
            Assert.That(P2.FirstSample(a, D, K), Is.EqualTo(56L));
            NoFire(r.A, D, 80, "from d on");
        }

        // 4(iii): K = 10, R = 0. A Queries system (Order 25, before combat's 30) deals B a lethal blow on d - 1 = 47; the
        // death settles after that phase, so Sense on 47 still keeps B (entry at the end of 47) and on d = 48 B resolves but
        // is dead: the IsDead branch clears the entry (nothing was destroyed), and A fires nothing from d on.
        [Test]
        public void DeadTarget_ClearsTheEntry()
        {
            const long D = 48;
            M6World w = P2.World(P2.Cast(), P2.Ai(0, K), false, new P2.KillProbe(D - 1, P2.KeyB));
            Assert.That(P2.Systems(w), Does.Contain("test.p2-kill|3|25;"));
            ulong a = P2.Id(w, P2.KeyA).Value;
            Assert.That(P2.Samples(a, D, K), Is.False, "d is not a sample tick");
            long first = -1;
            for (long tick = 1; tick <= 80; tick++)
            {
                w.Sim.Step(P2.Drive(w, tick, P1.Walk));
                if (first < 0 && P1.Fired(w, P2.KeyA)) first = tick;
                if (tick == D - 2) Assert.That(w.Dead(P2.KeyB), Is.False, "B alive before the blow");
                if (tick == D - 1) Assert.That(P2.Count(w), Is.EqualTo(1), "the entry at the end of d - 1");
                if (tick == D)
                {
                    Assert.That(P2.Present(w, P2.KeyB), Is.True, "B still resolves");
                    Assert.That(w.Dead(P2.KeyB), Is.True, "B is dead");
                    Assert.That(P2.Count(w), Is.EqualTo(0), "the entry is gone at the end of d");
                }
                if (tick >= D) Assert.That(P1.Fired(w, P2.KeyA), Is.False, "no shot from d on, tick " + tick);
            }
            Assert.That(first, Is.EqualTo(TsA));
        }

        // 4(iv-a): K = 10, R = 15. A fires at B on 61 and 91 (ready again on 121). C leaves the grass and, nearer, is picked
        // on c = 89 (not a sample tick); the next sample tick is ns = 96. The weapon is ready on y = 91 inside (c, ns): the shot
        // goes to the kept target B while the selector already picks C. From ns the clock runs for C, but the cooldown ends
        // later than ns + 15, so the first shot at C comes on 121 = DueMilli(91) / 1000, nothing between.
        [Test]
        public void NearerTarget_WindowShotGoesToTheKeptOne()
        {
            const long C = 89, Ns = 96, Y = 91, FirstC = 121;
            const int AmmoC = 2; const long DueC = 91000;
            var script = new[] { new P1.Move(20, 2, A2.Cell(8, 10)), new P1.Move(46, 3, A2.Cell(20, 19)) };
            P2.Trace r = P2.Run(P2.Cast(null, null, A2.Cell(20, 25)), script, P2.Ai(15, K), 160);
            ulong a = r.Id(P2.KeyA), b = r.Id(P2.KeyB), c = r.Id(P2.KeyC);
            Assert.That(r.A.FirstSelect(b), Is.EqualTo(P1.T));
            Assert.That(r.A.FirstSelect(c), Is.EqualTo(C));
            Assert.That(P2.Samples(a, C, K), Is.False, "c is not a sample tick");
            long ns = P2.FirstSample(a, C + 1, K);
            Assert.That(ns, Is.EqualTo(Ns));
            Assert.That(C, Is.InRange(ns - 9, ns - 1));
            Assert.That(r.A.Ammo[(int)C], Is.EqualTo(AmmoC));
            Assert.That(r.A.Due[(int)C], Is.EqualTo(DueC));
            long y = (DueC + 999) / 1000;
            Assert.That(y, Is.EqualTo(Y));
            Assert.That(y, Is.InRange(C, ns - 1));
            Assert.That(r.A.Fire[(int)y], Is.True, "the window shot");
            Assert.That(r.A.Target[(int)y], Is.EqualTo(b), "at the kept target");
            Assert.That(r.A.Sel[(int)y], Is.EqualTo(c), "while the selector picks C");
            Assert.That(r.A.Due[(int)y] / 1000, Is.EqualTo(FirstC));
            Assert.That(FirstC, Is.GreaterThan(ns + 15), "decided by the cooldown, not the reaction time");
            NoFire(r.A, y + 1, FirstC - 1, "between the window shot and the first shot at C");
            Assert.That(r.A.Fire[(int)FirstC], Is.True);
            Assert.That(r.A.Target[(int)FirstC], Is.EqualTo(c));
        }

        // 4(iv-b): K = 10, R = 15. C is picked on c = 99 (not a sample tick), ns = 106. A's last shot at B is y = 91, inside
        // [ns - 30, ns - 15] with a round left (ready on 121 = y + 30): no shot in (y, ns), the selector picks C on 99..105
        // and the clock starts again on ns, so the first shot at C comes exactly on ns + 15 = 121, none on ns .. ns + 14.
        [Test]
        public void NewTarget_StartsTheClockAgainOnTheSampleTick()
        {
            const long C = 99, Ns = 106, Y = 91;
            var script = new[] { new P1.Move(20, 2, A2.Cell(8, 10)), new P1.Move(56, 3, A2.Cell(20, 19)) };
            P2.Trace r = P2.Run(P2.Cast(null, null, A2.Cell(20, 25)), script, P2.Ai(15, K), 160);
            ulong a = r.Id(P2.KeyA), b = r.Id(P2.KeyB), c = r.Id(P2.KeyC);
            Assert.That(r.A.FirstSelect(c), Is.EqualTo(C));
            Assert.That(P2.Samples(a, C, K), Is.False, "c is not a sample tick");
            long ns = P2.FirstSample(a, C + 1, K);
            Assert.That(ns, Is.EqualTo(Ns));
            Assert.That(C, Is.InRange(ns - 9, ns - 1));
            Assert.That(r.A.LastFire(ns), Is.EqualTo(Y), "the last shot at B before ns");
            Assert.That(r.A.Target[(int)Y], Is.EqualTo(b));
            Assert.That(Y, Is.InRange(ns - 30, ns - 15));
            Assert.That(r.A.Ammo[(int)Y], Is.EqualTo(1), "not the last round of the magazine");
            Assert.That(r.A.Due[(int)ns - 1], Is.EqualTo((Y + 30) * 1000));
            Selected(r.A, c, C, ns - 1, "C picked before ns");
            NoFire(r.A, Y + 1, ns + 14, "after y up to ns + 14");
            Assert.That(r.A.Fire[(int)(ns + 15)], Is.True, "the first shot at C on ns + R");
            Assert.That(r.A.Target[(int)(ns + 15)], Is.EqualTo(c));
        }

        // 5: the selector queries inside the combat step (QueriesThisStep; A is the only armed unit, B selectable from 40):
        // K = 10, R = 0 asks once on A's sample ticks and never otherwise; K = 1, R = 15 asks once on every tick (P1 asked
        // twice on the ticks TryAim handed over).
        [Test]
        public void SelectorQueries_OncePerSampleTick()
        {
            P2.Trace r = P2.Run(P2.Cast(), P1.Walk, P2.Ai(0, K), 100);
            ulong a = r.Id(P2.KeyA);
            for (long t = 41; t <= 100; t++)
            {
                Assert.That(r.A.Sel[(int)t], Is.Not.EqualTo(0UL), "B selectable, tick " + t);
                Assert.That(r.Queries[(int)t], Is.EqualTo(P2.Samples(a, t, K) ? 1 : 0), "queries, tick " + t);
            }
            Assert.That(r.Queries[(int)TsA], Is.EqualTo(1));
            Assert.That(r.Queries[(int)TsA + 1], Is.EqualTo(0));
            P2.Trace one = P2.Run(P2.Cast(), P1.Walk, P2.Ai(15, 1), 100);
            for (long t = 41; t <= 100; t++) Assert.That(one.Queries[(int)t], Is.EqualTo(1), "K = 1, tick " + t);
        }

        // 11: the sample test on the last legal tick. A's phase is 4 and (long.MaxValue - 1) % 10 = 6, so long.MaxValue - 1 is
        // a sample tick of A under K = 10; a ReactionFire made here (K = 10, R = 0, not registered) sees B on that tick and
        // TryAim fires. The sum (tick + phase) would overflow and read as no sample.
        [Test]
        public void LongBound_LastLegalTickSamples()
        {
            const long Tick = 9223372036854775806L;
            Assert.That(Tick, Is.EqualTo(long.MaxValue - 1));
            Assert.That(Tick % K, Is.EqualTo(6L));
            M6World w = P2.World(P2.Cast(), null);
            for (long tick = 1; tick <= 45; tick++) w.Sim.Step(P2.Drive(w, tick, P1.Walk));
            Assert.That(P1.Selected(w, P2.KeyA), Is.EqualTo(P2.Id(w, P2.KeyB).Value));
            ulong a = P2.Id(w, P2.KeyA).Value;
            Assert.That(P2.Phase(a, K), Is.EqualTo(PhaseA));
            Assert.That(P2.Samples(a, Tick, K), Is.True);
            var fire = new ReactionFire(w.Combat, w.Combat.BuiltInAutoFire, 0, K);
            EntityId shooter = w.Unit(P2.KeyA);
            Combatant unit = w.Read<Combatant>(P2.KeyA);
            Position at = w.Read<Position>(P2.KeyA);
            WeaponDef weapon = w.Combat.Pack.Weapons[unit.Weapon];
            Assert.That(P1.Count(fire), Is.EqualTo(0), "an empty fire decision");
            fire.Sense(w.Combat, shooter, in unit, in at, Tick);
            Assert.That(P1.Count(fire), Is.EqualTo(1), "the sample tick made an entry");
            Assert.That(fire.TryAim(w.Combat, shooter, in unit, in at, in weapon, Tick, out SimVec3 direction), Is.EqualTo(AutoFireAction.Fire));
            Assert.That(direction.LengthSquared.Sign, Is.Not.EqualTo(0));
        }
    }
}
