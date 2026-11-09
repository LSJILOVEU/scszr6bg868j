using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Presentation;

namespace WarCore.Tests
{
    // Acceptance 2-6 and 8: the burst rhythm on the fast rifle (A first picks B on 40, ready with a full magazine), the
    // reload inside and at the end of a burst, single-shot bursts, the pauses that change nothing, the burst under a sense
    // interval, and a new target that inherits no pause. Every vector is asserted twice: against the fixture's model and
    // as the literal measured on this fixture.
    public sealed class P3TimingTests
    {
        private const int B = 3, Q = 20;

        // 2: R = 0, B = 3, Q = 20 (then R = 15: the first shot on 40 + 15, the same rhythm after it).
        internal static readonly long[] Burst3Pause20 =
        {
            40, 45, 50, 70, 75, 80, 100, 105, 110, 130, 190, 195, 200, 220, 225, 230, 250, 255, 260, 280,
        };
        internal static readonly long[] Burst3Pause20Reaction15 =
        {
            55, 60, 65, 85, 90, 95, 115, 120, 125, 145, 205, 210, 215, 235, 240, 245, 265, 270, 275, 295,
        };

        private static void Ready(P3.Trace r)
        {
            Assert.That(r.A.FirstSelect(r.Id(P3.KeyB)), Is.EqualTo(P1.T), "A first picks B on t");
            Assert.That(r.A.Due[(int)P1.T - 1], Is.EqualTo(0L), "A is ready on t");
            Assert.That(r.A.Ammo[(int)P1.T - 1], Is.EqualTo(P3.Magazine), "a full magazine on t");
        }

        [TestCase(0)]
        [TestCase(15)]
        public void Burst3Pause20_Rhythm(int reaction)
        {
            P3.Trace r = P3.Run(P2.Cast(), P1.Walk, P3.Ai(reaction, null, B, Q), 300);
            Ready(r);
            List<long> fires = r.A.FireTicks(300);
            Assert.That(fires, Is.EqualTo(reaction == 0 ? Burst3Pause20 : Burst3Pause20Reaction15));
            Assert.That(fires, Is.EqualTo(P3.Expect(P1.T + reaction, B, Q, 300)), "the model");
            if (reaction != 0) return;
            // The pause after the third shot (50): the weapon is ready from 55 (DueMilli 55000) and the entry lives, yet
            // nothing is fired before 70 = 50 + Q.
            for (long t = 51; t <= 69; t++)
            {
                Assert.That(r.A.Due[(int)t], Is.EqualTo(55000L), "DueMilli, tick " + t);
                Assert.That(r.A.Fire[(int)t], Is.False, "no shot, tick " + t);
                Assert.That(r.Counts[(int)t], Is.EqualTo(1), "the entry lives, tick " + t);
            }
            for (long t = 55; t <= 69; t++) Assert.That(r.A.Due[(int)t], Is.LessThanOrEqualTo(t * 1000), "ready but gated, tick " + t);
            Assert.That(r.A.Fire[69], Is.False, "the last tick of the pause");
            Assert.That(r.A.Fire[70], Is.True, "the first tick after the pause");
        }

        // 3(a): the tenth round (130) is the first shot of the fourth burst and empties the magazine: the count is reset,
        // so after the reload the burst 190, 195, 200 is a full one and the pause runs to 220 (a count carried over would
        // stop after 195 and fire again on 215).
        [Test]
        public void ReloadInsideABurst_ResetsTheCount()
        {
            P3.Trace r = P3.Run(P2.Cast(), P1.Walk, P3.Ai(0, null, B, Q), 300);
            Ready(r);
            Assert.That(r.A.Ammo[129], Is.EqualTo(1), "the last round before 130");
            Assert.That(r.A.Fire[130], Is.True);
            Assert.That(r.A.Ammo[130], Is.EqualTo(P3.Magazine), "reloaded");
            Assert.That(r.A.Due[130], Is.EqualTo(190000L), "ready again on 190");
            Assert.That(r.A.Fire[190], Is.True);
            Assert.That(r.A.Fire[195], Is.True);
            Assert.That(r.A.Fire[200], Is.True, "the third shot of the burst after the reload");
            P3.NoFire(r.A, 201, 219, "the pause after 200");
            Assert.That(r.A.Fire[215], Is.False, "not the end of a carried-over pause");
            Assert.That(r.A.Fire[220], Is.True);
        }

        // 3(b): B = 5, Q = 100 (longer than the reload): the fifth shot of the second burst (180) empties the magazine, so
        // the next shot comes on 240 when the reload ends, not on 280 when a pause would end.
        internal static readonly long[] Burst5Pause100 =
        {
            40, 45, 50, 55, 60, 160, 165, 170, 175, 180, 240, 245, 250, 255, 260, 360, 365, 370, 375, 380,
        };
        [Test]
        public void LastShotOfABurstEmptiesTheMagazine_NoPauseAdded()
        {
            P3.Trace r = P3.Run(P2.Cast(), P1.Walk, P3.Ai(0, null, 5, 100), 400);
            Ready(r);
            List<long> fires = r.A.FireTicks(400);
            Assert.That(fires, Is.EqualTo(Burst5Pause100));
            Assert.That(fires, Is.EqualTo(P3.Expect(P1.T, 5, 100, 400)), "the model");
            Assert.That(r.A.Ammo[179], Is.EqualTo(1));
            Assert.That(r.A.Due[180], Is.EqualTo(240000L), "the reload decides");
            P3.NoFire(r.A, 181, 239, "the reload");
            Assert.That(r.A.Fire[240], Is.True);
        }

        // 3(c): B = 9, Q = 50, one below the magazine: the ninth shot (80) pauses to 130, the tenth (130) empties the
        // magazine and the reload runs to 190.
        internal static readonly long[] Burst9Pause50 =
        {
            40, 45, 50, 55, 60, 65, 70, 75, 80, 130, 190, 195, 200, 205, 210, 215, 220, 225, 230, 280,
        };
        [Test]
        public void BurstOneBelowTheMagazine()
        {
            P3.Trace r = P3.Run(P2.Cast(), P1.Walk, P3.Ai(0, null, 9, 50), 300);
            Ready(r);
            List<long> fires = r.A.FireTicks(300);
            Assert.That(fires, Is.EqualTo(Burst9Pause50));
            Assert.That(fires, Is.EqualTo(P3.Expect(P1.T, 9, 50, 300)), "the model");
        }

        // 4: B = 1, Q = 12: one shot every 12 ticks (longer than the 5-tick interval); the tenth (148) empties the magazine.
        internal static readonly long[] Burst1Pause12 =
        {
            40, 52, 64, 76, 88, 100, 112, 124, 136, 148, 208, 220, 232, 244, 256, 268, 280, 292,
        };
        [Test]
        public void SingleShotBursts()
        {
            P3.Trace r = P3.Run(P2.Cast(), P1.Walk, P3.Ai(0, null, 1, 12), 300);
            Ready(r);
            List<long> fires = r.A.FireTicks(300);
            Assert.That(fires, Is.EqualTo(Burst1Pause12));
            Assert.That(fires, Is.EqualTo(P3.Expect(P1.T, 1, 12, 300)), "the model");
        }

        // 5(a): Q at the interval (5) or 0 changes nothing: the continuous vector, although the state is version 3.
        // 5(c): B at the magazine (10) or above it (11): the count is reset by every reload and never reaches B.
        [TestCase(3, 5)]
        [TestCase(3, 0)]
        [TestCase(10, 50)]
        [TestCase(11, 50)]
        public void PauseWithinTheInterval_OrBurstAtTheMagazine_IsContinuous(int burst, int pause)
        {
            P3.Trace r = P3.Run(P2.Cast(), P1.Walk, P3.Ai(0, null, burst, pause), 300);
            Ready(r);
            Assert.That(r.W.System<ReactionFire>(), Is.Not.Null, "registered");
            Assert.That(r.W.System<ReactionFire>().StateVersion, Is.EqualTo(3));
            List<long> fires = r.A.FireTicks(300);
            Assert.That(fires, Is.EqualTo(P3BaselineTests.Fires0), "the continuous vector");
            Assert.That(fires, Is.EqualTo(P3.Expect(P1.T, burst, pause, 300)), "the model");
        }

        // 5(b): Q = 6, one above the interval: every third shot waits one tick longer.
        internal static readonly long[] Burst3Pause6 =
        {
            40, 45, 50, 56, 61, 66, 72, 77, 82, 88, 148, 153, 158, 164, 169, 174, 180, 185, 190, 196,
            256, 261, 266, 272, 277, 282, 288, 293, 298,
        };
        [Test]
        public void PauseOneAboveTheInterval()
        {
            P3.Trace r = P3.Run(P2.Cast(), P1.Walk, P3.Ai(0, null, B, 6), 300);
            Ready(r);
            List<long> fires = r.A.FireTicks(300);
            Assert.That(fires, Is.EqualTo(Burst3Pause6));
            Assert.That(fires, Is.EqualTo(P3.Expect(P1.T, B, 6, 300)), "the model");
        }

        // 6: with the sense interval K = 10 (A's phase 4, first sample tick 46): the burst runs over kept ticks as well.
        internal static readonly long[] Burst3Pause20Sense10 =
        {
            46, 51, 56, 76, 81, 86, 106, 111, 116, 136, 196, 201, 206, 226, 231, 236, 256, 261, 266, 286,
        };
        internal static readonly long[] Burst3Pause20Sense10Reaction15 =
        {
            61, 66, 71, 91, 96, 101, 121, 126, 131, 151, 211, 216, 221, 241, 246, 251, 271, 276, 281,
        };
        [TestCase(0)]
        [TestCase(15)]
        public void WithTheSenseInterval(int reaction)
        {
            const int K = 10;
            P3.Trace r = P3.Run(P2.Cast(), P1.Walk, P3.Ai(reaction, K, B, Q), 300);
            Ready(r);
            ulong a = r.Id(P3.KeyA);
            Assert.That(P2.Phase(a, K), Is.EqualTo(P2TimingTests.PhaseA));
            long ts = P2.FirstSample(a, P1.T, K);
            Assert.That(ts, Is.EqualTo(P2TimingTests.TsA));
            List<long> fires = r.A.FireTicks(300);
            Assert.That(fires, Is.EqualTo(reaction == 0 ? Burst3Pause20Sense10 : Burst3Pause20Sense10Reaction15));
            Assert.That(fires, Is.EqualTo(P3.Expect(ts + reaction, B, Q, 300)), "the model");
            int kept = 0;
            foreach (long t in fires) if (!P2.Samples(a, t, K)) kept++;
            Assert.That(kept, Is.GreaterThan(0), "shots on kept ticks");
        }

        // 8: B = 3, Q = 40. A fires at B on 40, 45, 50 and pauses to 90; B is destroyed on 60 and C, walking in from 35,
        // is first selectable on c = 64, inside the pause. The new entry starts at zero: A fires at C on c, 69 and 74,
        // then pauses to 114. A pause kept across entries would hold the fire until 90.
        [Test]
        public void NewTarget_InheritsNoPause()
        {
            const long Destroy = 60, C = 64;
            var script = new[] { new P1.Move(20, 2, A2.Cell(8, 10)), new P1.Move(35, 3, A2.Cell(8, 4)) };
            P3.Trace r = P3.Run(P2.Cast(), script, P3.Ai(0, null, B, 40), 200, false, new P1.DestroyProbe(Destroy, P3.KeyB));
            Assert.That(P3.Systems(r.W), Does.Contain("test.p1-destroy|0|25;"));
            Assert.That(r.A.Sel[(int)P1.T - 1], Is.EqualTo(0UL), "nothing selectable on t - 1");
            Assert.That(r.A.Sel[(int)P1.T], Is.Not.EqualTo(0UL), "B selectable on t (B's id cannot be read after 60)");
            Assert.That(r.A.Due[(int)P1.T - 1], Is.EqualTo(0L), "A is ready on t");
            Assert.That(r.A.Ammo[(int)P1.T - 1], Is.EqualTo(P3.Magazine));
            ulong c = r.Id(P3.KeyC);
            Assert.That(r.A.Sel[(int)P1.T], Is.Not.EqualTo(c), "the first target is not C");
            Assert.That(r.A.FireTicks(50), Is.EqualTo(new long[] { 40, 45, 50 }), "the burst at B");
            Assert.That(P2.Present(r.W, P3.KeyB), Is.False, "B is gone");
            Assert.That(r.Counts[(int)Destroy], Is.EqualTo(0), "the entry is gone at the end of 60");
            Assert.That(r.A.Sel[(int)Destroy], Is.EqualTo(0UL), "nothing selectable on 60");
            long first = r.A.FirstSelect(c);
            Assert.That(first, Is.EqualTo(C));
            Assert.That(first, Is.GreaterThan(Destroy).And.LessThan(50 + 40), "c falls inside the pause after 50");
            P3.NoFire(r.A, 51, first - 1, "before c");
            Assert.That(r.A.Fire[(int)first], Is.True, "the first shot at C on c");
            Assert.That(r.A.Target[(int)first], Is.EqualTo(c));
            Assert.That(r.A.FireTicks(120), Is.EqualTo(new long[] { 40, 45, 50, first, first + 5, first + 10, first + 10 + 40, first + 15 + 40 }), "the next burst from 114");
            P3.NoFire(r.A, first + 11, first + 10 + 39, "the pause after the burst at C");
        }
    }
}
