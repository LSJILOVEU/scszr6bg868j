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
    // RC2-3 acceptance 8 and 9: the five content fields of dispatch "commander" (all five or none, appendix 9 bounds), the
    // fields of the other dispatch, floor plates; the system's registration; the five numbers in the battle's policy digest.
    public sealed class CommanderContentTests
    {
        private const string Id = "commander-test";
        private const string Head = "content pack 'commander-test' layer 'commander-test' ai ";
        private static readonly string[] Fields =
            { "commanderMaxAttackTargets", "commanderEnemyWeight", "commanderDistanceWeightPerMetre", "commanderStickiness", "commanderThreatRadiusMm" };
        private static readonly int[] Values = { 3, 200, 1, 600, 30000 };

        private static ContentPack Pack(string ai) => ContentLoader.Load(new MemoryContentSource(new Dictionary<string, string>
        {
            [Id] = "{ \"id\": \"" + Id + "\", \"extends\": \"rwr\"" + (ai == null ? "" : ", \"ai\": " + ai) + " }"
        }, FileContentSource.Rwr()), Id);

        private static string Json(int[] values, int skip = -1, string extra = null)
        {
            var parts = new List<string> { "\"dispatch\": \"commander\"" };
            if (extra != null) parts.Add(extra);
            for (int i = 0; i < Fields.Length; i++) if (i != skip) parts.Add("\"" + Fields[i] + "\": " + values[i]);
            return "{ " + string.Join(", ", parts) + " }";
        }

        private static int[] Of(CommanderRules r) =>
            new[] { r.Quota.MaxAttackTargets, r.Quota.EnemyWeight, r.Quota.DistanceWeightPerMetre, r.Quota.Stickiness, r.ThreatRadiusMm };

        // A flat 120 x 40 m field with a point each, two unarmed men; the match never ends.
        private static M6World Field(string ai)
        {
            var points = new[] { new ControlPointDef(1, SimVec3.Metres(20, 0, 20), SimFix.FromInt(6), 1), new ControlPointDef(2, SimVec3.Metres(100, 0, 20), SimFix.FromInt(6), 2) };
            var units = new[] { M6World.U(1, 1, 10, 20, weapon: -1, speed: 4000), M6World.U(2, 2, 110, 20, weapon: -1, speed: 4000) };
            WorldRecipe recipe = M6World.RecipeOf(units, M6World.Pack(points, think: 30)).WithRuleSet(MatchMode.TestNeverEndsId);
            if (ai != null) recipe = recipe.WithPack(Pack(ai));
            return new M6World(recipe, WcMapWriter.Write(Wp23.Make(120, 40, 0, 4 * SimFix.Scale, Array.Empty<(int, int, int, int)>(), 2 * SimFix.Scale, null)));
        }

        private static ulong Digest(M6World w)
        { var writer = new SimStateWriter(); w.System<SimpleAiSystem>().SaveState(writer); return new SimStateReader(writer.ToArray()).UInt64(); }
        private static object Dispatch(M6World w) =>
            typeof(SimpleAiSystem).GetField("dispatch", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(w.System<SimpleAiSystem>());

        // The part reads its five numbers; the content part parts/commander and the playtest pack carry 3, 200, 1, 600, 30000.
        [Test]
        public void Content_TheFiveNumbersAreRead()
        {
            Assert.That(Of(Pack(Json(Values)).Section<AiRules>().Commander.Value), Is.EqualTo(Values));
            Assert.That(Of(Pack(Json(new[] { 5, 0, 7, 0, 1 })).Section<AiRules>().Commander.Value), Is.EqualTo(new[] { 5, 0, 7, 0, 1 }));
            ContentPack playtest = ContentLoader.Load(FileContentSource.Rwr(), "rwr-m7-playtest");
            Assert.That(playtest.Section<AiRules>().Dispatch, Is.EqualTo(ContentPolicies.CommanderDispatch));
            Assert.That(Of(playtest.Section<AiRules>().Commander.Value), Is.EqualTo(Values));
            Assert.That(playtest.Section<AiRules>().HoldSharePermille, Is.EqualTo(0), "the playtest pack writes no hold share");
        }

        // Selected, the commander needs all five: each one left out, or all of them, is named.
        [Test]
        public void Content_TheCommanderNeedsAllFive()
        {
            for (int skip = 0; skip < Fields.Length; skip++)
            {
                var e = Assert.Throws<ContentException>(() => Pack(Json(Values, skip)));
                Assert.That(e.Message, Is.EqualTo(Head + "is missing field '" + Fields[skip] + "'"));
            }
            var none = Assert.Throws<ContentException>(() => Pack("{ \"dispatch\": \"commander\" }"));
            Assert.That(none.Message, Is.EqualTo(Head + "is missing field 'commanderMaxAttackTargets'"));
        }

        // Each field's bounds (appendix 9): both edges are read, one beyond names the field.
        [TestCase(0, 1, CommanderQuotaRules.MaxTargets)]
        [TestCase(1, 0, CommanderQuotaRules.MaxWeight)]
        [TestCase(2, 0, CommanderQuotaRules.MaxWeight)]
        [TestCase(3, 0, CommanderQuotaRules.MaxWeight)]
        [TestCase(4, 1, CommanderRules.MaxThreatRadiusMm)]
        public void Content_EachFieldHasItsBounds(int index, int least, int most)
        {
            foreach (int edge in new[] { least, most })
            {
                int[] values = (int[])Values.Clone(); values[index] = edge;
                Assert.That(Of(Pack(Json(values)).Section<AiRules>().Commander.Value)[index], Is.EqualTo(edge));
            }
            foreach (int beyond in new[] { least - 1, most + 1 })
            {
                int[] values = (int[])Values.Clone(); values[index] = beyond;
                var e = Assert.Throws<ContentException>(() => Pack(Json(values)));
                Assert.That(e.Message, Does.StartWith(Head + "field '" + Fields[index] + "': "), beyond.ToString());
            }
        }

        // The bounds are the ones appendix 9 names: 1..8, 0..10^6, and 1 mm up to the map side limit in millimetres; that
        // limit lies inside the coordinate limit the plan checks every point against.
        [Test]
        public void Rules_TheThreatRadiusIsBoundByTheMapSide()
        {
            Assert.That(CommanderRules.MaxThreatRadiusMm, Is.EqualTo(HeightField.MaxSide * 1000));
            Assert.That(CommanderRules.MaxThreatRadiusMm, Is.EqualTo(512000));
            Assert.That((long)HeightField.MaxSide, Is.LessThanOrEqualTo(CommanderQuota.CoordinateLimitMetres));
            var quota = new CommanderQuotaRules(3, 200, 1, 600);
            Assert.That(new CommanderRules(quota, 1).ThreatRadiusMm, Is.EqualTo(1));
            Assert.That(new CommanderRules(quota, CommanderRules.MaxThreatRadiusMm).ThreatRadiusMm, Is.EqualTo(CommanderRules.MaxThreatRadiusMm));
            Assert.Throws<ArgumentOutOfRangeException>(() => new CommanderRules(quota, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new CommanderRules(quota, CommanderRules.MaxThreatRadiusMm + 1));
        }

        // The fields of dispatch "nearest" beside the commander, and the commander's fields without it, name the part they
        // belong to.
        [TestCase("holdSharePermille", 300)]
        [TestCase("attackSpreadPoints", 2)]
        public void Content_NearestFieldsBesideTheCommanderAreRefused(string field, int value)
        {
            var e = Assert.Throws<ContentException>(() => Pack(Json(Values, -1, "\"" + field + "\": " + value)));
            Assert.That(e.Message, Is.EqualTo(Head + "field '" + field + "': belongs to dispatch 'nearest', but dispatch is 'commander'"));
        }

        [Test]
        public void Content_CommanderFieldsWithoutTheCommanderAreRefused()
        {
            for (int i = 0; i < Fields.Length; i++)
                foreach (string dispatch in new[] { "", "\"dispatch\": \"nearest\", " })
                {
                    var e = Assert.Throws<ContentException>(() => Pack("{ " + dispatch + "\"" + Fields[i] + "\": " + Values[i] + " }"));
                    Assert.That(e.Message, Is.EqualTo(Head + "field '" + Fields[i] + "': belongs to dispatch 'commander', but dispatch is 'nearest'"));
                }
            foreach (string none in new[] { null, "{}", "{ \"holdSharePermille\": 300 }", "{ \"dispatch\": \"nearest\" }" })
                Assert.That(Pack(none).Section<AiRules>().Commander, Is.Null, none);
        }

        // On a map with floor plates the commander is refused when the world is built (it counts enemies on the plane); the
        // same rules build a world without plates.
        [Test]
        public void World_TheCommanderIsRefusedOnFloorPlates()
        {
            var units = new[] { Ml11aWorld.Walker(1, 30500, 31500), Ml11aWorld.Walker(2, 60500, 31500, 0, 2) };
            ControlPointDef[] points = { Ml11aWorld.PointUp(1, 41500, 32500) };
            Exception thrown = Assert.Catch(() => Ml11aWorld.World(units, points, ai: Pack(Json(Values))));
            if (thrown is TargetInvocationException wrapped) thrown = wrapped.InnerException;
            Assert.That(thrown, Is.InstanceOf<ContentException>());
            Assert.That(thrown.Message, Is.EqualTo(Head + "field 'dispatch': is not supported on a map with floor plates"));
            AiRules rules = Pack(Json(Values)).Section<AiRules>();
            Assert.That(Assert.Throws<ContentException>(() => ContentPolicies.Parts(rules, plates: true)).Message, Is.EqualTo(thrown.Message));
            Assert.That(() => Field(Json(Values)), Throws.Nothing);
        }

        // Selected, the commander is the system "battle.ai-commander" in the queries phase at 41 (after the capture at 40,
        // before the match at 50), and its dispatch part is the battle's; nothing else in the system list changes.
        [Test]
        public void World_TheCommanderIsRegisteredAfterTheCapture()
        {
            M6World bare = Field(null), on = Field(Json(Values));
            Commander commander = on.System<Commander>();
            Assert.That(commander, Is.Not.Null);
            Assert.That(commander.Order, Is.EqualTo(41));
            Assert.That(commander.StateTypeId, Is.EqualTo("battle.ai-commander"));
            Assert.That(commander.StateVersion, Is.EqualTo(1));
            string entry = "battle.ai-commander|" + (int)SimSystemPhase.Queries + "|41";
            string systems = AssemblyWorlds.Systems(on.Sim);
            Assert.That(systems, Does.Contain("battle.capture|" + (int)SimSystemPhase.Queries + "|40;" + entry + ";battle.match|" + (int)SimSystemPhase.Queries + "|50"));
            Assert.That(systems.Replace(entry + ";", ""), Is.EqualTo(AssemblyWorlds.Systems(bare.Sim)));
            Assert.That(Dispatch(on), Is.SameAs(commander.Dispatch));
            Assert.That(commander.PointCount, Is.EqualTo(2));
            Assert.That(commander.TeamCount, Is.EqualTo(2));
            Assert.That(commander.Rules.ThreatRadiusMm, Is.EqualTo(30000));
        }

        // Without the commander nothing is added: no system, the nearest dispatch, the same policy digest and hash on every
        // tick as a battle without AI rules.
        [Test]
        public void World_WithoutTheCommanderNothingIsAdded()
        {
            M6World bare = Field(null), empty = Field("{}"), nearest = Field("{ \"dispatch\": \"nearest\" }");
            foreach (M6World off in new[] { bare, empty, nearest })
            {
                Assert.That(off.System<Commander>(), Is.Null);
                Assert.That(AssemblyWorlds.Systems(off.Sim), Does.Not.Contain("commander"));
                Assert.That(Dispatch(off), Is.InstanceOf<NearestDispatch>());
            }
            Assert.That(Digest(empty), Is.EqualTo(Digest(bare)));
            Assert.That(Digest(nearest), Is.EqualTo(Digest(bare)));
            for (int tick = 0; tick < 60; tick++)
            {
                Assert.That(empty.Hash, Is.EqualTo(bare.Hash), "tick " + tick);
                Assert.That(nearest.Hash, Is.EqualTo(bare.Hash), "tick " + tick);
                bare.Step(); empty.Step(); nearest.Step();
            }
        }

        // Acceptance 9: the digest with the commander is the digest without it, then the part's tag and five numbers; so
        // each of the five makes another battle, and a snapshot of one is refused by the other.
        [Test]
        public void World_EachOfTheFiveNumbersIsInThePolicy()
        {
            ulong without = Digest(Field(null));
            var hash = new StateHash(); hash.Add("battle.ai.commander.v1"); hash.Add(without);
            foreach (int value in Values) hash.Add(value);
            M6World world = Field(Json(Values));
            Assert.That(Digest(world), Is.EqualTo(hash.Value));
            var seen = new HashSet<ulong> { without, Digest(world) };
            byte[] snapshot = world.Sim.SaveRetainedSnapshot();
            Assert.DoesNotThrow(() => Field(Json(Values)).Sim.RestoreRetainedSnapshot(snapshot));
            int[] others = { 4, 201, 2, 601, 30001 };
            for (int i = 0; i < Fields.Length; i++)
            {
                int[] other = (int[])Values.Clone(); other[i] = others[i];
                M6World changed = Field(Json(other));
                Assert.That(seen.Add(Digest(changed)), Is.True, Fields[i]);
                Assert.Catch(() => changed.Sim.RestoreRetainedSnapshot(snapshot), Fields[i]);
            }
        }
    }
}
