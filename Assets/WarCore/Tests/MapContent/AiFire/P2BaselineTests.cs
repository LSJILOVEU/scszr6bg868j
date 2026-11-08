using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Content;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // Acceptance 1(b), recorded on the code before P2 (base a8ac432) with the reaction time on (15) and no sense interval:
    // (b1) the P1 fixture alone and (b2) the twin fixture with A2 straight behind A on A's line to B (the ally check holds
    // A2's fire). Recorded: the 600-tick run (64-bit FNV-1a over the per-tick hashes), the state hash after tick 600, every
    // shooter's fire ticks, A2's DueMilli sequence (FNV-1a) and the retained snapshots at 300 and 600 (length, digest).
    // Then the same two fixtures with "senseTicks": 1 written out, and 1(c)(d): a sense interval of 1 registers nothing.
    public sealed class P2BaselineTests
    {
        internal const string Systems = "bridge.dead-cleanup|0|5;battle.reinforce|0|8;interim.orders|0|10;battle.ai|0|12;battle.ai-fire|0|14;"
            + "interim.locomotion|1|20;combat.system|3|30;battle.capture|3|40;battle.match|3|50";
        internal static readonly long[] FiresA = { 55, 85, 115, 175, 205, 235, 295, 325, 355, 415, 445, 475, 535, 565, 595 };

        // (b1) P1's fixture: A alone, B walks in from tick 20 (P1.Walk).
        internal const ulong Run600 = 12408100022081845447UL, End600 = 10081314224586331524UL;
        internal const int Snapshot300Length = 6531, Snapshot600Length = 6531;
        internal const ulong Snapshot300Digest = 1187515356512560726UL, Snapshot600Digest = 10151445073216429489UL;

        // (b2) A2 behind A: B is first picked by A on 40 and by A2 on 60; A2's first TryAim (75) is held by the ally check,
        // its DueMilli jumps to 75000 + 30000 and it never fires.
        internal const ulong TwinRun600 = 5383590616295255197UL, TwinEnd600 = 439506984530672070UL, TwinDue600 = 8289626199243901359UL;
        internal const int TwinSnapshot600Length = 7344;
        internal const ulong TwinSnapshot600Digest = 211481255301620509UL;
        internal const long TwinA2First = 60, TwinA2Aim = 75;

        [Test]
        public void ReactionOnly_Unchanged()
        {
            M6World w = null;
            Assert.DoesNotThrow(() => w = P1.World(P1.Cast(), P1.Ai(15)));
            var hashes = new List<ulong> { w.Hash };
            var fires = new List<long>();
            for (long tick = 1; tick <= 600; tick++)
            {
                w.Sim.Step(P1.Drive(w, tick, P1.Walk));
                hashes.Add(w.Hash);
                if (P1.Fired(w, P1.KeyA)) fires.Add(tick);
                if (tick == 300 || tick == 600)
                {
                    byte[] snapshot = w.Sim.SaveRetainedSnapshot();
                    Assert.That(snapshot.Length, Is.EqualTo(tick == 300 ? Snapshot300Length : Snapshot600Length), "length, tick " + tick);
                    Assert.That(P2.Digest(snapshot), Is.EqualTo(tick == 300 ? Snapshot300Digest : Snapshot600Digest), "digest, tick " + tick);
                }
            }
            Assert.That(P2.Systems(w), Is.EqualTo(Systems));
            Assert.That(fires, Is.EqualTo(FiresA), "A's shots");
            Assert.That(P2.Fold(hashes), Is.EqualTo(Run600));
            Assert.That(w.Hash, Is.EqualTo(End600));
        }

        // (b2) The explicit combat pack of the P2 fixture is the P1 fixture's (same hash at the start); the twin behind A
        // selects B on 60, is held on 75 (DueMilli 105000) and on 105 (135000), and never fires in 600 ticks.
        [Test]
        public void ReactionOnly_TwinBehind_Unchanged()
        {
            P2.Trace r = P2.Run(P2.Cast(P2.A2Behind), P1.Walk, P2.Ai(15, null), 600);
            Assert.That(P1.World(P2.Cast(P2.A2Behind), P1.Ai(15)).Hash, Is.EqualTo(r.Hashes[0]), "the written-out pack equals A2.Combat(0)");
            Assert.That((r.W.Combat.Pack.AllyCheckMissRaw, r.W.Combat.Pack.AllyHoldMilli), Is.EqualTo((P2.AllyMissRaw, P2.AllyHoldMilli)));
            Assert.That(P2.Systems(r.W), Is.EqualTo(Systems));
            Assert.That(r.A.FirstSelect(r.Id(P2.KeyB)), Is.EqualTo(P1.T));
            Assert.That(r.A2.FirstSelect(r.Id(P2.KeyB)), Is.EqualTo(TwinA2First));
            Assert.That(r.A.FireTicks(600), Is.EqualTo(FiresA), "A's shots");
            Assert.That(r.A2.FireTicks(600), Is.Empty, "A2 is held by the ally check every time");
            Assert.That(r.A2.Due[(int)TwinA2Aim - 1], Is.EqualTo(0L));
            Assert.That(r.A2.Due[(int)TwinA2Aim], Is.EqualTo(TwinA2Aim * 1000 + P2.AllyHoldMilli), "the first hold");
            Assert.That(r.A2.Due[(int)TwinA2Aim + 30], Is.EqualTo((TwinA2Aim + 30) * 1000 + P2.AllyHoldMilli), "the second hold");
            Assert.That(P2.Fold(r.A2.Due), Is.EqualTo(TwinDue600));
            Assert.That(P2.Fold(r.Hashes), Is.EqualTo(TwinRun600));
            Assert.That(r.Hashes[600], Is.EqualTo(TwinEnd600));
            byte[] snapshot = r.W.Sim.SaveRetainedSnapshot();
            Assert.That(snapshot.Length, Is.EqualTo(TwinSnapshot600Length));
            Assert.That(P2.Digest(snapshot), Is.EqualTo(TwinSnapshot600Digest));
        }

        // 1(b): "senseTicks": 1 written out beside the reaction time: the same system table, state version 1, the same hash
        // on every tick and the same snapshot bytes on 300, for the single shooter (b1) and the twin behind A (b2).
        [Test]
        public void SenseOne_IsByteForByteTheSame()
        {
            Same(P1.World(P1.Cast(), P1.Ai(15)), P1.World(P1.Cast(), P2.Ai(15, 1)), "b1");
            Same(P2.World(P2.Cast(P2.A2Behind), P2.Ai(15, null)), P2.World(P2.Cast(P2.A2Behind), P2.Ai(15, 1)), "b2");
        }
        private static void Same(M6World plain, M6World one, string what)
        {
            Assert.That(P2.Systems(one), Is.EqualTo(Systems), what);
            Assert.That(one.System<ReactionFire>().StateVersion, Is.EqualTo(1), what);
            Assert.That(one.Hash, Is.EqualTo(plain.Hash), what + ", tick 0");
            for (long tick = 1; tick <= 600; tick++)
            {
                plain.Sim.Step(P1.Drive(plain, tick, P1.Walk));
                one.Sim.Step(P1.Drive(one, tick, P1.Walk));
                Assert.That(one.Hash, Is.EqualTo(plain.Hash), what + ", tick " + tick);
                if (tick == 300) Assert.That(one.Sim.SaveRetainedSnapshot(), Is.EqualTo(plain.Sim.SaveRetainedSnapshot()), what + ", snapshot bytes on 300");
            }
        }

        // 1(c): { "reactionTicks": 0, "senseTicks": 1 } beside P1's world written as 0: the same system table (without
        // battle.ai-fire) and the same hash after 600 ticks.
        [Test]
        public void ZeroAndOne_RegisterNothing()
        {
            M6World zero = P1.World(P2.Cast(), P1.Ai(0)), one = null;
            Assert.DoesNotThrow(() => one = P2.World(P2.Cast(), P2.Ai(0, 1)));
            Assert.That(P2.Systems(one), Is.EqualTo(P1BaselineTests.Systems));
            Assert.That(P2.Systems(zero), Is.EqualTo(P1BaselineTests.Systems));
            Assert.That(P2.Systems(one), Does.Not.Contain("battle.ai-fire"));
            Assert.That(one.System<ReactionFire>(), Is.Null);
            for (long tick = 1; tick <= 600; tick++)
            {
                zero.Sim.Step(P1.Drive(zero, tick, P1.Walk));
                one.Sim.Step(P1.Drive(one, tick, P1.Walk));
            }
            Assert.That(one.Hash, Is.EqualTo(zero.Hash));
        }

        // 1(d): a flat map (no surface table, no height field) with a content layer that sets senseTicks 10: not a terrain
        // world, so nothing is registered and the system table is the one of 1(c).
        [Test]
        public void FlatMapWithSenseInterval_DoesNotRegister()
        {
            byte[] map = WcMapWriter.Write(Wp23.Make(A2.Width, A2.Depth, 0, 4 * A2.S, A2.DWalls, 2 * A2.S));
            M6World flat = null;
            Assert.DoesNotThrow(() => flat = new M6World(P2.Recipe(P2.Cast(), P2.Ai(0, 10)), map));
            Assert.That(P2.Systems(flat), Is.EqualTo(P1BaselineTests.Systems));
            Assert.That(P2.Systems(flat), Does.Not.Contain("battle.ai-fire"));
            Assert.That(flat.System<ReactionFire>(), Is.Null);
        }
    }
}
