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
        private const string Chase = "{ \"meleeChaseRange\": 30 }";
        private const string All = "{ \"failedSlotTicks\": 7, \"targetMemoryTicks\": 9, \"searchTicks\": 11, "
            + "\"reactionTicks\": 13, \"senseTicks\": 3, \"burstShots\": 2, \"burstPauseTicks\": 17, "
            + "\"suppressTicks\": 19, \"meleeChaseRange\": 30 }";
        private static readonly SimVec3 Start = A2.Cell(10, 20), Enemy = A2.Cell(30, 20);

        private static ContentPack Pack(string ai) => ContentLoader.Load(new MemoryContentSource(new Dictionary<string, string>
        {
            ["gen-ai"] = "{ \"id\": \"gen-ai\", \"extends\": \"rwr\", \"ai\": " + ai + " }"
        }, FileContentSource.Rwr()), "gen-ai");

        private static WorldRecipe Recipe() => new WorldRecipe(41, MoverKind.InterimCrowd, new[]
        {
            new UnitSpawn(1, 0, 1, 10500, 20500, 3000, 1, melee: 0),
            new UnitSpawn(2, 0, 2, 30500, 20500, 3000, 2)
        }, Array.Empty<BridgeCommand>())
            .WithMatch(M6World.Pack(points: new[] { new ControlPointDef(1, Start, SimFix.Ratio(1, 2)) }, ticks: 1, think: 10))
            .WithCombat(MeleeWorld.Pack(new[] { MeleeWorld.Spec() })).WithPlayer(2).WithRuleSet(MatchMode.TestNeverEndsId);

        private static byte[] Map(bool terrain = false) => WcMapWriter.Write(Wp23.Make(40, 30, 0, 4 * SimFix.Scale,
            Array.Empty<(int, int, int, int)>(), 2 * SimFix.Scale, terrain ? (Func<int, int, int>)((x, z) => 0) : null));

        [Test]
        public void FlatMap_OptInChasesAtFirstThink()
        {
            WorldRecipe original = Recipe();
            var on = new M6World(original.WithAiRules(Pack(Chase)).WithRuleSet(MatchMode.TestNeverEndsId), Map());
            var off = new M6World(original, Map());
            Assert.That(original.Ai, Is.Null);
            Assert.That(on.Recipe.TerrainPack, Is.Null);
            for (int tick = 0; tick < 6; tick++)
            {
                Assert.That(on.Read<MoveIntent>(1).Active, Is.False, "tick " + tick);
                Assert.That(on.Read<Position>(1).Value, Is.EqualTo(Start), "tick " + tick);
                on.Step(); off.Step();
            }
            Assert.That(on.Sim.Clock.Tick, Is.EqualTo(6));
            Assert.That(on.Read<MoveIntent>(1).Target, Is.EqualTo(Enemy));
            Assert.That(on.Read<MoveIntent>(1).Active, Is.True);
            Assert.That(on.Read<Position>(1).Value, Is.EqualTo(Start + new SimVec3(SimFix.Ratio(1, 10), default, default)));
            Assert.That(off.Read<Position>(1).Value, Is.EqualTo(Start));
            Assert.That(off.Read<MoveIntent>(1).Active, Is.False);
            on.Step(10);
            Assert.That(on.Read<Position>(1).Value.X, Is.GreaterThan(Start.X));
            Assert.That(on.Read<Position>(2).Value, Is.EqualTo(Enemy));
        }

        [Test]
        public void FlatMap_DefaultIgnoresContentAi()
        {
            var off = new M6World(Recipe().WithTerrainRules(Pack(All)), Map());
            var zero = new M6World(Recipe().WithTerrainRules(Pack("{}")), Map());
            Assert.That(L4.Systems(off), Is.EqualTo(L4.Systems(zero)));
            Assert.That(off.Sim.SaveRetainedSnapshot(), Is.EqualTo(zero.Sim.SaveRetainedSnapshot()));
            for (int tick = 0; tick <= 30; tick++)
            {
                Assert.That(off.Mode.IsFinished, Is.False, "tick " + tick);
                Assert.That(off.Hash, Is.EqualTo(zero.Hash), "tick " + tick);
                Assert.That(off.Read<Position>(1).Value, Is.EqualTo(Start), "tick " + tick);
                Assert.That(off.Read<MoveIntent>(1).Active, Is.False, "tick " + tick);
                if (tick < 30) { off.Step(); zero.Step(); }
            }
            Assert.That(off.Sim.SaveRetainedSnapshot(), Is.EqualTo(zero.Sim.SaveRetainedSnapshot()));
        }

        [Test]
        public void FlatMap_OptInRegistersContentSystems()
        {
            var w = new M6World(Recipe().WithAiRules(Pack(All)), Map());
            Assert.That(w.System<FailedSlotMemory>(), Is.Not.Null);
            Assert.That(w.System<TargetMemory>(), Is.Not.Null);
            Assert.That(w.System<TargetSearch>(), Is.Not.Null);
            Assert.That(w.System<TargetSuppress>(), Is.Not.Null);
            Assert.That(w.System<ReactionFire>(), Is.Not.Null);
        }

        [TestCase(All, "{}")]
        [TestCase("{}", All)]
        public void Terrain_ExplicitAiDoesNotOverrideTerrainRules(string terrainAi, string flatAi)
        {
            WorldRecipe recipe = Recipe().WithTerrainRules(A1.Pack(terrainAi));
            var original = new M6World(recipe, Map(true));
            var explicitAi = new M6World(recipe.WithAiRules(Pack(flatAi)), Map(true));
            Assert.That(L4.Systems(explicitAi), Is.EqualTo(L4.Systems(original)));
            Assert.That(explicitAi.Sim.SaveRetainedSnapshot(), Is.EqualTo(original.Sim.SaveRetainedSnapshot()));
            for (int tick = 1; tick <= 30; tick++)
            {
                Assert.That(original.Mode.IsFinished, Is.False, "before tick " + tick);
                original.Step(); explicitAi.Step();
                Assert.That(explicitAi.Hash, Is.EqualTo(original.Hash), "tick " + tick);
            }
            Assert.That(explicitAi.Sim.SaveRetainedSnapshot(), Is.EqualTo(original.Sim.SaveRetainedSnapshot()));
        }
    }
}
