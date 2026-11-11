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
        private const string All = "{ \"failedSlotTicks\": 2, \"targetMemoryTicks\": 3, \"searchTicks\": 4,"
            + " \"suppressTicks\": 5, \"reactionTicks\": 7, \"senseTicks\": 8, \"burstShots\": 9,"
            + " \"burstPauseTicks\": 11, \"meleeChaseRange\": 12 }";
        private static readonly SimVec3 Start = SimVec3.Metres(10, 0, 10), Enemy = SimVec3.Metres(20, 0, 10);

        private static ContentPack Pack(string ai) => ContentLoader.Load(new MemoryContentSource(new Dictionary<string, string>
        {
            ["gen-ai"] = "{ \"id\": \"gen-ai\", \"combat\": { \"maxHealth\": 100, \"resolver\": \"rwr.hit\","
                + " \"selector\": \"nearest-visible\", \"friendlyFire\": false, \"friendlyFireLocked\": false,"
                + " \"allyCheckMissRate\": 0, \"allyHoldMilli\": 1000 },"
                + " \"melee\": [{ \"id\": \"sword\", \"windupTicks\": 6, \"activeTicks\": 2, \"recoveryTicks\": 9,"
                + " \"radius\": 2, \"halfHeight\": 1, \"damage\": 10, \"engageRange\": 1.8,"
                + " \"cosHalfAnglePermille\": 500, \"maxTargets\": 1 }],"
                + " \"unitTypes\": [{ \"id\": \"swordsman\", \"weapon\": \"\", \"melee\": \"sword\", \"speed\": 3000 }]"
                + (ai == null ? "" : ", \"ai\": " + ai) + " }"
        }), "gen-ai");

        private static WorldRecipe Recipe(ContentPack pack) => new WorldRecipe(41, MoverKind.InterimCrowd, new[]
        {
            UnitSpawn.OfType(1, pack, "swordsman", 1, 1, 10000, 10000),
            new UnitSpawn(2, 0, 2, 20000, 10000, 1, 2),
        }, Array.Empty<BridgeCommand>()).WithCombat(pack.BuildCombat())
            .WithMatch(M6World.Pack(new[] { new ControlPointDef(1, Start, SimFix.Ratio(1, 2), 1) }, think: 10))
            .WithRuleSet(MatchMode.TestNeverEndsId);

        [Test]
        public void FlatWithoutOptIn_KeepsLegacyBytesAndDoesNotChase()
        {
            ContentPack pack = Pack(All);
            var legacy = new M6World(Recipe(Pack(null)));
            var off = new M6World(Recipe(pack).WithTerrainRules(pack));
            Assert.That(L4.Systems(off), Is.EqualTo(L4.Systems(legacy)));
            for (int tick = 0; tick <= 100; tick++)
            {
                if (tick > 0) { legacy.Step(); off.Step(); }
                Assert.That(off.Hash, Is.EqualTo(legacy.Hash), "tick " + tick);
                Assert.That(off.Sim.SaveRetainedSnapshot(), Is.EqualTo(legacy.Sim.SaveRetainedSnapshot()), "tick " + tick);
                Assert.That(off.Read<Position>(1).Value, Is.EqualTo(Start), "tick " + tick);
                Assert.That(off.Read<MoveIntent>(1).Active, Is.False, "tick " + tick);
            }
        }

        [Test]
        public void FlatOptIn_ChasesAtTickSix()
        {
            ContentPack pack = Pack(Chase);
            WorldRecipe original = Recipe(pack);
            var on = new M6World(original.WithAiRules(pack).WithRuleSet(MatchMode.TestNeverEndsId));
            var off = new M6World(original);
            for (int tick = 1; tick < 6; tick++)
            {
                on.Step(); off.Step();
                Assert.That(on.Read<Position>(1).Value, Is.EqualTo(Start), "tick " + tick);
                Assert.That(on.Read<MoveIntent>(1).Active, Is.False, "tick " + tick);
                Assert.That(on.Hash, Is.EqualTo(off.Hash), "tick " + tick);
            }
            on.Step(); off.Step();
            Assert.That(on.Sim.Clock.Tick, Is.EqualTo(6));
            Assert.That(on.Read<MoveIntent>(1).Target, Is.EqualTo(Enemy));
            Assert.That(on.Read<MoveIntent>(1).Active, Is.True);
            Assert.That(on.Read<Position>(1).Value.X, Is.GreaterThan(Start.X));
            Assert.That((Enemy - on.Read<Position>(1).Value).LengthSquared, Is.LessThan((Enemy - Start).LengthSquared));
            Assert.That(off.Read<Position>(1).Value, Is.EqualTo(Start));
            Assert.That(off.Read<MoveIntent>(1).Active, Is.False);
        }

        [Test]
        public void FlatOptIn_AssemblesAllAiRules()
        {
            ContentPack pack = Pack(All);
            var w = new M6World(Recipe(pack).WithAiRules(pack).WithTerrainRules(Pack(null)));
            State(w.System<FailedSlotMemory>(), 2, 0);
            State(w.System<TargetMemory>(), 3, 0);
            State(w.System<TargetSearch>(), 4, 0);
            State(w.System<TargetSuppress>(), 5, 0);
            State(w.System<ReactionFire>(), 7, 0, 8, 9, 11);
        }

        [TestCase(null)]
        [TestCase("{}")]
        public void FlatOptIn_DefaultRulesKeepLegacyBytes(string ai)
        {
            ContentPack pack = Pack(ai);
            WorldRecipe recipe = Recipe(pack);
            var legacy = new M6World(recipe);
            var on = new M6World(recipe.WithAiRules(pack));
            Assert.That(L4.Systems(on), Is.EqualTo(L4.Systems(legacy)));
            for (int tick = 0; tick <= 100; tick++)
            {
                if (tick > 0) { legacy.Step(); on.Step(); }
                Assert.That(on.Hash, Is.EqualTo(legacy.Hash), "tick " + tick);
                Assert.That(on.Sim.SaveRetainedSnapshot(), Is.EqualTo(legacy.Sim.SaveRetainedSnapshot()), "tick " + tick);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void TerrainKeepsOriginalRuleSource(bool terrainAi)
        {
            ContentPack pack = Pack(Chase);
            WorldRecipe recipe = Recipe(pack).WithTerrainRules(A1.Pack(terrainAi ? All : null));
            var legacy = new M6World(recipe, A1.Map());
            var on = new M6World(recipe.WithAiRules(Pack(terrainAi ? null : All)), A1.Map());
            Assert.That(L4.Systems(on), Is.EqualTo(L4.Systems(legacy)));
            for (int tick = 0; tick <= 100; tick++)
            {
                if (tick > 0) { legacy.Step(); on.Step(); }
                Assert.That(on.Hash, Is.EqualTo(legacy.Hash), "tick " + tick);
                Assert.That(on.Sim.SaveRetainedSnapshot(), Is.EqualTo(legacy.Sim.SaveRetainedSnapshot()), "tick " + tick);
                if (tick == 6)
                {
                    Assert.That(on.Read<MoveIntent>(1).Active, Is.EqualTo(terrainAi));
                    if (terrainAi) Assert.That(on.Read<MoveIntent>(1).Target, Is.EqualTo(Enemy));
                    else Assert.That(on.Read<Position>(1).Value, Is.EqualTo(Start));
                }
            }
        }

        private static void State(IRetainedGameplaySystem system, params int[] expected)
        {
            Assert.That(system, Is.Not.Null);
            var writer = new SimStateWriter(); system.SaveState(writer);
            var reader = new SimStateReader(writer.ToArray());
            foreach (int value in expected) Assert.That(reader.Int32(), Is.EqualTo(value));
            Assert.That(reader.Remaining, Is.EqualTo(0));
        }
    }
}
