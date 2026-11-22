using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // AI-P5b, the regroup mark of a squad: -1 not regrouping, a tick regrouping since then, -2 short and not regrouping.
    // It changes on form ticks only, after every squad has recruited, and it is saved.
    public sealed partial class AiP5bTests
    {
        private static byte[] Save(M6World w) { var writer = new SimStateWriter(); Sq(w).SaveState(writer); return writer.ToArray(); }
        private static ulong HashOf(M6World w) { var hash = new StateHash(); Sq(w).WriteState(hash); return hash.Value; }
        // The mark of the squad the man leads, read from what the system saves.
        private static long Mark(M6World w, int[] seven, int[] regroup, ulong leader)
        {
            var reader = new SimStateReader(Save(w));
            for (int i = 0; i < seven.Length + regroup.Length; i++) reader.Int32();
            int squads = reader.Int32();
            for (int i = 0; i < squads; i++)
            {
                ulong key = reader.UInt64(); reader.Int32(); long mark = reader.Int64();
                if (key == Stable(leader)) return mark;
            }
            throw new InvalidOperationException("The man leads no squad.");
        }

        // A leader (key 1) and two members (2, 3) of a squad of size 4, alone on the field: strength 3 * 1000 / 5 = 600.
        // Nobody is free, so nothing is recruited and the strength stays.
        private static M6World Three(int[] regroup, long mark, Action<M6World> setup = null)
        {
            var units = new[] { Man(1, 1, 20000), Man(2, 1, 21000), Man(3, 1, 22000), Man(90, 2, 150000, 50000) };
            M6World w = Field(Json(Seven, null, regroup), units, setup);
            Tables(w, Seven, regroup, new Squad(1, 4, mark, 2, 3));
            Assert.That(Strength(w, 1), Is.EqualTo(600));
            return w;
        }

        // 4. A squad that is not regrouping starts to when its strength is below the Below line, on a form tick and on no
        // other: with the line at its strength (600) it does not, one above it does, and the mark is the form tick.
        [Test]
        public void ASquadBelowTheLineRegroupsFromTheNextFormTick()
        {
            int[] at = Rules(600, 1000, 1200), above = Rules(601, 1000, 1200);
            M6World stays = Three(at, -1), goes = Three(above, -1);
            for (int tick = 1; tick < Form; tick++)
            {
                stays.Step(); goes.Step();
                Assert.That(Rally(goes, 1), Is.EqualTo(SquadRally.None), "tick " + tick);
                Assert.That(Mark(goes, Seven, above, 1), Is.EqualTo(-1L), "tick " + tick);
            }
            stays.Step(); goes.Step();
            Assert.That(Rally(stays, 1), Is.EqualTo(SquadRally.None));
            Assert.That(Mark(stays, Seven, at, 1), Is.EqualTo(-1L));
            foreach (ulong key in new ulong[] { 1, 2, 3 }) Assert.That(Rally(goes, key), Is.EqualTo(SquadRally.Active), "key " + key);
            Assert.That(Mark(goes, Seven, above, 1), Is.EqualTo(15L));
            // The mark keeps the tick it started on.
            To(goes, 3 * Form + 7);
            Assert.That(Mark(goes, Seven, above, 1), Is.EqualTo(15L));
        }

        // 4. A regrouping squad sets out again when its strength is at least the Until line: with the line at its
        // strength it does, one above it does not.
        [Test]
        public void ARegroupingSquadSetsOutAtTheUntilLine()
        {
            int[] at = Rules(100, 600, 100000), above = Rules(100, 601, 100000);
            M6World goes = Three(at, 3), stays = Three(above, 3);
            To(goes, Form - 1); To(stays, Form - 1);
            Assert.That(Rally(goes, 2), Is.EqualTo(SquadRally.Active), "not before the form tick");
            To(goes, Form); To(stays, Form);
            Assert.That(Rally(goes, 2), Is.EqualTo(SquadRally.None));
            Assert.That(Mark(goes, Seven, at, 1), Is.EqualTo(-1L));
            Assert.That(Rally(stays, 2), Is.EqualTo(SquadRally.Active));
            Assert.That(Mark(stays, Seven, above, 1), Is.EqualTo(3L));
        }

        // 4. A squad that cannot fill up gives up on the form tick on which MaxTicks ticks have passed since it started.
        // Started on tick 15: with 30 ticks it gives up on tick 45 (30 passed) and not on tick 30 (15 passed); with 31 ticks
        // not on tick 45 either, but on tick 60.
        [TestCase(30, 45)]
        [TestCase(31, 60)]
        [TestCase(16, 45)]
        [TestCase(15, 30)]
        public void ARegroupingSquadGivesUpAfterMaxTicks(int maxTicks, int gaveUp)
        {
            int[] regroup = Rules(700, 800, maxTicks);
            M6World w = Three(regroup, 15);
            for (int tick = 1; tick <= 75; tick++)
            {
                w.Step();
                Assert.That(Rally(w, 1), Is.EqualTo(tick < gaveUp ? SquadRally.Active : SquadRally.Idle), "tick " + tick);
                Assert.That(Mark(w, Seven, regroup, 1), Is.EqualTo(tick < gaveUp ? 15L : -2L), "tick " + tick);
            }
        }

        // 4. A squad that gave up (or was never whole) is idle until its strength is at least the Below line; then it is a
        // squad like any other. With the line at its strength (600) it is, one above it stays idle. The Until line (far
        // above here) has nothing to say about it.
        [Test]
        public void AnIdleSquadSettlesAtTheBelowLine()
        {
            int[] at = Rules(600, 1000, 30), above = Rules(601, 1000, 30);
            M6World settles = Three(at, -2), stays = Three(above, -2);
            To(settles, Form - 1); To(stays, Form - 1);
            Assert.That(Rally(settles, 3), Is.EqualTo(SquadRally.Idle), "not before the form tick");
            To(settles, Form); To(stays, Form);
            Assert.That(Rally(settles, 3), Is.EqualTo(SquadRally.None));
            Assert.That(Mark(settles, Seven, at, 1), Is.EqualTo(-1L));
            Assert.That(Rally(stays, 3), Is.EqualTo(SquadRally.Idle));
            To(stays, 5 * Form);
            Assert.That(Mark(stays, Seven, above, 1), Is.EqualTo(-2L), "it does not start to regroup either");
        }

        // 4. Recruiting comes before the marks on a form tick: a squad of size 4 with one member (400, below the line of
        // 500) and two free men in reach has three members (800) by the time it is looked at, and does not regroup.
        // Without the two it does.
        [Test]
        public void ASquadThatFillsUpOnTheFormTickDoesNotRegroup()
        {
            int[] regroup = Rules(500, 750, 1200);
            var units = new[] { Man(1, 1, 20000), Man(2, 1, 21000), Man(3, 1, 22000), Man(4, 1, 23000), Man(90, 2, 150000, 50000) };
            M6World w = Field(Json(Seven, null, regroup), units);
            Tables(w, Seven, regroup, new Squad(1, 4, -1, 2));
            Assert.That(Strength(w, 1), Is.EqualTo(400));
            To(w, Form);
            Assert.That(Members(w, 1), Is.EqualTo(3));
            Assert.That(Strength(w, 1), Is.EqualTo(800));
            Assert.That(Rally(w, 1), Is.EqualTo(SquadRally.None));

            var alone = new[] { Man(1, 1, 20000), Man(2, 1, 21000), Man(90, 2, 150000, 50000) };
            M6World short2 = Field(Json(Seven, null, regroup), alone);
            Tables(short2, Seven, regroup, new Squad(1, 4, -1, 2));
            To(short2, Form);
            Assert.That(Rally(short2, 1), Is.EqualTo(SquadRally.Active));
            Assert.That(Mark(short2, Seven, regroup, 1), Is.EqualTo(15L));
        }

        // 4. A new squad starts idle: it was never whole, so being short is no loss. Sizes 6..6, reach 5 m. Two men stand
        // together; two more walk in from 10 m and from 20 m away. The squad of two (strength 2 * 1000 / 7 = 285, below the
        // line of 500) is idle and stays idle with two members (428); with three (4 * 1000 / 7 = 571) it settles on that
        // form tick. Later one member falls on tick 600 (428 again): from the next form tick, 615, it regroups.
        [Test]
        public void ANewSquadShortOfMembersIsIdleUntilItHasBeenStrong()
        {
            int[] seven = { 6, 6, 0, 5000, 15, 0, 0 }, regroup = Rules(500, 750, 100000);
            ulong[] k = ByElect(1, 2);
            var units = new[]
            {
                Man(k[0], 1, 20000), Man(k[1], 1, 21000), Man(3, 1, 31000, 20000, 2000), Man(4, 1, 41000, 20000, 2000), Man(90, 2, 150000, 50000),
            };
            M6World w = Field(Json(seven, null, regroup), units, x => Kill(x, 600, 3));
            L4.Moves(w).Order(Id(w, 3), SimVec3.Metres(22, 0, 20));
            L4.Moves(w).Order(Id(w, 4), SimVec3.Metres(23, 0, 20));
            To(w, Form - 1);
            Assert.That(Sq(w).SquadCount, Is.Zero);
            var seen = new HashSet<int>();
            for (int tick = Form; tick < 600; tick++)
            {
                w.Step();
                int members = Members(w, k[0]);
                seen.Add(members);
                Assert.That(Strength(w, k[0]), Is.EqualTo(new[] { 0, 285, 428, 571 }[members]), "tick " + tick);
                Assert.That(Rally(w, k[0]), Is.EqualTo(members < 3 ? SquadRally.Idle : SquadRally.None), "tick " + tick + ", " + members + " members");
                Assert.That(Mark(w, seven, regroup, k[0]), Is.EqualTo(members < 3 ? -2L : -1L), "tick " + tick);
            }
            Assert.That(seen, Is.EquivalentTo(new[] { 1, 2, 3 }), "the squad grew one man at a time");
            for (int tick = 600; tick < 615; tick++)
            {
                w.Step();
                Assert.That(Rally(w, k[0]), Is.EqualTo(SquadRally.None), "tick " + tick);
            }
            Assert.That(Members(w, k[0]), Is.EqualTo(2));
            w.Step();
            Assert.That(Rally(w, k[0]), Is.EqualTo(SquadRally.Active));
            Assert.That(Mark(w, seven, regroup, k[0]), Is.EqualTo(615L));
        }

        // 4. A new squad that is strong enough the moment it forms settles on that same form tick: five men, sizes 4..4,
        // strength 1000. A squad that dissolves takes its mark with it, and the squad that forms in its place starts anew.
        [Test]
        public void ANewSquadThatIsStrongSettlesAtOnce()
        {
            int[] seven = { 4, 4, 0, 10000, 15, 0, 0 }, regroup = Rules(900, 1000, 100000);
            ulong[] k = ByElect(1, 2, 3, 4, 5);
            var units = new List<UnitSpawn>();
            for (int i = 0; i < 5; i++) units.Add(Man(k[i], 1, 20000 + i * 1000));
            units.Add(Man(90, 2, 150000, 50000));
            M6World w = Field(Json(seven, null, regroup), units.ToArray(), x => Kills(x, (20, k[4]), (50, k[0])));
            To(w, Form);
            Assert.That(Rally(w, k[0]), Is.EqualTo(SquadRally.None));
            Assert.That(Mark(w, seven, regroup, k[0]), Is.EqualTo(-1L));
            // One member falls: 800, below the line of 900, regrouping from tick 30.
            To(w, 2 * Form);
            Assert.That(Mark(w, seven, regroup, k[0]), Is.EqualTo(30L));
            // The leader falls on tick 50; the three that are left form under the next leader on tick 60: 3 * 1000 / 5 =
            // 600, short from the start, so idle and not regrouping.
            To(w, 51);
            Assert.That(Sq(w).SquadCount, Is.Zero);
            Assert.That(Save(w), Is.EqualTo(Payload(seven, regroup, new (ulong, int, long)[0], new (ulong, ulong)[0])));
            To(w, 4 * Form);
            Assert.That(Band(w, k[2]), Is.EqualTo(k[1]));
            Assert.That(Strength(w, k[2]), Is.EqualTo(600));
            Assert.That(Mark(w, seven, regroup, k[1]), Is.EqualTo(-2L));
            Assert.That(Rally(w, k[3]), Is.EqualTo(SquadRally.Idle));
            To(w, 8 * Form);
            Assert.That(Mark(w, seven, regroup, k[1]), Is.EqualTo(-2L));
        }

        // 7. What is saved and hashed with the regroup fields, byte for byte: version 2, the seven numbers, the three
        // regroup numbers, then every squad's leader, size and mark, then the members. Three squads with the three kinds
        // of mark.
        [Test]
        public void State_TheThreeNumbersAndEverySquadsMark()
        {
            int[] regroup = Rules(500, 750, 1200);
            M6World w = Field(Json(Seven, Rules(300, 250, 600, 8000), regroup), Twenty());
            Assert.That(Sq(w).StateVersion, Is.EqualTo(2));
            Assert.That(Save(w), Is.EqualTo(Payload(Seven, regroup, new (ulong, int, long)[0], new (ulong, ulong)[0])));
            Squad[] squads = { new Squad(1, 4, -1, 11, 12), new Squad(2, 3, 45, 13), new Squad(3, 9, -2, 14) };
            byte[] payload = Payload(Seven, regroup, squads);
            Sq(w).LoadState(new SimStateReader(payload));
            Assert.That(Save(w), Is.EqualTo(payload));
            Assert.That(Rally(w, 12), Is.EqualTo(SquadRally.None));
            Assert.That(Rally(w, 13), Is.EqualTo(SquadRally.Active));
            Assert.That(Rally(w, 3), Is.EqualTo(SquadRally.Idle));
            Assert.That(Strength(w, 13), Is.EqualTo(500), "the strength is told right after a load");

            var leaders = new List<(ulong key, int size, long mark)> { (Stable(1), 4, -1), (Stable(2), 3, 45), (Stable(3), 9, -2) };
            var members = new List<(ulong key, ulong leader)> { (Stable(11), Stable(1)), (Stable(12), Stable(1)), (Stable(13), Stable(2)), (Stable(14), Stable(3)) };
            leaders.Sort((a, b) => a.key.CompareTo(b.key)); members.Sort((a, b) => a.key.CompareTo(b.key));
            var hash = new StateHash(); hash.Add("battle.ai-squads"); hash.Add(2);
            foreach (int value in Seven) hash.Add(value);
            foreach (int value in regroup) hash.Add(value);
            hash.Add(leaders.Count);
            foreach ((ulong key, int size, long mark) in leaders) { hash.Add(key); hash.Add(size); hash.Add(mark); }
            hash.Add(members.Count);
            foreach ((ulong key, ulong leader) in members) { hash.Add(key); hash.Add(leader); }
            Assert.That(HashOf(w), Is.EqualTo(hash.Value));
            // Another mark, another hash.
            squads[1].Mark = 46;
            Sq(w).LoadState(new SimStateReader(Payload(Seven, regroup, squads)));
            Assert.That(HashOf(w), Is.Not.EqualTo(hash.Value));
        }

        // 1, 7. Without the regroup fields the state is what it was before them, tempers or not: version 1, no regroup
        // numbers, no marks.
        [Test]
        public void State_WithoutRegroupingNothingMoreIsSaved()
        {
            foreach (int[] temper in new[] { null, Rules(300, 250, 600, 8000) })
            {
                M6World w = Field(Json(Seven, temper), Twenty());
                Assert.That(Sq(w).StateVersion, Is.EqualTo(1));
                Tables(w, Seven, null, new Squad(1, 4, -1, 11, 12));
                var writer = new SimStateWriter();
                foreach (int value in Seven) writer.Int32(value);
                writer.Int32(1); writer.UInt64(Stable(1)); writer.Int32(4);
                writer.Int32(2);
                foreach (ulong member in Stable(11) < Stable(12) ? new ulong[] { 11, 12 } : new ulong[] { 12, 11 }) { writer.UInt64(Stable(member)); writer.UInt64(Stable(1)); }
                Assert.That(Save(w), Is.EqualTo(writer.ToArray()));
                var hash = new StateHash(); hash.Add("battle.ai-squads"); hash.Add(1);
                foreach (int value in Seven) hash.Add(value);
                hash.Add(1); hash.Add(Stable(1)); hash.Add(4);
                hash.Add(2);
                foreach (ulong member in Stable(11) < Stable(12) ? new ulong[] { 11, 12 } : new ulong[] { 12, 11 }) { hash.Add(Stable(member)); hash.Add(Stable(1)); }
                Assert.That(HashOf(w), Is.EqualTo(hash.Value));
                Assert.That(Rally(w, 1), Is.EqualTo(SquadRally.None));
            }
        }

        private static void Refused(M6World w, byte[] payload, string reason)
        {
            var e = Assert.Catch<InvalidOperationException>(() => Sq(w).LoadState(new SimStateReader(payload)), reason);
            Assert.That(e.Message, Is.EqualTo(reason));
        }

        // 7. Load refuses a mark below -2, and each of the three regroup numbers is compared with the battle's own.
        [Test]
        public void State_LoadRefusesAMarkThatCannotBeAndOtherRegroupNumbers()
        {
            int[] regroup = Rules(500, 750, 1200);
            M6World w = Field(Json(Seven, null, regroup), Twenty());
            foreach (long mark in new long[] { -2, -1, 0, 1, long.MaxValue })
            {
                Assert.DoesNotThrow(() => Tables(w, Seven, regroup, new Squad(1, 4, mark, 11)), "mark " + mark);
                Assert.That(Rally(w, 11), Is.EqualTo(mark >= 0 ? SquadRally.Active : mark == -2 ? SquadRally.Idle : SquadRally.None), "mark " + mark);
            }
            foreach (long mark in new long[] { -3, long.MinValue })
                Refused(w, Payload(Seven, regroup, new Squad(1, 4, mark, 11)), "AI squad regroup mark is out of range.");
            for (int i = 0; i < 3; i++)
            {
                int[] other = (int[])regroup.Clone(); other[i]++;
                Refused(w, Payload(Seven, other, new Squad(1, 4, -1, 11)), "AI squad rules differ.");
            }
            // A load that was refused half-way and the next good one: the marks are the good one's.
            Tables(w, Seven, regroup, new Squad(2, 4, 9, 12));
            Assert.That(Rally(w, 12), Is.EqualTo(SquadRally.Active));
            Assert.That(Rally(w, 1), Is.EqualTo(SquadRally.None));
            Assert.That(Sq(w).SquadCount, Is.EqualTo(1));
        }

        // 7. A snapshot taken while a squad regroups, restored into a new world: the same marks, and the same hash on each
        // of the next 300 ticks as the battle that went on (the squad gives up on the way). A snapshot of a battle without
        // regrouping is refused by one with it, and the other way round.
        [Test]
        public void State_RestoreWhileASquadRegroups()
        {
            int[] seven = { 4, 4, 0, 10000, 15, 0, 0 }, regroup = Rules(900, 1000, 100), temper = Rules(300, 250, 600, 8000);
            ulong[] k = ByElect(1, 2, 3, 4, 5);
            M6World Make(int[] r)
            {
                var units = new List<UnitSpawn>();
                for (int i = 0; i < 5; i++) units.Add(Man(k[i], 1, 20000 + i * 1000));
                units.Add(Man(90, 2, 150000, 50000));
                return Field(Json(seven, temper, r), units.ToArray(), x => Kill(x, 20, k[4]));
            }
            M6World first = Make(regroup);
            To(first, 40);
            Assert.That(Mark(first, seven, regroup, k[0]), Is.EqualTo(30L));
            byte[] snapshot = first.Sim.SaveRetainedSnapshot();
            M6World restored = Make(regroup);
            Assert.That(restored.Hash, Is.Not.EqualTo(first.Hash));
            restored.Sim.RestoreRetainedSnapshot(snapshot);
            Assert.That(restored.Hash, Is.EqualTo(first.Hash));
            Assert.That(Rally(restored, k[1]), Is.EqualTo(SquadRally.Active));
            Assert.That(Mood(restored, k[1]), Is.EqualTo(Mood(first, k[1])));
            for (int tick = 0; tick < 300; tick++)
            {
                first.Step(); restored.Step();
                Assert.That(restored.Hash, Is.EqualTo(first.Hash), "tick " + first.Sim.Clock.Tick);
            }
            Assert.That(Mark(restored, seven, regroup, k[0]), Is.EqualTo(-2L), "it gave up on tick 135");
            Assert.That(Save(restored), Is.EqualTo(Save(first)));

            M6World without = Make(null);
            To(without, 40);
            Assert.Catch(() => Make(regroup).Sim.RestoreRetainedSnapshot(without.Sim.SaveRetainedSnapshot()));
            Assert.Catch(() => Make(null).Sim.RestoreRetainedSnapshot(snapshot));
            Assert.DoesNotThrow(() => Make(null).Sim.RestoreRetainedSnapshot(without.Sim.SaveRetainedSnapshot()));
            // The marks are part of the world's hash.
            ulong before = restored.Hash;
            Tables(restored, seven, regroup, new Squad(k[0], 4, 7, k[1], k[2], k[3]));
            Assert.That(restored.Hash, Is.Not.EqualTo(before));
        }
    }
}
