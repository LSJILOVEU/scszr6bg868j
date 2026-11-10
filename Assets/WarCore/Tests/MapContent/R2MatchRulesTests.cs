using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Content;
using WarCore.Content.Rwr;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class R2MatchRulesTests
    {
        private const string DefaultRules = "[{\"id\":\"all-points\"},{\"id\":\"exhausted\"},{\"id\":\"time-limit\"}]";

        private static ContentPack Load(string fields, string match = "") => ContentLoader.Load(new FixedLocationSource(new[]
        {
            new KeyValuePair<string, string>("r2-pack", "{\"id\":\"r2-pack\",\"extends\":\"rwr-m6\",\"match\":{" + match + "}" + (fields.Length == 0 ? "" : "," + fields) + "}"),
        }), "r2-pack");

        private static MatchPack Pack(string fields) => RwrMatch.CreateM6(Load(fields));

        private sealed class Reader : IMatchReader
        {
            internal int OwnedOne, AliveOne = 5, AliveTwo = 5;
            public int PointCount => 3;
            public int PointsOwnedBy(int team) => team == 1 ? OwnedOne : 0;
            public void CountAlive(out int one, out int two) { one = AliveOne; two = AliveTwo; }
        }

        [Test]
        public void R2_DefaultEqualsExplicit()
        {
            var recipe = DemoRecipes.M6();
            var map = DemoRecipes.M6MapBytes();
            var omitted = new M6World(recipe.WithMatch(Pack("")), map);
            var explicitRules = new M6World(recipe.WithMatch(Pack("\"rules\":" + DefaultRules)), map);
            Assert.That(explicitRules.Hash, Is.EqualTo(omitted.Hash), "tick 0");
            Assert.That(explicitRules.Sim.SaveRetainedSnapshot(), Is.EqualTo(omitted.Sim.SaveRetainedSnapshot()));
            while (omitted.Match.Winner == 0 && omitted.Sim.Clock.Tick < recipe.Match.MaxTicks)
            {
                omitted.Step(); explicitRules.Step();
                Assert.That(explicitRules.Hash, Is.EqualTo(omitted.Hash), "tick " + omitted.Sim.Clock.Tick);
                if (omitted.Sim.Clock.Tick == 600)
                {
                    var saved = omitted.Sim.SaveRetainedSnapshot();
                    explicitRules.Sim.RestoreRetainedSnapshot(saved);
                    Assert.That(explicitRules.Sim.SaveRetainedSnapshot(), Is.EqualTo(saved));
                }
            }
            Assert.That((omitted.Match.Winner, omitted.Match.EndTick, omitted.Match.EndReason), Is.EqualTo((1, 1172L, MatchEndReason.AllPoints)));
            Assert.That((explicitRules.Match.Winner, explicitRules.Match.EndTick, explicitRules.Match.EndReason),
                Is.EqualTo((omitted.Match.Winner, omitted.Match.EndTick, omitted.Match.EndReason)));
        }

        [Test]
        public void R2_OnlyExhausted()
        {
            var content = Load("\"rules\":[{\"id\":\"exhausted\"}]", "\"captureTicks\":3,\"thinkInterval\":1000000,\"maxTicks\":4");
            var pack = content.BuildMatch(new[] { new ControlPointDef(7, M6World.P(20), SimFix.FromInt(6)) },
                new[] { new SpawnAnchor(1, M6World.P(6)), new SpawnAnchor(2, M6World.P(74)) });
            var world = new M6World(M6World.RecipeOf(new[] { M6World.U(1, 1), M6World.U(2, 2, 60) }, pack));
            world.Step(2);
            Assert.That(world.Capture.Points[0].Owner, Is.EqualTo(0));
            world.Step();
            Assert.That(world.Sim.Clock.Tick, Is.EqualTo(3));
            Assert.That(world.Capture.Points[0].Owner, Is.EqualTo(1));
            Assert.That(world.Capture.Changes.Length, Is.EqualTo(1));
            Assert.That(world.Match.Winner, Is.EqualTo(0));
            world.Step();
            Assert.That((world.Match.Winner, world.Match.EndReason), Is.EqualTo((0, MatchEndReason.None)));
        }

        [TestCase("[{\"id\":\"missing-rule\"}]", "missing-rule")]
        [TestCase("[]", "[]")]
        public void R2_InvalidRulesNamePackFieldAndId(string rules, string badId)
        {
            var error = Assert.Throws<ContentException>(() => Pack("\"rules\":" + rules));
            Assert.That(error.Message, Does.Contain("r2-pack"));
            Assert.That(error.Message, Does.Contain("rules"));
            Assert.That(error.Message, Does.Contain(badId));
        }

        [Test]
        public void R2_DuplicateRuleUsesTheArrayMergeError()
        {
            var error = Assert.Throws<ContentException>(() => Load("\"rules\":[{\"id\":\"exhausted\"},{\"id\":\"exhausted\"}]"));
            Assert.That(error.Message, Is.EqualTo("content layer 'r2-pack' rule 'exhausted' is duplicated"));
        }

        [TestCase("\"exhausted\"", "must be an array")]
        [TestCase("[7]", "entry must be an object")]
        [TestCase("[{}]", "entry needs a string id")]
        public void R2_RulesMustBeAnIdArray(string rules, string reason)
        {
            var error = Assert.Throws<ContentException>(() => Load("\"rules\":" + rules));
            Assert.That(error.Message, Does.Contain("r2-pack"));
            Assert.That(error.Message, Does.Contain("rule"));
            Assert.That(error.Message, Does.Contain(reason));
        }

        [Test]
        public void R2_RuleEntriesOnlyAcceptId()
        {
            var error = Assert.Throws<ContentException>(() => Load("\"rules\":[{\"id\":\"exhausted\",\"enabled\":true}]"));
            Assert.That(error.Message, Is.EqualTo("content pack 'r2-pack' layer 'r2-pack' rule 'exhausted' field 'enabled': unknown field name (misspelled?)"));
        }

        [Test]
        public void R2_InheritedRulesKeepParentOrderAndAppendNewIds()
        {
            var source = new FixedLocationSource(new[]
            {
                new KeyValuePair<string, string>("r2-parent", "{\"id\":\"r2-parent\",\"extends\":\"rwr-m6\",\"rules\":[{\"id\":\"exhausted\"},{\"id\":\"all-points\"}]}"),
                new KeyValuePair<string, string>("r2-child", "{\"id\":\"r2-child\",\"extends\":\"r2-parent\",\"rules\":[{\"id\":\"all-points\"},{\"id\":\"time-limit\"}]}"),
                new KeyValuePair<string, string>("r2-omitted", "{\"id\":\"r2-omitted\",\"extends\":\"r2-parent\"}"),
            });
            Assert.That(RwrMatch.CreateM6(ContentLoader.Load(source, "r2-child")).Rules.ToArray(),
                Is.EqualTo(new[] { "exhausted", "all-points", "time-limit" }));
            Assert.That(RwrMatch.CreateM6(ContentLoader.Load(source, "r2-omitted")).Rules.ToArray(),
                Is.EqualTo(new[] { "exhausted", "all-points" }));
        }

        [Test]
        public void R2_EmptyChildRulesAreRejectedEvenWithParentEntries()
        {
            var source = new FixedLocationSource(new[]
            {
                new KeyValuePair<string, string>("r2-parent", "{\"id\":\"r2-parent\",\"extends\":\"rwr-m6\",\"rules\":[{\"id\":\"exhausted\"}]}"),
                new KeyValuePair<string, string>("r2-pack", "{\"id\":\"r2-pack\",\"extends\":\"r2-parent\",\"rules\":[]}"),
            });
            var error = Assert.Throws<ContentException>(() => ContentLoader.Load(source, "r2-pack"));
            Assert.That(error.Message, Does.Contain("r2-pack"));
            Assert.That(error.Message, Does.Contain("rules"));
            Assert.That(error.Message, Does.Contain("[]"));
        }

        [TestCase("[{\"id\":\"all-points\"},{\"id\":\"exhausted\"},{\"id\":\"time-limit\"}]", 1, MatchEndReason.AllPoints)]
        [TestCase("[{\"id\":\"exhausted\"},{\"id\":\"all-points\"},{\"id\":\"time-limit\"}]", 2, MatchEndReason.Exhausted)]
        [TestCase("[{\"id\":\"time-limit\"},{\"id\":\"exhausted\"},{\"id\":\"all-points\"}]", 1, MatchEndReason.Time)]
        public void R2_FirstSelectedRuleDecides(string rules, int winner, MatchEndReason reason)
        {
            var pack = Pack("\"rules\":" + rules);
            var mode = new MatchMode(pack);
            mode.Bind(new Reader { OwnedOne = 3, AliveOne = 0 });
            mode.Evaluate(pack.MaxTicks);
            Assert.That((mode.Winner, mode.EndReason, mode.EndTick), Is.EqualTo((winner, reason, (long)pack.MaxTicks)));
        }

        [Test]
        public void R2_DefaultIncludesTimeLimit()
        {
            var pack = Pack("");
            var mode = new MatchMode(pack);
            mode.Bind(new Reader { OwnedOne = 2 });
            mode.Evaluate(pack.MaxTicks - 1);
            Assert.That(mode.Winner, Is.EqualTo(0));
            mode.Evaluate(pack.MaxTicks);
            Assert.That((mode.Winner, mode.EndReason, mode.EndTick), Is.EqualTo((1, MatchEndReason.Time, (long)pack.MaxTicks)));
        }

        [TestCase("all-points", 9000, 9001, MatchEndReason.AllPoints)]
        [TestCase("time-limit", 8999, 9000, MatchEndReason.Time)]
        public void R2_OnlyTheSelectedPartRuns(string rule, int before, int end, MatchEndReason reason)
        {
            var mode = new MatchMode(Pack("\"rules\":[{\"id\":\"" + rule + "\"}]"));
            var reader = new Reader { OwnedOne = rule == "time-limit" ? 3 : 2, AliveOne = 0 };
            mode.Bind(reader);
            mode.Evaluate(before);
            Assert.That(mode.Winner, Is.EqualTo(0));
            reader.OwnedOne = 3;
            mode.Evaluate(end);
            Assert.That((mode.Winner, mode.EndReason, mode.EndTick), Is.EqualTo((1, reason, (long)end)));
        }

        [TestCase(DefaultRules, "[{\"id\":\"exhausted\"}]")]
        [TestCase(DefaultRules, "[{\"id\":\"exhausted\"},{\"id\":\"all-points\"},{\"id\":\"time-limit\"}]")]
        [TestCase("[{\"id\":\"exhausted\"},{\"id\":\"all-points\"}]", "[{\"id\":\"all-points\"},{\"id\":\"exhausted\"}]")]
        [TestCase("[{\"id\":\"exhausted\"}]", "[{\"id\":\"time-limit\"}]")]
        public void R2_ChangedRulesRejectSnapshotWithoutChangingRuleSetId(string originalRules, string rules)
        {
            var recipe = M6World.RecipeOf(new[] { M6World.U(1, 1), M6World.U(2, 2, 60) }, Pack("\"rules\":" + originalRules));
            var original = new M6World(recipe);
            var changed = new M6World(recipe.WithMatch(Pack("\"rules\":" + rules)));
            Assert.That(changed.Mode.RuleSetId, Is.EqualTo(original.Mode.RuleSetId));
            Assert.That(changed.Hash, Is.Not.EqualTo(original.Hash));
            var error = Assert.Throws<InvalidOperationException>(() => changed.Sim.RestoreRetainedSnapshot(original.Sim.SaveRetainedSnapshot()));
            Assert.That(error.Message, Is.EqualTo("Battle configuration differs from snapshot."));
        }

        [Test]
        public void R2_PackCopiesRuleIds()
        {
            var original = Pack("");
            var rules = new[] { "exhausted" };
            var pack = new MatchPack(original.Points.ToArray(), original.Bases.ToArray(), original.Num, original.Den, original.Strict,
                original.CaptureTicks, original.Interrupt, original.DecayPerTick, original.IntervalTicks, original.ThinkInterval,
                original.MaxTicks, original.Sight, original.GridColumns, original.GridRows, original.GridSpacing, rules: rules);
            rules[0] = "all-points";
            var mode = new MatchMode(pack);
            mode.Bind(new Reader { OwnedOne = 3, AliveOne = 0 });
            mode.Evaluate(1);
            Assert.That((mode.Winner, mode.EndReason), Is.EqualTo((2, MatchEndReason.Exhausted)));
        }

        [Test]
        public void R2_NeverEndsSkipsSelectedRules()
        {
            var pack = Pack("\"rules\":[{\"id\":\"time-limit\"},{\"id\":\"all-points\"},{\"id\":\"exhausted\"}]");
            var mode = new MatchMode(pack, MatchMode.TestNeverEndsId);
            mode.Bind(new Reader { OwnedOne = 3, AliveOne = 0 });
            mode.Evaluate(pack.MaxTicks);
            Assert.That((mode.Winner, mode.EndReason, mode.EndTick), Is.EqualTo((0, MatchEndReason.None, 0L)));
        }
    }
}
