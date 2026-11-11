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
        private const string Chase = "{ \"meleeChaseRange\": 12 }";
        private const string AllRules = "{ \"failedSlotTicks\": 7, \"targetMemoryTicks\": 11, \"searchTicks\": 13, "
            + "\"suppressTicks\": 17, \"reactionTicks\": 3, \"senseTicks\": 2, \"burstShots\": 2, \"burstPauseTicks\": 5, \"meleeChaseRange\": 12 }";
        private static readonly SimVec3 Start = L4.Cell(10, 10), Enemy = L4.Cell(20, 10);

        private static ContentPack Pack(string ai)
        {
            string json = @"{ ""id"": ""gen-ai-rules"",
                ""combat"": { ""maxHealth"": 100, ""resolver"": ""rwr.hit"", ""selector"": ""nearest-visible"",
                    ""friendlyFire"": false, ""friendlyFireLocked"": false, ""allyCheckMissRate"": 0, ""allyHoldMilli"": 30000 },
                ""melee"": [ { ""id"": ""blade"", ""windupTicks"": 2, ""activeTicks"": 1, ""recoveryTicks"": 3,
                    ""radius"": 2, ""halfHeight"": 1, ""damage"": 10, ""engageRange"": 1,
                    ""cosHalfAnglePermille"": 0, ""maxTargets"": 1 } ]"
                + (ai == null ? "" : ", \"ai\": " + ai) + " }";
            return ContentLoader.Load(new MemoryContentSource(new Dictionary<string, string> { ["gen-ai-rules"] = json }), "gen-ai-rules");
        }

        private static WorldRecipe Recipe(ContentPack pack) =>
            new WorldRecipe(41, MoverKind.InterimCrowd, new[]
            {
                new UnitSpawn(1, 0, 1, 10500, 10500, 3000, 1, melee: 0),
                new UnitSpawn(2, 0, 2, 20500, 10500, 3000, 2),
            }, Array.Empty<BridgeCommand>()).WithCombat(pack.BuildCombat())
                .WithMatch(L4.Match(new ControlPointDef(1, L4.Cell(2, 2), SimFix.FromInt(1), 1), 1))
                .WithPlayer(2).WithRuleSet(MatchMode.TestNeverEndsId);

        private static byte[] Map(bool terrain = false) => WcMapWriter.Write(Wp23.Make(40, 20, 0, 4 * SimFix.Scale,
            Array.Empty<(int, int, int, int)>(), 2 * SimFix.Scale, terrain ? (Func<int, int, int>)((x, z) => 0) : null));

        [Test]
        public void FlatWithOption_ChasesOnFirstTick()
        {
            ContentPack pack = Pack(Chase);
            WorldRecipe recipe = Recipe(pack);
            var on = new M6World(recipe.WithAiRules(pack).WithPlayer(2), Map());
            var off = new M6World(recipe, Map());
            Assert.That(on.Sim.Clock.Tick, Is.EqualTo(0));
            Assert.That(on.Read<Position>(1).Value, Is.EqualTo(Start));
            Assert.That(on.Read<MoveIntent>(1).Active, Is.False);
            on.Step(); off.Step();
            Assert.That(on.Sim.Clock.Tick, Is.EqualTo(1));
            Assert.That(on.Read<MoveIntent>(1).Active, Is.True);
            Assert.That(on.Read<MoveIntent>(1).Target, Is.EqualTo(Enemy));
            Assert.That(on.Read<Position>(1).Value,
                Is.EqualTo(Start + new SimVec3(SimFix.Ratio(3000, 1000 * SimClock.TickRate), default, default)));
            Assert.That(off.Read<Position>(1).Value, Is.EqualTo(Start));
            Assert.That(off.Read<MoveIntent>(1).Active, Is.False);
        }

        [Test]
        public void FlatWithoutOption_KeepsOriginalState()
        {
            ContentPack pack = Pack(AllRules);
            var withContent = new M6World(Recipe(pack).WithTerrainRules(pack), Map());
            var omitted = new M6World(Recipe(Pack(null)), Map());
            Assert.That(L4.Systems(withContent), Is.EqualTo(L4.Systems(omitted)));
            for (int tick = 0; tick <= 30; tick++)
            {
                if (tick > 0) { withContent.Step(); omitted.Step(); }
                Assert.That(withContent.Sim.Clock.Tick, Is.EqualTo(tick));
                Assert.That(withContent.Read<Position>(1).Value, Is.EqualTo(Start), "tick " + tick);
                Assert.That(withContent.Read<MoveIntent>(1).Active, Is.False, "tick " + tick);
                Assert.That(withContent.Hash, Is.EqualTo(omitted.Hash), "tick " + tick);
                Assert.That(withContent.Sim.SaveRetainedSnapshot(), Is.EqualTo(omitted.Sim.SaveRetainedSnapshot()), "tick " + tick);
            }
        }

        [Test]
        public void FlatWithDefaultRules_KeepsOriginalState()
        {
            ContentPack pack = Pack(AllRules);
            WorldRecipe recipe = Recipe(pack).WithTerrainRules(pack);
            var off = new M6World(recipe, Map());
            var defaults = new M6World(recipe.WithAiRules(Pack(null)), Map());
            Assert.That(L4.Systems(defaults), Is.EqualTo(L4.Systems(off)));
            Assert.That(defaults.Sim.SaveRetainedSnapshot(), Is.EqualTo(off.Sim.SaveRetainedSnapshot()));
            defaults.Step(30); off.Step(30);
            Assert.That(defaults.Read<Position>(1).Value, Is.EqualTo(Start));
            Assert.That(defaults.Hash, Is.EqualTo(off.Hash));
            Assert.That(defaults.Sim.SaveRetainedSnapshot(), Is.EqualTo(off.Sim.SaveRetainedSnapshot()));
        }

        [Test]
        public void FlatWithOption_RegistersContentRules()
        {
            ContentPack pack = Pack(AllRules);
            var flat = new M6World(Recipe(Pack(null)).WithAiRules(pack), Map());
            var terrain = new M6World(Recipe(pack).WithTerrainRules(pack), Map(true));
            SameRuleState<FailedSlotMemory>(flat, terrain);
            SameRuleState<TargetMemory>(flat, terrain);
            SameRuleState<TargetSearch>(flat, terrain);
            SameRuleState<TargetSuppress>(flat, terrain);
            SameRuleState<ReactionFire>(flat, terrain);
        }

        private static void SameRuleState<T>(M6World flat, M6World terrain) where T : class, IRetainedGameplaySystem
        {
            T actual = flat.System<T>(), expected = terrain.System<T>();
            Assert.That(actual, Is.Not.Null, typeof(T).Name);
            Assert.That(expected, Is.Not.Null, typeof(T).Name);
            var flatHash = new StateHash(); var terrainHash = new StateHash();
            actual.WriteState(flatHash); expected.WriteState(terrainHash);
            Assert.That(flatHash.Value, Is.EqualTo(terrainHash.Value), typeof(T).Name);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void TerrainWithOption_KeepsTerrainRules(bool enabled)
        {
            ContentPack pack = Pack(enabled ? AllRules : null);
            WorldRecipe recipe = Recipe(pack).WithTerrainRules(pack);
            var original = new M6World(recipe, Map(true));
            var withOption = new M6World(recipe.WithAiRules(Pack(enabled ? null : AllRules)), Map(true));
            Assert.That(L4.Systems(withOption), Is.EqualTo(L4.Systems(original)));
            for (int tick = 0; tick <= 30; tick++)
            {
                if (tick > 0) { original.Step(); withOption.Step(); }
                Assert.That(withOption.Hash, Is.EqualTo(original.Hash), "tick " + tick);
                Assert.That(withOption.Sim.SaveRetainedSnapshot(), Is.EqualTo(original.Sim.SaveRetainedSnapshot()), "tick " + tick);
            }
            Assert.That(original.Read<MoveIntent>(1).Active, Is.EqualTo(enabled));
            if (enabled) Assert.That(original.Read<Position>(1).Value.X.Raw, Is.GreaterThan(Start.X.Raw));
            else Assert.That(original.Read<Position>(1).Value, Is.EqualTo(Start));
        }
    }
}
