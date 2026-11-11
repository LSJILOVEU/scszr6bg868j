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
        private const string AllRules = "{ \"failedSlotTicks\": 7, \"targetMemoryTicks\": 11, \"searchTicks\": 13,"
            + " \"suppressTicks\": 17, \"reactionTicks\": 3, \"senseTicks\": 2, \"burstShots\": 2,"
            + " \"burstPauseTicks\": 5, \"meleeChaseRange\": 30 }";
        private static readonly SimVec3 Start = new SimVec3(SimFix.Ratio(21, 2), default, SimFix.Ratio(21, 2));
        private static readonly SimVec3 Enemy = new SimVec3(SimFix.Ratio(61, 2), default, SimFix.Ratio(21, 2));

        private static ContentPack Pack(string ai) => ContentLoader.Load(new MemoryContentSource(new Dictionary<string, string>
        {
            ["gen-ai"] = "{ \"id\": \"gen-ai\", \"ai\": " + ai + ","
                + " \"combat\": { \"maxHealth\": 100, \"resolver\": \"rwr.hit\", \"selector\": \"nearest-visible\","
                + " \"friendlyFire\": false, \"friendlyFireLocked\": false, \"allyCheckMissRate\": 0, \"allyHoldMilli\": 1000 },"
                + " \"melee\": [{ \"id\": \"sword\", \"windupTicks\": 6, \"activeTicks\": 2, \"recoveryTicks\": 9,"
                + " \"radius\": 2, \"cosHalfAnglePermille\": 500, \"halfHeight\": 1, \"damage\": 10,"
                + " \"maxTargets\": 1, \"engageRange\": 1.8 }],"
                + " \"unitTypes\": [{ \"id\": \"soldier\", \"weapon\": \"\", \"melee\": \"sword\", \"speed\": 3000 }] }"
        }), "gen-ai");

        private static WorldRecipe Recipe(ContentPack pack) => new WorldRecipe(41, MoverKind.InterimCrowd, new[]
        {
            UnitSpawn.OfType(1, pack, "soldier", 1, 1, 10500, 10500),
            UnitSpawn.OfType(2, pack, "soldier", 2, 2, 30500, 10500),
        }, Array.Empty<BridgeCommand>()).WithCombat(pack.BuildCombat())
            .WithMatch(M6World.Pack(new[] { new ControlPointDef(1, Start, SimFix.Ratio(1, 2), 1) }, think: 10))
            .WithPlayer(2).WithRuleSet(MatchMode.TestNeverEndsId);

        [Test]
        public void FlatMap_OptIn_ChasesAtTickSix()
        {
            var pack = Pack(Chase);
            WorldRecipe original = Recipe(pack);
            WorldRecipe enabled = original.WithAiRules(pack).WithRuleSet(MatchMode.TestNeverEndsId);
            Assert.That(original.AiPack, Is.Null);
            Assert.That(enabled.AiPack, Is.SameAs(pack));
            var on = new M6World(enabled);
            var off = new M6World(original.WithTerrainRules(pack));
            for (int tick = 1; tick <= 5; tick++)
            {
                on.Step(); off.Step();
                Assert.That(on.Read<Position>(1).Value, Is.EqualTo(Start), "tick " + tick);
                Assert.That(on.Read<MoveIntent>(1).Active, Is.False, "tick " + tick);
            }
            on.Step(); off.Step();
            Assert.That(on.Sim.Clock.Tick, Is.EqualTo(6));
            Assert.That(on.Read<MoveIntent>(1).Target, Is.EqualTo(Enemy));
            Assert.That(on.Read<MoveIntent>(1).Active, Is.True);
            Assert.That(on.Read<Position>(1).Value.X.Raw, Is.GreaterThan(Start.X.Raw));
            Assert.That(on.Read<Position>(1).Value.Z.Raw, Is.EqualTo(Start.Z.Raw));
            Assert.That(off.Read<MoveIntent>(1).Active, Is.False);
            Assert.That(off.Read<Position>(1).Value, Is.EqualTo(Start));
        }

        [Test]
        public void FlatMap_WithoutOptIn_IgnoresContentAiByteForByte()
        {
            var pack = Pack(AllRules);
            var off = new M6World(Recipe(pack).WithTerrainRules(pack));
            var plain = new M6World(Recipe(Pack("{}")));
            Assert.That(AssemblyWorlds.Systems(off.Sim), Is.EqualTo(AssemblyWorlds.Systems(plain.Sim)));
            for (int tick = 0; tick <= 30; tick++)
            {
                Assert.That(off.Hash, Is.EqualTo(plain.Hash), "tick " + tick);
                Assert.That(off.Sim.SaveRetainedSnapshot(), Is.EqualTo(plain.Sim.SaveRetainedSnapshot()), "tick " + tick);
                Assert.That(off.Read<Position>(1).Value, Is.EqualTo(Start), "tick " + tick);
                Assert.That(off.Read<MoveIntent>(1).Active, Is.False, "tick " + tick);
                if (tick < 30) { off.Step(); plain.Step(); }
            }
        }

        [Test]
        public void FlatMap_OptIn_AssemblesAllContentAiRules()
        {
            var pack = Pack(AllRules);
            var w = new M6World(Recipe(pack).WithTerrainRules(Pack("{}")).WithAiRules(pack));
            State(w.System<FailedSlotMemory>(), 7, 0);
            State(w.System<TargetMemory>(), 11, 0);
            State(w.System<TargetSearch>(), 13, 0);
            State(w.System<TargetSuppress>(), 17, 0);
            State(w.System<ReactionFire>(), 3, 0, 2, 2, 5);
        }

        private static void State(IRetainedGameplaySystem system, params int[] values)
        {
            Assert.That(system, Is.Not.Null);
            var actual = new SimStateWriter();
            system.SaveState(actual);
            var expected = new SimStateWriter();
            foreach (int value in values) expected.Int32(value);
            Assert.That(actual.ToArray(), Is.EqualTo(expected.ToArray()));
        }

        [Test]
        public void FlatMap_OptIn_DefaultRulesLeaveBytesUnchanged()
        {
            var pack = Pack("{}");
            WorldRecipe recipe = Recipe(pack);
            var off = new M6World(recipe);
            var on = new M6World(recipe.WithAiRules(pack));
            for (int tick = 0; tick <= 30; tick++)
            {
                Assert.That(on.Hash, Is.EqualTo(off.Hash), "tick " + tick);
                Assert.That(on.Sim.SaveRetainedSnapshot(), Is.EqualTo(off.Sim.SaveRetainedSnapshot()), "tick " + tick);
                if (tick < 30) { off.Step(); on.Step(); }
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void TerrainMap_OptIn_KeepsTerrainRuleSource(bool terrainEnabled)
        {
            var pack = Pack(terrainEnabled ? "{}" : AllRules);
            WorldRecipe recipe = Recipe(pack).WithTerrainRules(L4.Pack(terrainEnabled ? AllRules : "{}"));
            byte[] map = A1.Map();
            var off = new M6World(recipe, map);
            var on = new M6World(recipe.WithAiRules(pack), map);
            Assert.That(on.System<ReactionFire>() != null, Is.EqualTo(terrainEnabled));
            Assert.That(AssemblyWorlds.Systems(on.Sim), Is.EqualTo(AssemblyWorlds.Systems(off.Sim)));
            for (int tick = 0; tick <= 30; tick++)
            {
                Assert.That(on.Hash, Is.EqualTo(off.Hash), "tick " + tick);
                Assert.That(on.Sim.SaveRetainedSnapshot(), Is.EqualTo(off.Sim.SaveRetainedSnapshot()), "tick " + tick);
                if (tick == 6) Assert.That(on.Read<MoveIntent>(1).Active, Is.EqualTo(terrainEnabled));
                if (tick < 30) { off.Step(); on.Step(); }
            }
        }
    }
}
