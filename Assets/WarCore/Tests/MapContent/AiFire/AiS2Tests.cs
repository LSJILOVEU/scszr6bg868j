using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Content;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // AI-S2: each AI shooter's own reaction time, burst length and pause (ai.traitSpreadPermille, ReactionFire version 4).
    public sealed class AiS2Tests
    {
        private static AiRules Rules(string ai) => A3.Load(P1.PackJson(ai)).Section<AiRules>();
        private const string Prefix = "content pack 'a2-test' layer 'a2-test' ai field '";

        // ---- the per-shooter values ----

        // Three ids by hand, team values R 15, B 3, Q 30 (the playtest pack's), s = 300, so d = p * 601 / 1000 - 300:
        // key 1: p 774 / 183 / 722, d 165 / -191 / 133: R 15 * 1165 / 1000 = 17, B 3 * 809 / 1000 = 2, Q 30 * 1133 / 1000 = 33;
        // key 4: p 838 / 704 / 733, d 203 / 123 / 140: R 18, B 3, Q 34;
        // key 101: p 278 / 641 / 295, d -133 / 85 / -123: R 13, B 3, Q 26.
        [TestCase(1UL, 17, 2, 33)]
        [TestCase(4UL, 18, 3, 34)]
        [TestCase(101UL, 13, 3, 26)]
        public void Traits_ThreeKeysByHand(ulong key, int reaction, int burst, int pause)
        {
            ReactionFire fire = S2.Fire(15, 3, 30, 300);
            StableId id = S2.Placed(key);
            Assert.That((fire.ReactionOf(id), fire.BurstOf(id), fire.PauseOf(id)), Is.EqualTo((reaction, burst, pause)));
        }

        // d itself, seen through a team value of 1000 (value = 1000 + d): the three keys' reaction d of 165, 203, -133.
        [TestCase(1UL, 1165)]
        [TestCase(4UL, 1203)]
        [TestCase(101UL, 867)]
        public void Traits_DByHand(ulong key, int reaction)
        {
            Assert.That(S2.Fire(1000, 0, 0, 300).ReactionOf(S2.Placed(key)), Is.EqualTo(reaction));
        }

        // A hundred ids (keys 1..100), team values R 15, B 3, Q 30, s = 300: minimum, maximum, and how many fall below and
        // above the team value, each exactly as the formula gives.
        [Test]
        public void Traits_HundredShooters()
        {
            ReactionFire fire = S2.Fire(15, 3, 30, 300);
            var r = new List<int>(); var b = new List<int>(); var q = new List<int>();
            for (ulong key = 1; key <= 100; key++)
            {
                StableId id = S2.Placed(key);
                r.Add(fire.ReactionOf(id)); b.Add(fire.BurstOf(id)); q.Add(fire.PauseOf(id));
            }
            Assert.That(Summary(r, 15), Is.EqualTo((10, 19, 51, 34)), "reaction");
            Assert.That(Summary(b, 3), Is.EqualTo((2, 3, 42, 0)), "burst");
            Assert.That(Summary(q, 30), Is.EqualTo((21, 38, 48, 47)), "pause");

            ReactionFire same = S2.Fire(15, 3, 30, 0);
            for (ulong key = 1; key <= 100; key++)
            {
                StableId id = S2.Placed(key);
                Assert.That((same.ReactionOf(id), same.BurstOf(id), same.PauseOf(id)), Is.EqualTo((15, 3, 30)), "s = 0, key " + key);
            }
        }

        private static (int Min, int Max, int Below, int Above) Summary(List<int> values, int team)
        {
            int min = int.MaxValue, max = int.MinValue, below = 0, above = 0;
            foreach (int v in values) { min = Math.Min(min, v); max = Math.Max(max, v); if (v < team) below++; if (v > team) above++; }
            return (min, max, below, above);
        }

        // A burst of 1 at s = 500: 42 of the hundred ids round to 0 rounds and are held at 1, so nobody turns continuous;
        // a burst of 0 stays 0 (continuous fire), and a reaction or pause of 0 stays 0.
        [Test]
        public void Traits_BurstStaysAtLeastOne()
        {
            ReactionFire one = S2.Fire(0, 1, 0, 500), off = S2.Fire(0, 0, 0, 500);
            for (ulong key = 1; key <= 100; key++)
            {
                StableId id = S2.Placed(key);
                Assert.That(one.BurstOf(id), Is.EqualTo(1), "key " + key);
                Assert.That((off.ReactionOf(id), off.BurstOf(id), off.PauseOf(id)), Is.EqualTo((0, 0, 0)), "key " + key);
            }
        }

        // ---- the first shot and the rhythm on the line of 20 ----

        // s = 0 (written or left out): all twenty select B on tick 1 and fire 31, 36, 41, then pause, as before the
        // change (P3's rhythm model with R 30, B 3, Q 20); the two worlds hash alike on every tick, in version 3, and the
        // hash on 200 is the one this line gave on the start commit.
        [Test]
        public void Line_ZeroSpread_AllAlike()
        {
            S2.LineRun zero = S2.RunLine(S2.Ai(0), 200), absent = S2.RunLine(S2.Ai((int?)null), 200);
            Assert.That(zero.W.System<ReactionFire>().StateVersion, Is.EqualTo(3));
            Assert.That(absent.W.System<ReactionFire>().StateVersion, Is.EqualTo(3));
            Assert.That(zero.Hashes, Is.EqualTo(absent.Hashes));
            Assert.That(zero.Hashes[200], Is.EqualTo(0x50097E387F176170UL), "the hash on 200 recorded before the change (4fd77f9)");
            List<long> expected = P3.Expect(31, S2.B, S2.Q, 200);
            Assert.That(expected.GetRange(0, 4), Is.EqualTo(new long[] { 31, 36, 41, 61 }));
            for (int i = 0; i < S2.Shooters; i++)
            {
                Assert.That(zero.Select[i], Is.EqualTo(1L), "shooter " + i + " selects on tick 1");
                Assert.That(zero.Fires[i], Is.EqualTo(expected), "shooter " + i);
            }
        }

        // s = 300: still all select B on tick 1; each fires first on 1 + its own reaction and then keeps its own burst and
        // pause (the rhythm model with that shooter's values). First shots spread over 22..38.
        [Test]
        public void Line_Spread_EachOnItsOwnValues()
        {
            S2.LineRun r = S2.RunLine(S2.Ai(S2.Spread), 200);
            ReactionFire fire = r.W.System<ReactionFire>();
            Assert.That(fire.StateVersion, Is.EqualTo(4));
            var firsts = new SortedSet<long>();
            for (int i = 0; i < S2.Shooters; i++)
            {
                StableId id = S2.Placed(S2.FirstKey + (ulong)i);
                Assert.That((fire.ReactionOf(id), fire.BurstOf(id), fire.PauseOf(id)), Is.EqualTo((S2.LineR[i], S2.LineB[i], S2.LineQ[i])), "shooter " + i);
                Assert.That(r.Select[i], Is.EqualTo(1L), "shooter " + i + " selects on tick 1");
                Assert.That(r.Fires[i], Is.EqualTo(P3.Expect(1 + S2.LineR[i], S2.LineB[i], S2.LineQ[i], 200)), "shooter " + i);
                firsts.Add(r.Fires[i][0]);
            }
            Assert.That(firsts.Min, Is.EqualTo(22L));
            Assert.That(firsts.Max, Is.EqualTo(38L));
            Assert.That(firsts.Count, Is.EqualTo(12), "distinct first-shot ticks");
        }

        // ---- determinism, snapshots, versions ----

        [Test]
        public void Line_SameSeedTwice_AndReverseAllocation()
        {
            S2.LineRun a = S2.RunLine(S2.Ai(S2.Spread), 200), b = S2.RunLine(S2.Ai(S2.Spread), 200), rev = S2.RunLine(S2.Ai(S2.Spread), 200, true);
            Assert.That(b.Hashes, Is.EqualTo(a.Hashes));
            Assert.That(rev.Hashes, Is.EqualTo(a.Hashes));
            for (int i = 0; i < S2.Shooters; i++) Assert.That(rev.Fires[i], Is.EqualTo(a.Fires[i]), "shooter " + i);
        }

        // Saved on 45 (every shooter has fired: 6 inside a burst, 14 at a burst's end or in its pause) and restored, forward
        // and into the reverse allocation: the same hash at once and on every tick to 200. The payload is version 3's
        // followed by s, and every burst record is the one the rhythm model gives for that shooter.
        [TestCase(false)]
        [TestCase(true)]
        public void Line_RestoreMidBurst(bool reverse)
        {
            const long Save = 45, Last = 200;
            S2.LineRun straight = S2.RunLine(S2.Ai(S2.Spread), Last);
            S2.LineRun upTo = S2.RunLine(S2.Ai(S2.Spread), Save);
            byte[] snapshot = upTo.W.Sim.SaveRetainedSnapshot();
            M6World w = S2.LineWorld(S2.Ai(S2.Spread), reverse);
            Assert.DoesNotThrow(() => w.Sim.RestoreRetainedSnapshot(snapshot, reverseAllocation: reverse));
            Assert.That(w.Hash, Is.EqualTo(straight.Hashes[(int)Save]), "at once");
            for (long tick = Save + 1; tick <= Last; tick++)
            {
                w.Sim.Step(P2.Drive(w, tick, Array.Empty<P1.Move>()));
                Assert.That(w.Hash, Is.EqualTo(straight.Hashes[(int)tick]), "tick " + tick);
            }

            byte[] bytes = P3.Payload(upTo.W.System<ReactionFire>());
            P3.Saved s = P3.Parse(bytes);
            Assert.That(s.Count, Is.EqualTo(S2.Shooters));
            Assert.That((s.R, s.K, s.B, s.Q), Is.EqualTo((S2.R, 1, S2.B, S2.Q)));
            Assert.That(s.RemainingAtEnd, Is.EqualTo(4));
            Assert.That(BitConverter.ToInt32(bytes, bytes.Length - 4), Is.EqualTo(S2.Spread));
            var index = new Dictionary<ulong, int>();
            for (int i = 0; i < S2.Shooters; i++) index[S2.Placed(S2.FirstKey + (ulong)i).Value] = i;
            int inBurst = 0, atEnd = 0, none = 0;
            for (int j = 0; j < s.Count; j++)
            {
                int i = index[s.Entries[j].Shooter];
                (int shots, long last) = Record(P3.Expect(1 + S2.LineR[i], S2.LineB[i], S2.LineQ[i], Save), S2.LineB[i]);
                Assert.That(s.Bursts[j], Is.EqualTo((shots, last)), "shooter " + i);
                if (shots == 0) none++; else if (shots == S2.LineB[i]) atEnd++; else inBurst++;
            }
            Assert.That((none, inBurst, atEnd), Is.EqualTo((0, 6, 14)), "records with no shot, inside a burst, at a burst's end");
        }

        // The burst record after the given shots: the count is cleared by the aim after a pause and by the emptying shot.
        private static (int Shots, long Last) Record(List<long> fires, int burst)
        {
            int count = 0, fired = 0; long last = 0;
            foreach (long t in fires)
            {
                if (count == burst) count = 0;
                count++; fired++; last = t;
                if (fired % P3.Magazine == 0) count = 0;
            }
            return (count, last);
        }

        // Version 4 exactly when s is above 0, whatever R, K and B; version 4 and version 3 refuse each other's snapshots
        // at the manifest check (naming the system and both versions); two version-4 worlds with another s refuse at the
        // field. The state hash holds s.
        [Test]
        public void Versions_AndRefusals()
        {
            Assert.That(S2.Fire(15, 0, 0, 1).StateVersion, Is.EqualTo(4));
            Assert.That(S2.Fire(15, 3, 30, 0).StateVersion, Is.EqualTo(3));
            Assert.That(S2.Fire(15, 0, 0, 0).StateVersion, Is.EqualTo(1));
            S2.LineRun four = S2.RunLine(S2.Ai(S2.Spread), 45), three = S2.RunLine(S2.Ai(0), 45);
            Assert.That(P3.Systems(four.W), Is.EqualTo(P3.Systems(three.W)), "the same systems");
            foreach ((M6World from, M6World into, string saved, string expected) in new[]
                { (four.W, S2.LineWorld(S2.Ai(0)), "v4", "v3"), (three.W, S2.LineWorld(S2.Ai(S2.Spread)), "v3", "v4") })
            {
                var error = Assert.Throws<SimSnapshotException>(() => into.Sim.RestoreRetainedSnapshot(from.Sim.SaveRetainedSnapshot()));
                Assert.That(error.Message, Does.Contain("State registration manifest mismatch"));
                Assert.That(error.Message, Does.Contain("battle.ai-fire"));
                Assert.That(error.Message, Does.Contain(saved));
                Assert.That(error.Message, Does.Contain(expected));
            }
            // The state hash holds s: two decisions without entries that differ only in s hash apart.
            Assert.That(StateOf(S2.Fire(15, 3, 30, 200)), Is.Not.EqualTo(StateOf(S2.Fire(15, 3, 30, 300))), "s in the hash");
            Assert.That(StateOf(S2.Fire(15, 3, 30, 300)), Is.EqualTo(StateOf(S2.Fire(15, 3, 30, 300))));
            M6World other = S2.LineWorld(S2.Ai(200));
            var differs = Assert.Throws<SimSnapshotException>(() => other.Sim.RestoreRetainedSnapshot(four.W.Sim.SaveRetainedSnapshot()));
            Assert.That(differs.Message, Is.EqualTo("AI trait spread differs."));
        }

        private static ulong StateOf(ReactionFire fire)
        {
            var hash = new StateHash();
            fire.WriteState(hash);
            return hash.Value;
        }

        // Version 4 without a burst: the burst block is still written (B 0, the Q of the pack, one (0, 0) record per entry)
        // before s; 68 bytes for P1's one entry. It restores into the same configuration and hashes alike.
        [Test]
        public void Version4_WithoutBurst_Payload()
        {
            string ai = "{ \"reactionTicks\": 15, \"traitSpreadPermille\": 300 }";
            M6World f = P3.RunTo(ai, 60);
            byte[] bytes = P3.Payload(f.System<ReactionFire>());
            Assert.That(bytes.Length, Is.EqualTo(68));
            P3.Saved s = P3.Parse(bytes);
            Assert.That((s.R, s.Count, s.K, s.B, s.Q), Is.EqualTo((15, 1, 1, 0, 0)));
            Assert.That(s.Bursts, Is.EqualTo(new[] { (0, 0L) }));
            Assert.That(BitConverter.ToInt32(bytes, 64), Is.EqualTo(300));
            M6World b = P3.World(P2.Cast(), ai);
            Assert.DoesNotThrow(() => b.Sim.RestoreRetainedSnapshot(f.Sim.SaveRetainedSnapshot()));
            Assert.That(b.Hash, Is.EqualTo(f.Hash));
        }

        // P3's one shooter A (key 1: burst 2, pause 22 at B 3, Q 20, s 300; R 0): A fires 40, 45, then 45 + 22 = 67, 72.
        // Saved on 46 its record is (2, 45). The load checks a count against A's own burst: 2 is accepted, 3 (the team
        // value) is refused. Offsets: R 0, count 4, the entry 8..39, K B Q 40..51, the record 52 (count) and 56 (latest
        // shot), s 64.
        [Test]
        public void Load_CountAgainstTheShootersOwnBurst()
        {
            string ai = P3.Ai(0, null, 3, 20).Replace(" }", ", \"traitSpreadPermille\": 300 }");
            P3.Trace t = P3.Run(P2.Cast(), P1.Walk, ai, 120);
            Assert.That(t.A.FireTicks(120), Is.EqualTo(new long[] { 40, 45, 67, 72, 94, 99 }));
            M6World f = P3.RunTo(ai, 46);
            byte[] bytes = P3.Payload(f.System<ReactionFire>());
            Assert.That(bytes.Length, Is.EqualTo(68));
            Assert.That((BitConverter.ToInt32(bytes, 52), BitConverter.ToInt64(bytes, 56)), Is.EqualTo((2, 45L)));
            M6World w = P3.World(P2.Cast(), ai);
            Assert.DoesNotThrow(() => w.System<ReactionFire>().LoadState(new SimStateReader(bytes)));
            Array.Copy(BitConverter.GetBytes(3), 0, bytes, 52, 4);
            var error = Assert.Throws<SimSnapshotException>(() => P3.World(P2.Cast(), ai).System<ReactionFire>().LoadState(new SimStateReader(bytes)));
            Assert.That(error.Message, Is.EqualTo("AI burst shot count is out of range."));
        }

        // ---- with the other AI fire options ----

        // With the sense interval (K 10, R 15, B 3, Q 20, A2 aside): A (phase 4, first sample 46, R 17, B 2, Q 22) and
        // A2 (phase 6, first sample 44, R 18, B 3, Q 22) each keep their own values.
        [Test]
        public void With_SenseInterval()
        {
            string ai = P3.Ai(15, 10, 3, 20).Replace(" }", ", \"traitSpreadPermille\": 300 }");
            P3.Trace r = P3.Run(P2.Cast(P2.A2Aside), P1.Walk, ai, 300);
            Assert.That(r.A.FireTicks(300), Is.EqualTo(P3.Expect(46 + 17, 2, 22, 300)));
            Assert.That(r.A2.FireTicks(300), Is.EqualTo(P3.Expect(44 + 18, 3, 22, 300)));
            P3.Trace again = P3.Run(P2.Cast(P2.A2Aside), P1.Walk, ai, 300);
            Assert.That(again.Hashes, Is.EqualTo(r.Hashes));
        }

        // With burstPauseFollowsShooter (Rai01's B 3, Q 40): A's own burst 2 and pause 45 reach the cadence: fires 40, 45,
        // 90, 95, 140, 145, and after 45 the cadence holds A until 45 + 45 = 90. A snapshot on 60 restores and hashes alike.
        [Test]
        public void With_PauseFollowsShooter()
        {
            string ai = Rai01TimingTests.Ai(3, 40).Replace(" }", ", \"traitSpreadPermille\": 300 }");
            P3.Trace r = P3.Run(P2.Cast(), P1.Walk, ai, 150);
            Assert.That(r.A.FireTicks(150), Is.EqualTo(new long[] { 40, 45, 90, 95, 140, 145 }));
            M6World f = P3.RunTo(ai, 60);
            var reader = new SimStateReader(Rai01StateTests.Payload(f.System<BurstCadence>()));
            Assert.That(reader.Int32(), Is.EqualTo(1));
            Assert.That(reader.UInt64(), Is.EqualTo(P2.Id(f, P3.KeyA).Value));
            Assert.That(reader.Int64(), Is.EqualTo(90L));
            M6World b = P3.World(P2.Cast(), ai);
            Assert.DoesNotThrow(() => b.Sim.RestoreRetainedSnapshot(f.Sim.SaveRetainedSnapshot()));
            Assert.That(b.Hash, Is.EqualTo(r.Hashes[60]));
        }

        // With memory and suppression (Rai01's switch script, B destroyed on 60): the same hashes twice and after a
        // snapshot on 70; A's first burst is its own (R 17: first shot 57, burst 2: 57 and 62 cut by the loss of B on 60).
        [Test]
        public void With_Suppression()
        {
            string ai = "{ \"targetMemoryTicks\": 300, \"suppressTicks\": 90, \"reactionTicks\": 15, \"burstShots\": 3, "
                + "\"burstPauseTicks\": 20, \"traitSpreadPermille\": 300 }";
            var destroy = new P1.DestroyProbe(60, P3.KeyB);
            P3.Trace r = P3.Run(P2.Cast(), Rai01TimingTests.SwitchScript, ai, 200, false, destroy);
            P3.Trace again = P3.Run(P2.Cast(), Rai01TimingTests.SwitchScript, ai, 200, false, new P1.DestroyProbe(60, P3.KeyB));
            Assert.That(again.Hashes, Is.EqualTo(r.Hashes));
            Assert.That(r.A.FireTicks(60), Is.EqualTo(new long[] { 57 }));
            M6World f = P3.World(P2.Cast(), ai, false, new P1.DestroyProbe(60, P3.KeyB));
            for (long tick = 1; tick <= 70; tick++) f.Sim.Step(P2.Drive(f, tick, Rai01TimingTests.SwitchScript));
            M6World b = P3.World(P2.Cast(), ai, false, new P1.DestroyProbe(60, P3.KeyB));
            Assert.DoesNotThrow(() => b.Sim.RestoreRetainedSnapshot(f.Sim.SaveRetainedSnapshot()));
            Assert.That(b.Hash, Is.EqualTo(r.Hashes[70]));
            for (long tick = 71; tick <= 200; tick++)
            {
                b.Sim.Step(P2.Drive(b, tick, Rai01TimingTests.SwitchScript));
                Assert.That(b.Hash, Is.EqualTo(r.Hashes[(int)tick]), "tick " + tick);
            }
        }

        // ---- content and assembly ----

        [Test]
        public void Content_ValuesAndDefaults()
        {
            Assert.That(Rules(null).TraitSpreadPermille, Is.EqualTo(0), "section left out");
            Assert.That(Rules("{ }").TraitSpreadPermille, Is.EqualTo(0), "field left out");
            Assert.That(Rules("{ \"traitSpreadPermille\": 0 }").TraitSpreadPermille, Is.EqualTo(0));
            Assert.That(Rules("{ \"traitSpreadPermille\": 300 }").TraitSpreadPermille, Is.EqualTo(300));
            Assert.That(Rules("{ \"traitSpreadPermille\": 500 }").TraitSpreadPermille, Is.EqualTo(500));
            // A scheduler-side field: valid under any part.
            Assert.That(Rules("{ \"placement\": \"scatter\", \"scatterMm\": 6000, \"engage\": \"bound\", \"boundMoveTicks\": 60, \"boundFireTicks\": 90, "
                + "\"boundSharePermille\": 700, \"boundMinRangeMm\": 12000, \"traitSpreadPermille\": 300 }").TraitSpreadPermille, Is.EqualTo(300));
        }

        [TestCase("{ \"traitSpreadPermille\": -1 }", "out of range [0, 500]")]
        [TestCase("{ \"traitSpreadPermille\": 501 }", "out of range [0, 500]")]
        [TestCase("{ \"traitSpreadPermille\": 1.5 }", "must be an integer")]
        public void Content_Errors(string ai, string message)
        {
            var e = Assert.Throws<ContentException>(() => A3.Load(P1.PackJson(ai)));
            Assert.That(e.Message, Is.EqualTo(Prefix + "traitSpreadPermille': " + message));
        }

        // The pack's value reaches the registered decision; a spread alone (R 0, K 1, B 0) registers nothing, as before.
        [Test]
        public void Assembly_PassesTheSpread()
        {
            M6World w = P3.World(P2.Cast(), "{ \"reactionTicks\": 15, \"traitSpreadPermille\": 300 }");
            Assert.That(w.System<ReactionFire>().StateVersion, Is.EqualTo(4));
            Assert.That(w.System<ReactionFire>().ReactionOf(P2.Id(w, P3.KeyA)), Is.EqualTo(17));
            Assert.That(P3.Systems(P3.World(P2.Cast(), "{ \"traitSpreadPermille\": 300 }")), Does.Not.Contain("battle.ai-fire"));
        }

        [TestCase(-1)]
        [TestCase(501)]
        public void Constructor_RefusesOutOfRange(int spread)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => S2.Fire(15, 3, 30, spread));
        }
    }
}
