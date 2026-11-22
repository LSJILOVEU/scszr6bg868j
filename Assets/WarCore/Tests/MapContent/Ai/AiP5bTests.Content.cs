using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Content;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // AI-P5b, the content fields (the four temper fields all or none, the three regroup fields all or none, both only
    // with the seven squad fields), what a battle is with and without them, and what the view is told.
    public sealed partial class AiP5bTests
    {
        private const string Head = "content pack 'p5b-test' layer 'p5b-test' ai ";
        private static readonly int[] Temper = { 300, 250, 600, 8000 }, Regroup = { 500, 750, 1200 };
        private static string Without(string[] names, int[] values, int skip)
        {
            var parts = new List<string>();
            for (int i = 0; i < names.Length; i++) if (i != skip) parts.Add("\"" + names[i] + "\": " + values[i]);
            return string.Join(", ", parts);
        }
        private static SquadRules Read(string ai) => Pack(ai).Section<AiRules>().Squads.Value;
        private static int[] Numbers(SquadTemper t) => new[] { t.RashPermille, t.SteadyPermille, t.ShakenPermille, t.RashMinRangeMm };
        private static int[] Numbers(SquadRegroup r) => new[] { r.BelowPermille, r.UntilPermille, r.MaxTicks };

        // 8. Each group is read all or none, in its own right; any one field written makes the others of its group required.
        [Test]
        public void Content_TheTwoGroupsAreReadAllOrNone()
        {
            SquadRules bare = Read(Json(Seven));
            Assert.That(bare.Temper, Is.Null);
            Assert.That(bare.Regroup, Is.Null);
            SquadRules temper = Read(Json(Seven, Temper));
            Assert.That(Numbers(temper.Temper.Value), Is.EqualTo(Temper));
            Assert.That(temper.Regroup, Is.Null);
            SquadRules regroup = Read(Json(Seven, null, Regroup));
            Assert.That(regroup.Temper, Is.Null);
            Assert.That(Numbers(regroup.Regroup.Value), Is.EqualTo(Regroup));
            SquadRules both = Read(Json(Seven, Temper, Regroup, "\"holdSharePermille\": 300"));
            Assert.That(Numbers(both.Temper.Value), Is.EqualTo(Temper));
            Assert.That(Numbers(both.Regroup.Value), Is.EqualTo(Regroup));
            Assert.That(new[] { both.MinSize, both.MaxSize, both.SkirmisherPermille, both.RecruitRadiusMm, both.FormTicks, both.SpreadMinMm, both.SpreadMaxMm }, Is.EqualTo(Seven));

            for (int skip = 0; skip < 4; skip++)
            {
                var e = Assert.Throws<ContentException>(() => Pack("{ " + Join(Group(Fields, Seven), Without(TemperFields, Temper, skip), Group(RegroupFields, Regroup)) + " }"));
                Assert.That(e.Message, Is.EqualTo(Head + "is missing field '" + TemperFields[skip] + "'"));
            }
            for (int skip = 0; skip < 3; skip++)
            {
                var e = Assert.Throws<ContentException>(() => Pack("{ " + Join(Group(Fields, Seven), Group(TemperFields, Temper), Without(RegroupFields, Regroup, skip)) + " }"));
                Assert.That(e.Message, Is.EqualTo(Head + "is missing field '" + RegroupFields[skip] + "'"));
            }
            for (int only = 0; only < 4; only++)
            {
                var e = Assert.Throws<ContentException>(() => Pack("{ " + Join(Group(Fields, Seven), "\"" + TemperFields[only] + "\": " + Temper[only]) + " }"));
                Assert.That(e.Message, Is.EqualTo(Head + "is missing field '" + TemperFields[only == 0 ? 1 : 0] + "'"));
            }
            for (int only = 0; only < 3; only++)
            {
                var e = Assert.Throws<ContentException>(() => Pack("{ " + Join(Group(Fields, Seven), "\"" + RegroupFields[only] + "\": " + Regroup[only]) + " }"));
                Assert.That(e.Message, Is.EqualTo(Head + "is missing field '" + RegroupFields[only == 0 ? 1 : 0] + "'"));
            }
        }

        // 8. The bounds of each field: one beyond names the field and its range, the edges are read. The numbers around the field under
        // test are 0 (tempers) and 1..1000 (regrouping), so the pair rules hold at both edges.
        [TestCase(0, 0, 1000)]
        [TestCase(1, 0, 1000)]
        [TestCase(2, 0, 1000)]
        [TestCase(3, 0, 512000)]
        public void Content_EachTemperFieldHasItsBounds(int index, int least, int most)
        {
            foreach (int beyond in new[] { least - 1, most + 1 })
            {
                int[] values = { 0, 0, 0, 0 }; values[index] = beyond;
                var e = Assert.Throws<ContentException>(() => Pack(Json(Seven, values)));
                Assert.That(e.Message, Is.EqualTo(Head + "field '" + TemperFields[index] + "': out of range [" + least + ", " + most + "]"), beyond.ToString());
            }
            foreach (int edge in new[] { least, most })
            {
                int[] values = { 0, 0, 0, 0 }; values[index] = edge;
                Assert.That(Numbers(Read(Json(Seven, values)).Temper.Value)[index], Is.EqualTo(edge));
            }
        }

        [TestCase(0, 1, 1000)]
        [TestCase(1, 1, 1000)]
        [TestCase(2, 1, 100000)]
        public void Content_EachRegroupFieldHasItsBounds(int index, int least, int most)
        {
            foreach (int beyond in new[] { least - 1, most + 1 })
            {
                int[] values = { 1, 1000, 1 }; values[index] = beyond;
                var e = Assert.Throws<ContentException>(() => Pack(Json(Seven, null, values)));
                Assert.That(e.Message, Is.EqualTo(Head + "field '" + RegroupFields[index] + "': out of range [" + least + ", " + most + "]"), beyond.ToString());
            }
            foreach (int edge in new[] { least, most })
            {
                int[] values = { 1, 1000, 1 }; values[index] = edge;
                if (index == 0) values[1] = 1000;
                if (index == 1) values[0] = 1;
                Assert.That(Numbers(Read(Json(Seven, null, values)).Regroup.Value)[index], Is.EqualTo(edge));
            }
        }

        // 8. The pair rules: the rash and the steady share together are at most everything, and the Until line is not
        // below the Below line.
        [Test]
        public void Content_TheSharesFitAndUntilIsAtLeastBelow()
        {
            SquadRules met = default;
            Assert.DoesNotThrow(() => met = Read(Json(Seven, Rules(400, 600, 0, 0))));
            Assert.That(met.Temper.Value.SteadyPermille, Is.EqualTo(600));
            var e = Assert.Throws<ContentException>(() => Pack(Json(Seven, Rules(400, 601, 0, 0))));
            Assert.That(e.Message, Is.EqualTo(Head + "field 'squadSteadyPermille': squadRashPermille + squadSteadyPermille must be at most 1000, got 1001"));
            Assert.DoesNotThrow(() => met = Read(Json(Seven, null, Rules(500, 500, 1))));
            Assert.That(met.Regroup.Value.UntilPermille, Is.EqualTo(500));
            e = Assert.Throws<ContentException>(() => Pack(Json(Seven, null, Rules(500, 499, 1))));
            Assert.That(e.Message, Is.EqualTo(Head + "field 'squadRegroupUntilPermille': must be at least squadRegroupBelowPermille (500)"));
        }

        // 8. Tempers and regrouping are for squads: either group without the seven squad fields is refused, naming the
        // first field of the group that was written.
        [Test]
        public void Content_BothGroupsNeedTheSquadFields()
        {
            (string ai, string field)[] cases =
            {
                ("{ " + Group(TemperFields, Temper) + " }", "squadRashPermille"),
                ("{ " + Group(RegroupFields, Regroup) + " }", "squadRegroupBelowPermille"),
                ("{ " + Join(Group(RegroupFields, Regroup), Group(TemperFields, Temper)) + " }", "squadRashPermille"),
                ("{ \"squadShakenPermille\": 600 }", "squadShakenPermille"),
                ("{ \"squadRegroupMaxTicks\": 1200, \"holdSharePermille\": 300 }", "squadRegroupMaxTicks"),
            };
            foreach ((string ai, string field) in cases)
            {
                var e = Assert.Throws<ContentException>(() => Pack(ai), ai);
                Assert.That(e.Message, Is.EqualTo(Head + "field '" + field + "': needs the squad fields (squadMinSize ...)"));
            }
        }

        // The rules themselves refuse what the content reader refuses.
        [Test]
        public void TheTemperAndRegroupRulesRefuseWhatCannotBe()
        {
            Assert.DoesNotThrow(() => new SquadTemper(0, 0, 0, 0));
            Assert.DoesNotThrow(() => new SquadTemper(1000, 0, 1000, 1));
            Assert.DoesNotThrow(() => new SquadTemper(0, 1000, 0, 0));
            Assert.DoesNotThrow(() => new SquadTemper(400, 600, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SquadTemper(-1, 0, 0, 0));
            Assert.That(Assert.Throws<ArgumentOutOfRangeException>(() => new SquadTemper(1001, 0, 0, 0)).ParamName, Is.EqualTo("rashPermille"));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SquadTemper(0, -1, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SquadTemper(400, 601, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SquadTemper(0, 0, -1, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SquadTemper(0, 0, 1001, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SquadTemper(0, 0, 0, -1));
            Assert.DoesNotThrow(() => new SquadRegroup(1, 1, 1));
            Assert.DoesNotThrow(() => new SquadRegroup(1000, 1000, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SquadRegroup(0, 1, 1));
            Assert.That(Assert.Throws<ArgumentOutOfRangeException>(() => new SquadRegroup(1001, 1001, 1)).ParamName, Is.EqualTo("belowPermille"));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SquadRegroup(2, 1, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SquadRegroup(1, 1001, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SquadRegroup(1, 1, 0));
        }

        private static ulong Digest(M6World w)
        { var writer = new SimStateWriter(); w.System<SimpleAiSystem>().SaveState(writer); return new SimStateReader(writer.ToArray()).UInt64(); }
        private static object Part(M6World w, string name) =>
            typeof(SimpleAiSystem).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(w.System<SimpleAiSystem>());
        private static UnitSpawn[] Pair() => new[] { Man(1, 1, 20000), Man(2, 1, 21000), Man(90, 2, 150000, 50000) };
        private const string BoundAi = "\"engage\": \"bound\", \"boundMoveTicks\": 60, \"boundFireTicks\": 90, \"boundSharePermille\": 700, \"boundMinRangeMm\": 12000";
        // The digest of a battle with squads and neither group: the dispatch wrapper's tag over the digest without squads
        // and the seven numbers, then the placement wrapper's tag.
        private static ulong SquadsOnly(ulong without)
        {
            var hash = new StateHash(); hash.Add("battle.ai.squads.v1"); hash.Add(without);
            foreach (int value in Seven) hash.Add(value);
            return Tag("battle.ai.squad-placement.v1", hash.Value);
        }

        // 1. With the seven squad fields and neither group nothing more is added: the engage part is the battle's own, the
        // policy digest and the saved state are what they were before tempers and regrouping existed, and the battle has
        // the same hash on every tick as one whose rules never heard of the two groups. With either group the engage part
        // is wrapped.
        [Test]
        public void World_WithTheSquadFieldsAloneNothingMoreIsAdded()
        {
            M6World hold = Field(Json(Seven), Pair(), think: 10), bound = Field(Json(Seven, more: BoundAi), Pair(), think: 10);
            Assert.That(Part(hold, "engage"), Is.InstanceOf<HoldEngage>());
            Assert.That(Part(bound, "engage"), Is.InstanceOf<BoundEngage>());
            Assert.That(Digest(hold), Is.EqualTo(SquadsOnly(Digest(Field(null, Pair(), think: 10)))));
            Assert.That(Sq(hold).StateVersion, Is.EqualTo(1));
            Assert.That(Sq(hold).Rules.Temper, Is.Null);
            Assert.That(Sq(hold).Rules.Regroup, Is.Null);
            To(hold, Form);
            Assert.That(Band(hold, 1), Is.Not.Zero);
            var writer = new SimStateWriter();
            foreach (int value in Seven) writer.Int32(value);
            ulong leader = Stable(Band(hold, 1)), member = Stable(Band(hold, 1) == 1 ? 2UL : 1UL);
            writer.Int32(1); writer.UInt64(leader); writer.Int32(Sq(hold).SizeOf(new StableId(leader)));
            writer.Int32(1); writer.UInt64(member); writer.UInt64(leader);
            Assert.That(Save(hold), Is.EqualTo(writer.ToArray()));
            Assert.That(Mood(hold, 1), Is.EqualTo(SquadMood.None));
            Assert.That(Rally(hold, 1), Is.EqualTo(SquadRally.None));

            foreach (string more in new[] { null, BoundAi })
                foreach ((int[] t, int[] r) in new (int[], int[])[] { (Temper, null), (null, Regroup), (Temper, Regroup) })
                {
                    M6World on = Field(Json(Seven, t, r, more), Pair(), think: 10);
                    Assert.That(Part(on, "engage"), Is.Not.InstanceOf<HoldEngage>().And.Not.InstanceOf<BoundEngage>());
                    Assert.That(AssemblyWorlds.Systems(on.Sim), Is.EqualTo(AssemblyWorlds.Systems(hold.Sim)), "no system is added");
                    Assert.That(Sq(on).StateVersion, Is.EqualTo(r == null ? 1 : 2));
                }
        }

        // 8. The policy digest with both groups, written out: the dispatch wrapper's (the seven numbers, then the regroup
        // tag and its three), the placement wrapper's tag, the engage wrapper's (its tag, the four temper numbers, 1 for
        // regrouping). Each of the seven new numbers makes another battle, and a snapshot of one is refused by the other.
        [Test]
        public void World_EachOfTheSevenNewNumbersIsInThePolicy()
        {
            ulong without = Digest(Field(null, Pair()));
            ulong Whole(int[] t, int[] r)
            {
                var dispatch = new StateHash(); dispatch.Add("battle.ai.squads.v1"); dispatch.Add(without);
                foreach (int value in Seven) dispatch.Add(value);
                if (r != null) { dispatch.Add("battle.ai.squad-regroup.v1"); foreach (int value in r) dispatch.Add(value); }
                var engage = new StateHash(); engage.Add("battle.ai.squad-engage.v1"); engage.Add(Tag("battle.ai.squad-placement.v1", dispatch.Value));
                foreach (int value in t ?? new[] { 0, 0, 0, 0 }) engage.Add(value);
                engage.Add(r != null ? 1 : 0);
                return engage.Value;
            }
            M6World world = Field(Json(Seven, Temper, Regroup), Pair());
            Assert.That(Digest(world), Is.EqualTo(Whole(Temper, Regroup)));
            Assert.That(Digest(Field(Json(Seven, Temper), Pair())), Is.EqualTo(Whole(Temper, null)));
            Assert.That(Digest(Field(Json(Seven, null, Regroup), Pair())), Is.EqualTo(Whole(null, Regroup)));
            var seen = new HashSet<ulong> { SquadsOnly(without), Whole(Temper, Regroup), Whole(Temper, null), Whole(null, Regroup) };
            Assert.That(seen.Count, Is.EqualTo(4));
            byte[] snapshot = world.Sim.SaveRetainedSnapshot();
            Assert.DoesNotThrow(() => Field(Json(Seven, Temper, Regroup), Pair()).Sim.RestoreRetainedSnapshot(snapshot));
            for (int i = 0; i < 7; i++)
            {
                int[] t = (int[])Temper.Clone(), r = (int[])Regroup.Clone();
                if (i < 4) t[i]++; else r[i - 4]++;
                string name = i < 4 ? TemperFields[i] : RegroupFields[i - 4];
                M6World changed = Field(Json(Seven, t, r), Pair());
                Assert.That(Digest(changed), Is.EqualTo(Whole(t, r)), name);
                Assert.That(seen.Add(Digest(changed)), name);
                Assert.Catch(() => changed.Sim.RestoreRetainedSnapshot(snapshot), name);
            }
            // A battle with tempers alone saves the squads as a battle without them does, and is still another battle.
            M6World tempered = Field(Json(Seven, Temper), Pair()), plain = Field(Json(Seven), Pair());
            Assert.That(Save(tempered), Is.EqualTo(Save(plain)));
            Assert.Catch(() => plain.Sim.RestoreRetainedSnapshot(tempered.Sim.SaveRetainedSnapshot()));
        }

        private static ViewEntity Seen(M6World w, ulong key)
        {
            Assert.That(w.Session.Current.TryGet(key, out ViewEntity seen), Is.True, "key " + key);
            return seen;
        }

        // 7. The view is told each man's squad's mood (1 normal, 2 rash, 3 steady) and regroup mark (1 regrouping, 2 idle);
        // 0 for a skirmisher, a dead man and a battle without the fields. An interpolated frame keeps the newer frame's.
        [Test]
        public void TheViewShowsEachSquadsMoodAndMark()
        {
            int[] seven = { 2, 2, 0, 10000, 15, 0, 0 };
            ulong[] k = ByElect(1, 2, 3);
            var units = new[] { Man(k[0], 1, 20000), Man(k[1], 1, 21000), Man(k[2], 1, 22000), Man(7, 1, 60000), Man(90, 2, 150000, 50000) };
            int t = T(k[0]);
            // The leader's number right below the rash line; whole on tick 15, one member falls on tick 20: strength 666, the
            // number moves up by 334 * 300 / 1000 = 100 and the squad is normal; below the line of 700 it regroups from tick 30.
            Assert.That(t, Is.LessThan(900));
            M6World w = Field(Json(seven, Rules(t + 1, 0, 300, 8000), Rules(700, 1000, 100000)), units, x => Kill(x, 20, k[2]));
            while (w.Session.Tick < Form - 1) w.Session.StepOnce();
            foreach (ulong key in k) { Assert.That(Seen(w, key).Mood, Is.Zero); Assert.That(Seen(w, key).Regroup, Is.Zero); }
            FrameView before = w.Session.Current;
            w.Session.StepOnce();
            foreach (ulong key in k) { Assert.That(Seen(w, key).Mood, Is.EqualTo(2), "key " + key); Assert.That(Seen(w, key).Regroup, Is.Zero); }
            Assert.That(Seen(w, 7).Mood, Is.Zero, "a skirmisher");
            Assert.That(FrameView.Interpolate(before, w.Session.Current, 0.5f).TryGet(k[1], out ViewEntity half), Is.True);
            Assert.That(half.Mood, Is.EqualTo(2));
            while (w.Session.Tick < 21) w.Session.StepOnce();
            Assert.That(Seen(w, k[2]).Alive, Is.False);
            Assert.That(Seen(w, k[2]).Mood, Is.Zero, "a dead man");
            Assert.That(Seen(w, k[1]).Mood, Is.EqualTo(1));
            Assert.That(Seen(w, k[1]).Regroup, Is.Zero);
            before = w.Session.Current;
            while (w.Session.Tick < 2 * Form) w.Session.StepOnce();
            foreach (ulong key in new[] { k[0], k[1] }) Assert.That(Seen(w, key).Regroup, Is.EqualTo(1), "key " + key);
            Assert.That(Seen(w, k[2]).Regroup, Is.Zero);
            Assert.That(Seen(w, 7).Regroup, Is.Zero);
            Assert.That(FrameView.Interpolate(before, w.Session.Current, 0.5f).TryGet(k[0], out half), Is.True);
            Assert.That(half.Regroup, Is.EqualTo(1));

            // A new squad that is short from the start shows 2; steady shows 3.
            var two = new[] { Man(1, 1, 20000), Man(2, 1, 21000), Man(90, 2, 150000, 50000) };
            M6World idle = Field(Json(Rules(4, 4, 0, 10000, 15, 0, 0), Rules(0, 1000, 0, 0), Rules(700, 1000, 100000)), two);
            while (idle.Session.Tick < Form) idle.Session.StepOnce();
            Assert.That(Seen(idle, 1).Regroup, Is.EqualTo(2));
            Assert.That(Seen(idle, 2).Mood, Is.EqualTo(3));
            M6World plain = Field(Json(seven), units);
            while (plain.Session.Tick < Form) plain.Session.StepOnce();
            foreach (ulong key in k) { Assert.That(Seen(plain, key).Mood, Is.Zero); Assert.That(Seen(plain, key).Regroup, Is.Zero); }
            Assert.That(new ViewEntity(7, 0, 1f, 2f, 3f).Mood, Is.Zero);
            Assert.That(new ViewEntity(7, 0, 1f, 2f, 3f).Regroup, Is.Zero);
            Assert.That(new ViewEntity(7, 0, 1f, 2f, 3f, mood: 3, regroup: 2).Mood, Is.EqualTo(3));
            Assert.That(new ViewEntity(7, 0, 1f, 2f, 3f, mood: 3, regroup: 2).Regroup, Is.EqualTo(2));
        }
    }
}
