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
    // AI-P5a, the content fields (ai.squad*, all seven or none) and what a battle is with and without them.
    public sealed partial class AiP5aTests
    {
        private const string Head = "content pack 'p5a-test' layer 'p5a-test' ai ";
        private static ulong Digest(M6World w)
        { var writer = new SimStateWriter(); w.System<SimpleAiSystem>().SaveState(writer); return new SimStateReader(writer.ToArray()).UInt64(); }
        private static object Part(M6World w, string name) =>
            typeof(SimpleAiSystem).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(w.System<SimpleAiSystem>());
        private static UnitSpawn[] Pair() => new[] { Man(1, 1, 20), Man(2, 1, 21), Man(90, 2, 150, 50) };

        // 5. All seven or none; any one written makes the other six required.
        [Test]
        public void Content_SquadFieldsAreReadAllOrNone()
        {
            foreach (string none in new[] { null, "{}", "{ \"holdSharePermille\": 300 }" })
                Assert.That(Pack(none).Section<AiRules>().Squads, Is.Null, none);
            int[] values = { 2, 7, 250, 31000, 12, 1500, 6000 };
            SquadRules read = Pack(Json(values, "\"holdSharePermille\": 300")).Section<AiRules>().Squads.Value;
            Assert.That(new[] { read.MinSize, read.MaxSize, read.SkirmisherPermille, read.RecruitRadiusMm, read.FormTicks, read.SpreadMinMm, read.SpreadMaxMm }, Is.EqualTo(values));
            for (int skip = 0; skip < Fields.Length; skip++)
            {
                var e = Assert.Throws<ContentException>(() => Pack(Json(values, null, skip)));
                Assert.That(e.Message, Is.EqualTo(Head + "is missing field '" + Fields[skip] + "'"));
            }
            for (int only = 0; only < Fields.Length; only++)
            {
                var e = Assert.Throws<ContentException>(() => Pack("{ \"" + Fields[only] + "\": " + values[only] + " }"));
                Assert.That(e.Message, Is.EqualTo(Head + "is missing field '" + Fields[only == 0 ? 1 : 0] + "'"));
            }
        }

        // 5. The bounds of each field: the edges are read, one beyond names the field.
        [TestCase(Min, 1, 64)]
        [TestCase(Max, 1, 64)]
        [TestCase(Skirmishers, 0, 1000)]
        [TestCase(Radius, 1, 512000)]
        [TestCase(FormTicks, 1, 100000)]
        [TestCase(SpreadMin, 0, 50000)]
        [TestCase(SpreadMax, 0, 50000)]
        public void Content_EachSquadFieldHasItsBounds(int index, int least, int most)
        {
            // Around the field under test: sizes 1..64 and spreads 0..50000 keep "max at least min" true at both edges.
            int[] wide = With(Min, 1, Max, 64, SpreadMin, 0, SpreadMax, 50000);
            foreach (int edge in new[] { least, most })
            {
                int[] values = (int[])wide.Clone(); values[index] = edge;
                if (index == Min) values[Max] = Math.Max(edge, values[Max]);
                if (index == Max) values[Min] = Math.Min(edge, values[Min]);
                if (index == SpreadMin) values[SpreadMax] = Math.Max(edge, values[SpreadMax]);
                if (index == SpreadMax) values[SpreadMin] = Math.Min(edge, values[SpreadMin]);
                SquadRules read = Pack(Json(values)).Section<AiRules>().Squads.Value;
                Assert.That(new[] { read.MinSize, read.MaxSize, read.SkirmisherPermille, read.RecruitRadiusMm, read.FormTicks, read.SpreadMinMm, read.SpreadMaxMm }[index], Is.EqualTo(edge));
            }
            foreach (int beyond in new[] { least - 1, most + 1 })
            {
                int[] values = (int[])wide.Clone(); values[index] = beyond;
                var e = Assert.Throws<ContentException>(() => Pack(Json(values)));
                Assert.That(e.Message, Does.StartWith(Head + "field '" + Fields[index] + "': "), beyond.ToString());
            }
        }

        // 5. The larger of a pair may equal the smaller, not be below it.
        [Test]
        public void Content_MaxIsAtLeastMin()
        {
            Assert.That(Pack(Json(With(Min, 5, Max, 5))).Section<AiRules>().Squads.Value.MaxSize, Is.EqualTo(5));
            var e = Assert.Throws<ContentException>(() => Pack(Json(With(Min, 5, Max, 4))));
            Assert.That(e.Message, Is.EqualTo(Head + "field 'squadMaxSize': must be at least squadMinSize (5)"));
            Assert.That(Pack(Json(With(SpreadMin, 700, SpreadMax, 700))).Section<AiRules>().Squads.Value.SpreadMaxMm, Is.EqualTo(700));
            e = Assert.Throws<ContentException>(() => Pack(Json(With(SpreadMin, 700, SpreadMax, 699))));
            Assert.That(e.Message, Is.EqualTo(Head + "field 'squadSpreadMaxMm': must be at least squadSpreadMinMm (700)"));
        }

        // The rules themselves refuse what the content reader refuses.
        [Test]
        public void SquadRulesRefuseWhatCannotBe()
        {
            Assert.DoesNotThrow(() => new SquadRules(1, 1, 0, 1, 1, 0, 0));
            Assert.DoesNotThrow(() => new SquadRules(1, 1, 1000, 1, 1, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SquadRules(0, 1, 0, 1, 1, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SquadRules(2, 1, 0, 1, 1, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SquadRules(1, 1, -1, 1, 1, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SquadRules(1, 1, 1001, 1, 1, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SquadRules(1, 1, 0, 0, 1, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SquadRules(1, 1, 0, 1, 0, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SquadRules(1, 1, 0, 1, 1, -1, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SquadRules(1, 1, 0, 1, 1, 1, 0));
        }

        // 1. Without the squad fields nothing is added: no squads system, the battle's own parts unwrapped, the same policy
        // digest and the same hash on every tick as a battle with no AI rules at all.
        [Test]
        public void World_WithoutTheSquadFieldsNothingIsAdded()
        {
            M6World bare = Field(null, Pair(), think: 10), empty = Field("{}", Pair(), think: 10), on = Field(Json(Values), Pair(), think: 10);
            foreach (M6World off in new[] { bare, empty })
            {
                Assert.That(off.System<Squads>(), Is.Null);
                Assert.That(AssemblyWorlds.Systems(off.Sim), Does.Not.Contain("squads"));
                Assert.That(Part(off, "dispatch"), Is.InstanceOf<NearestDispatch>());
                Assert.That(Part(off, "placement"), Is.InstanceOf<NearestSlotPlacement>());
                Assert.That(Part(off, "waypoints"), Is.Null);
            }
            Assert.That(Digest(empty), Is.EqualTo(Digest(bare)));
            Assert.That(AssemblyWorlds.Systems(on.Sim), Does.Contain("battle.ai-squads|" + (int)SimSystemPhase.Requests + "|3;"));
            Assert.That(AssemblyWorlds.Systems(on.Sim).Replace("battle.ai-squads|" + (int)SimSystemPhase.Requests + "|3;", ""), Is.EqualTo(AssemblyWorlds.Systems(bare.Sim)));
            Assert.That(Part(on, "dispatch"), Is.Not.InstanceOf<NearestDispatch>());
            Assert.That(Part(on, "placement"), Is.Not.InstanceOf<NearestSlotPlacement>());
            for (int tick = 0; tick < 60; tick++)
            {
                Assert.That(empty.Hash, Is.EqualTo(bare.Hash), "tick " + tick);
                bare.Step(); empty.Step();
            }
        }

        // 5. The battle's policy digest with squads: the digest without, then the dispatch wrapper's tag and seven numbers,
        // then the placement wrapper's tag. So each of the seven numbers makes another battle, and a snapshot of one is
        // refused by the other.
        [Test]
        public void World_EachOfTheSevenNumbersIsInThePolicy()
        {
            int[] values = With(SpreadMax, 2);
            ulong without = Digest(Field(null, Pair()));
            var hash = new StateHash(); hash.Add("battle.ai.squads.v1"); hash.Add(without);
            foreach (int value in values) hash.Add(value);
            M6World world = Field(Json(values), Pair());
            Assert.That(Digest(world), Is.EqualTo(Tag("battle.ai.squad-placement.v1", hash.Value)));
            var seen = new HashSet<ulong> { without, Digest(world) };
            byte[] snapshot = world.Sim.SaveRetainedSnapshot();
            Assert.DoesNotThrow(() => Field(Json(values), Pair()).Sim.RestoreRetainedSnapshot(snapshot));
            (int index, int value)[] others = { (Min, 3), (Max, 5), (Skirmishers, 301), (Radius, 30001), (FormTicks, 16), (SpreadMin, 1), (SpreadMax, 1) };
            foreach ((int index, int value) in others)
            {
                int[] other = (int[])values.Clone(); other[index] = value;
                M6World changed = Field(Json(other), Pair());
                Assert.That(seen.Add(Digest(changed)), Fields[index]);
                Assert.Catch(() => changed.Sim.RestoreRetainedSnapshot(snapshot), Fields[index]);
            }
        }

        // On a map with floor plates squads are refused when the world is built, after the rules that were refused there
        // before them; the same rules build a world without plates.
        [Test]
        public void World_SquadsAreRefusedOnFloorPlates()
        {
            var units = new[] { Ml11aWorld.Walker(1, 30500, 31500), Ml11aWorld.Walker(2, 60500, 31500, 0, 2) };
            ControlPointDef[] points = { Ml11aWorld.PointUp(1, 41500, 32500) };
            Exception thrown = Assert.Catch(() => Ml11aWorld.World(units, points, ai: Pack(Json(Real))));
            if (thrown is TargetInvocationException wrapped) thrown = wrapped.InnerException;
            Assert.That(thrown, Is.InstanceOf<ContentException>());
            Assert.That(thrown.Message, Is.EqualTo(Head + "field 'squadMinSize': is not supported on a map with floor plates"));

            AiRules alone = Pack(Json(Real)).Section<AiRules>();
            Assert.That(Assert.Throws<ContentException>(() => ContentPolicies.Parts(alone, plates: true)).Message, Is.EqualTo(thrown.Message));
            Assert.DoesNotThrow(() => ContentPolicies.Parts(alone));
            Assert.DoesNotThrow(() => ContentPolicies.Parts(Pack("{ \"holdSharePermille\": 300 }").Section<AiRules>(), plates: true));
            AiRules both = Pack(Json(Real, Scatter)).Section<AiRules>();
            Assert.That(Assert.Throws<ContentException>(() => ContentPolicies.Parts(both, plates: true)).Message,
                Does.Contain("'placement'").And.Not.Contain("squadMinSize"));
        }
    }
}
