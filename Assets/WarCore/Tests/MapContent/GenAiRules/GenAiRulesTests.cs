using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Content;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class GenAiRulesTests
    {
        private const string Chase = "{ \"meleeChaseRange\": 15 }";
        private const string All = "{ \"failedSlotTicks\": 7, \"targetMemoryTicks\": 9, \"searchTicks\": 11, "
            + "\"reactionTicks\": 13, \"senseTicks\": 3, \"burstShots\": 2, \"burstPauseTicks\": 17, "
            + "\"suppressTicks\": 5, \"meleeChaseRange\": 15 }";
        private static readonly SimVec3 Start = L4.Cell(10, 10), Enemy = L4.Cell(20, 10);

        private static ContentPack Pack(string ai) => ContentLoader.Load(new MemoryContentSource(new Dictionary<string, string>
        {
            ["gen-ai-test"] = "{ \"id\": \"gen-ai-test\", \"extends\": \"rwr\""
                + (ai == null ? "" : ", \"ai\": " + ai) + " }"
        }, FileContentSource.Rwr()), "gen-ai-test");

        private static WorldRecipe Recipe(ContentPack pack) => new WorldRecipe(41, MoverKind.InterimCrowd,
            new[] { MeleeWorld.Unit(1, 1, 10500, 10500, melee: 0), MeleeWorld.Unit(2, 2, 20500, 10500) },
            Array.Empty<BridgeCommand>())
            .WithCombat(MeleeWorld.Pack(new[] { MeleeWorld.Spec() }))
            .WithMatch(M6World.Pack(points: new[] { new ControlPointDef(1, Start, SimFix.Ratio(1, 2), initialOwner: 1) }, think: 1))
            .WithTerrainRules(pack).WithStandingOrders().WithRuleSet(MatchMode.TestNeverEndsId);

        private static byte[] Map(bool terrain = false) => WcMapWriter.Write(Wp23.Make(80, 40, 0, 4 * SimFix.Scale,
            Array.Empty<(int, int, int, int)>(), 2 * SimFix.Scale, terrain ? (Func<int, int, int>)((x, z) => 0) : null));

        private static void Step(M6World w) => w.Sim.Step(w.Sim.Clock.Tick == 0 ? P1.Command(SquadCommandKind.Hold, 2) : default);

        [Test]
        public void FlatOptIn_ChasesAtFirstThinkTick()
        {
            ContentPack pack = Pack(Chase);
            WorldRecipe recipe = Recipe(pack);
            var on = new M6World(recipe.WithAiRules(pack).WithRuleSet(MatchMode.TestNeverEndsId), Map());
            var off = new M6World(recipe.WithRuleSet(MatchMode.TestNeverEndsId), Map());
            Assert.That(on.Sim.Clock.Tick, Is.EqualTo(0));
            Assert.That(on.Read<MoveIntent>(1).Active, Is.False);
            Assert.That(on.Read<Position>(1).Value, Is.EqualTo(Start));
            Step(on); Step(off);
            Assert.That(on.Sim.Clock.Tick, Is.EqualTo(1));
            Assert.That(on.Read<MoveIntent>(1).Active, Is.True);
            Assert.That(on.Read<MoveIntent>(1).Target, Is.EqualTo(Enemy));
            Assert.That(on.Read<Position>(1).Value, Is.EqualTo(Start + new SimVec3(SimFix.FromRaw(6553), default, default)));
            Assert.That(off.Read<MoveIntent>(1).Active, Is.False);
            Assert.That(off.Read<Position>(1).Value, Is.EqualTo(Start));
            Assert.That(recipe.AiPack, Is.Null);
        }

        [Test]
        public void FlatWithoutOptIn_IgnoresContentAi()
        {
            var content = new M6World(Recipe(Pack(All)), Map());
            var omitted = new M6World(Recipe(Pack(null)), Map());
            Assert.That(content.System<FailedSlotMemory>(), Is.Null);
            Assert.That(content.System<TargetMemory>(), Is.Null);
            Assert.That(content.System<TargetSearch>(), Is.Null);
            Assert.That(content.System<TargetSuppress>(), Is.Null);
            Assert.That(content.System<ReactionFire>(), Is.Null);
            for (int tick = 0; tick <= 30; tick++)
            {
                if (tick > 0) { Step(content); Step(omitted); }
                Assert.That(content.Hash, Is.EqualTo(omitted.Hash), "tick " + tick);
                Assert.That(content.Sim.SaveRetainedSnapshot(), Is.EqualTo(omitted.Sim.SaveRetainedSnapshot()), "tick " + tick);
                Assert.That(content.Read<Position>(1).Value, Is.EqualTo(Start), "tick " + tick);
                Assert.That(content.Read<MoveIntent>(1).Active, Is.False, "tick " + tick);
            }
        }

        [Test]
        public void FlatOptIn_AssemblesAllContentRules()
        {
            var w = new M6World(Recipe(Pack(null)).WithAiRules(Pack(All)), Map());
            IMoveOrders moves = L4.Moves(w);
            var contacts = new TargetMemory(w.Combat, 9);
            Assert.That(State(w.System<FailedSlotMemory>()), Is.EqualTo(State(new FailedSlotMemory(w.Sim.Entities, moves, 7, (unit, squad) => false))));
            Assert.That(State(w.System<TargetMemory>()), Is.EqualTo(State(contacts)));
            Assert.That(State(w.System<TargetSearch>()), Is.EqualTo(State(new TargetSearch(w.Sim.Entities, contacts, 11))));
            Assert.That(State(w.System<TargetSuppress>()), Is.EqualTo(State(new TargetSuppress(w.Combat, contacts, moves, 5))));
            Assert.That(State(w.System<ReactionFire>()), Is.EqualTo(State(new ReactionFire(w.Combat, w.Combat.BuiltInAutoFire, 13, 3, 2, 17))));
        }

        private static ulong State(IRetainedGameplaySystem system)
        {
            Assert.That(system, Is.Not.Null);
            var hash = new StateHash(); system.WriteState(hash);
            return hash.Value;
        }

        [TestCase(null)]
        [TestCase("{ }")]
        public void FlatOptIn_DefaultRulesKeepBytes(string ai)
        {
            WorldRecipe recipe = Recipe(Pack(null));
            var off = new M6World(recipe, Map());
            var on = new M6World(recipe.WithAiRules(Pack(ai)), Map());
            for (int tick = 0; tick <= 30; tick++)
            {
                if (tick > 0) { Step(off); Step(on); }
                Assert.That(on.Hash, Is.EqualTo(off.Hash), "tick " + tick);
                Assert.That(on.Sim.SaveRetainedSnapshot(), Is.EqualTo(off.Sim.SaveRetainedSnapshot()), "tick " + tick);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void TerrainKeepsItsRuleSource(bool enabled)
        {
            WorldRecipe recipe = Recipe(Pack(enabled ? All : null));
            var original = new M6World(recipe, Map(true));
            var competing = new M6World(recipe.WithAiRules(Pack(enabled ? null : All)), Map(true));
            for (int tick = 0; tick <= 30; tick++)
            {
                if (tick > 0) { Step(original); Step(competing); }
                Assert.That(competing.Hash, Is.EqualTo(original.Hash), "tick " + tick);
                Assert.That(competing.Sim.SaveRetainedSnapshot(), Is.EqualTo(original.Sim.SaveRetainedSnapshot()), "tick " + tick);
            }
        }
    }
}
