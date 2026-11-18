using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // AiS3Tests: the saved state, the content fields, the view and the allocation check.
    public sealed partial class AiS3Tests
    {
        // ---- Saved state ----

        // Each of the ten numbers is part of the saved state: a battle with one changed refuses the other's snapshot.
        [Test]
        public void State_EveryRuleIsSavedAndChecked()
        {
            int[][] variants =
            {
                Values, With(0, 2999), With(1, 1999), With(2, 2999), With(3, 999), With(4, 13), With(5, 7001), With(6, 999), With(7, 2001), With(8, 499), With(9, 299),
            };
            var worlds = new M6World[variants.Length]; var saved = new byte[variants.Length][]; var own = new byte[variants.Length][];
            for (int i = 0; i < variants.Length; i++)
            {
                worlds[i] = Battle(41, null, Json(variants[i], BoundAi));
                worlds[i].Step();
                saved[i] = worlds[i].Sim.SaveRetainedSnapshot();
                var writer = new SimStateWriter(); worlds[i].System<Pressure>().SaveState(writer); own[i] = writer.ToArray();
            }
            // The system's own hash holds each rule (the battle hash below also sees the content pack).
            var ownHashes = new HashSet<ulong>();
            foreach (int[] v in variants)
            {
                var hash = new StateHash();
                new Pressure(worlds[0].Combat, new PressureRules(v[0], v[1], v[2], v[3], v[4], v[5], v[6], v[7], v[8], v[9])).WriteState(hash);
                ownHashes.Add(hash.Value);
            }
            Assert.That(ownHashes.Count, Is.EqualTo(variants.Length), "each rule is part of the system's own hash");
            var hashes = new HashSet<ulong>();
            foreach (M6World world in worlds) hashes.Add(world.Hash);
            Assert.That(hashes.Count, Is.EqualTo(variants.Length), "each rule is part of the state hash");
            for (int i = 0; i < variants.Length; i++)
                for (int j = 0; j < variants.Length; j++)
                {
                    if (i == j) { Assert.DoesNotThrow(() => worlds[j].System<Pressure>().LoadState(new SimStateReader(own[i]))); continue; }
                    var e = Assert.Catch(() => worlds[j].System<Pressure>().LoadState(new SimStateReader(own[i])));
                    Assert.That(e.Message, Does.Contain("AI pressure rules differ."), i + " into " + j);
                    Assert.Catch(() => worlds[j].Sim.RestoreRetainedSnapshot(saved[i]));
                }
            // A battle without the system and one with it refuse each other.
            M6World plain = Battle(41, null, "{ " + BoundAi + " }");
            plain.Step();
            Assert.Catch(() => plain.Sim.RestoreRetainedSnapshot(saved[0]));
            Assert.Catch(() => worlds[0].Sim.RestoreRetainedSnapshot(plain.Sim.SaveRetainedSnapshot()));
        }

        private static byte[] Saved(int[] rules, params (ulong key, int value, bool broken)[] entries)
        {
            var writer = new SimStateWriter();
            foreach (int rule in rules) writer.Int32(rule);
            writer.Int32(entries.Length);
            foreach (var (key, value, broken) in entries) { writer.UInt64(key); writer.Int32(value); writer.Bool(broken); }
            return writer.ToArray();
        }

        [Test]
        public void State_LoadRejectsWhatStepCannotLeave()
        {
            M6World w = Field(null);
            int[] rules = { 3000, 2000, 3000, 1000, 12, 7000, 1000, 2000, 0, 300 };
            var p = new Pressure(w.Combat, new PressureRules(3000, 2000, 3000, 1000, 12, 7000, 1000, 2000, 0, 300));
            // A key that can break and one that cannot.
            ulong breaks = 1, steady = 1;
            while ((int)(Finalize(breaks ^ BreakSalt) % 1000UL) >= 300) breaks++;
            while ((int)(Finalize(steady ^ BreakSalt) % 1000UL) < 300) steady++;
            ulong low = Math.Min(breaks, steady), high = Math.Max(breaks, steady);
            void Bad(string message, params (ulong key, int value, bool broken)[] entries)
            {
                var e = Assert.Catch(() => p.LoadState(new SimStateReader(Saved(rules, entries))));
                Assert.That(e.Message, Does.Contain(message));
            }
            void Good(params (ulong key, int value, bool broken)[] entries) =>
                Assert.DoesNotThrow(() => p.LoadState(new SimStateReader(Saved(rules, entries))));
            Good();
            Good((low, 1, false), (high, Pressure.Max, high == breaks));
            Assert.That(p.Value(new StableId(low)), Is.EqualTo(1));
            Assert.That(p.Level(new StableId(high)), Is.EqualTo(high == breaks ? AiNerve.Broken : AiNerve.Pinned));
            Bad("not in strictly increasing order", (high, 5, false), (low, 5, false));
            Bad("not in strictly increasing order", (low, 5, false), (low, 5, false));
            Bad("out of range", (low, 0, false));
            Bad("out of range", (low, Pressure.Max + 1, false));
            // Broken above the recover line only; 1001 is the lowest.
            Good((breaks, 1001, true));
            Bad("broken at or below the recover line", (breaks, 1000, true));
            // At the broken line a unit that can break is broken; one that cannot never is.
            Good((breaks, 6999, false)); Good((breaks, 7000, true)); Good((steady, 7000, false));
            Bad("broken flag disagree", (breaks, 7000, false));
            Bad("broken flag disagree", (steady, 7000, true));
            Bad("broken flag disagree", (steady, 5000, true));
            // A save and a load give the same hash.
            Good((low, 1500, false), (high, 9000, high == breaks));
            var hash = new StateHash(); p.WriteState(hash);
            var writer = new SimStateWriter(); p.SaveState(writer);
            var q = new Pressure(w.Combat, new PressureRules(3000, 2000, 3000, 1000, 12, 7000, 1000, 2000, 0, 300));
            q.LoadState(new SimStateReader(writer.ToArray()));
            var again = new StateHash(); q.WriteState(again);
            Assert.That(again.Value, Is.EqualTo(hash.Value));
            // The broken flag is part of the hash: the same number, broken and not.
            Good((breaks, 5000, true));
            var flagged = new StateHash(); p.WriteState(flagged);
            Good((breaks, 5000, false));
            var plain = new StateHash(); p.WriteState(plain);
            Assert.That(flagged.Value, Is.Not.EqualTo(plain.Value));
            var empty = new StateHash(); new Pressure(w.Combat, new PressureRules(3000, 2000, 3000, 1000, 12, 7000, 1000, 2000, 0, 300)).WriteState(empty);
            Assert.That(empty.Value, Is.Not.EqualTo(hash.Value));
        }

        // ---- Content ----

        [Test]
        public void Content_FieldsAreReadAllOrNone()
        {
            Assert.That(Pack("{}").Section<AiRules>().Pressure, Is.Null);
            Assert.That(Pack(null).Section<AiRules>().Pressure, Is.Null);
            PressureRules? read = Pack(Json(Values)).Section<AiRules>().Pressure;
            Assert.That(read, Is.Not.Null);
            PressureRules r = read.Value;
            Assert.That(new[] { r.DeathRadiusMm, r.DeathShock, r.NearRadiusMm, r.NearShock, r.DecayPermille, r.BrokenAt, r.RecoverAt, r.PinnedAt, r.SpreadPermille, r.BreakSharePermille },
                Is.EqualTo(Values));
            // The system is in the battle only when the fields are written; with hold or with bound.
            Assert.That(Field(null).System<Pressure>(), Is.Null);
            Assert.That(Field("{ " + BoundAi + " }").System<Pressure>(), Is.Null);
            Assert.That(Field(Json(Values)).System<Pressure>(), Is.Not.Null);
            Assert.That(Field(S3).System<Pressure>(), Is.Not.Null);
            // Range edges are accepted.
            Assert.That(Pack(Json(new[] { 0, 0, 0, 0, 1, 1, 0, 1, 0, 0 })).Section<AiRules>().Pressure, Is.Not.Null);
            Assert.That(Pack(Json(new[] { 512000, 10000, 512000, 10000, 1000, 10000, 9999, 10000, 1000, 1000 })).Section<AiRules>().Pressure, Is.Not.Null);
        }

        [Test]
        public void Content_AnyFieldWrittenNeedsAllTen()
        {
            for (int skip = 0; skip < Fields.Length; skip++)
            {
                var e = Assert.Throws<ContentException>(() => Pack(Json(Values, null, skip)));
                Assert.That(e.Message, Is.EqualTo("content pack 's3-test' layer 's3-test' ai is missing field '" + Fields[skip] + "'"));
            }
            // One field alone: the first of the others is reported missing.
            for (int only = 0; only < Fields.Length; only++)
            {
                var e = Assert.Throws<ContentException>(() => Pack("{ \"" + Fields[only] + "\": " + Values[only] + " }"));
                Assert.That(e.Message, Is.EqualTo("content pack 's3-test' layer 's3-test' ai is missing field '" + Fields[only == 0 ? 1 : 0] + "'"));
            }
        }

        [TestCase(0, -1)] [TestCase(0, 512001)]
        [TestCase(1, -1)] [TestCase(1, 10001)]
        [TestCase(2, -1)] [TestCase(2, 512001)]
        [TestCase(3, -1)] [TestCase(3, 10001)]
        [TestCase(4, 0)] [TestCase(4, 1001)]
        [TestCase(5, 0)] [TestCase(5, 10001)]
        [TestCase(6, -1)] [TestCase(6, 10001)]
        [TestCase(7, 0)] [TestCase(7, 10001)]
        [TestCase(8, -1)] [TestCase(8, 1001)]
        [TestCase(9, -1)] [TestCase(9, 1001)]
        public void Content_OutOfRangeNamesTheField(int index, int value)
        {
            var e = Assert.Throws<ContentException>(() => Pack(Json(With(index, value))));
            Assert.That(e.Message, Does.StartWith("content pack 's3-test' layer 's3-test' ai field '" + Fields[index] + "': "));
        }

        [TestCase(7000)]
        [TestCase(7001)]
        public void Content_RecoverMustBeBelowBroken(int recoverAt)
        {
            var e = Assert.Throws<ContentException>(() => Pack(Json(With(6, recoverAt))));
            Assert.That(e.Message, Is.EqualTo("content pack 's3-test' layer 's3-test' ai field 'pressureRecoverAt': must be below pressureBrokenAt (7000)"));
            Assert.That(Pack(Json(With(6, 6999))).Section<AiRules>().Pressure.Value.RecoverAt, Is.EqualTo(6999));
        }

        // The view tells who is pinned and who is broken; without the system everybody reads 0.
        [Test]
        public void View_CarriesTheNerve()
        {
            M6World w = Field(Inject(300));
            Pressure p = w.System<Pressure>();
            p.Advance(new[] { Fall(w, 10) });
            w.Session.StepOnce();
            int pinned = 0, broken = 0;
            for (ulong key = 1; key <= Attackers + Defenders; key++)
            {
                Assert.That(w.Session.Current.TryGet(key, out ViewEntity seen), Is.True, "key " + key);
                Assert.That(seen.Nerve, Is.EqualTo((int)p.Level(Id(w, key))), "key " + key);
                if (seen.Nerve == 1) pinned++; else if (seen.Nerve == 2) broken++;
            }
            Assert.That(pinned + broken, Is.EqualTo(Pressed.Length));
            M6World plain = Field(null);
            plain.Session.StepOnce();
            for (ulong key = 1; key <= Attackers + Defenders; key++)
            {
                Assert.That(plain.Session.Current.TryGet(key, out ViewEntity seen), Is.True);
                Assert.That(seen.Nerve, Is.EqualTo(0));
            }
        }

#if !UNITY_EDITOR && !UNITY_5_3_OR_NEWER
        // Shots land near the attackers every tick: entries come and go, and a step must not allocate.
        [Test]
        public void Battle_StepAllocatesZero()
        {
            string ai = Json(new[] { 3000, 2000, 3000, 1000, 12, 7000, 1000, 2000, 500, 300 }, BoundAi);
            M6World warm = Field(ai, attackersX: 80);
            warm.Step(300);
            M6World w = Field(ai, attackersX: 80);
            w.Step(300);
            Assert.That(Entries(w.System<Pressure>()), Is.GreaterThan(0), "somebody must be pressed");
            AllocationMeter.Stabilize();
            long total = 0;
            for (int i = 0; i < 150; i++)
            {
                long before = GC.GetAllocatedBytesForCurrentThread();
                w.Sim.Step(default);
                total += GC.GetAllocatedBytesForCurrentThread() - before;
            }
            Assert.That(total, Is.EqualTo(0L));
        }
#endif
    }
}
