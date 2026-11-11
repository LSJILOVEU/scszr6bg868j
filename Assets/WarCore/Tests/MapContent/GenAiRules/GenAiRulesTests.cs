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
        private const string All = "{ \"failedSlotTicks\": 7, \"targetMemoryTicks\": 11, \"searchTicks\": 13, "
            + "\"suppressTicks\": 17, \"reactionTicks\": 3, \"senseTicks\": 5, \"burstShots\": 2, \"burstPauseTicks\": 19, \"meleeChaseRange\": 12 }";
        private static readonly SimVec3 Start = SimVec3.Metres(10, 0, 10), Enemy = SimVec3.Metres(20, 0, 10);

        private static ContentPack Pack(string ai)
        {
            string json = "{ \"id\": \"gen-ai-rules\", \"combat\": { \"maxHealth\": 100, \"resolver\": \"rwr.hit\", "
                + "\"selector\": \"nearest-visible\", \"friendlyFire\": false, \"friendlyFireLocked\": false, \"allyCheckMissRate\": 0, \"allyHoldMilli\": 30000 }, "
                + "\"melee\": [ { \"id\": \"blade\", \"windupTicks\": 6, \"activeTicks\": 2, \"recoveryTicks\": 9, \"radius\": 2, "
                + "\"cosHalfAnglePermille\": 500, \"halfHeight\": 1, \"damage\": 10, \"maxTargets\": 1, \"engageRange\": 1.8 } ]"
                + (ai == null ? "" : ", \"ai\": " + ai) + " }";
            return ContentLoader.Load(new MemoryContentSource(new Dictionary<string, string> { ["gen-ai-rules"] = json }), "gen-ai-rules");
        }

        private static WorldRecipe Recipe(ContentPack pack) => new WorldRecipe(41, MoverKind.InterimCrowd,
            new[] { MeleeWorld.Unit(1, 1, 10000, 10000, melee: 0), MeleeWorld.Unit(2, 2, 20000, 10000) }, Array.Empty<BridgeCommand>())
            .WithCombat(pack.BuildCombat()).WithMatch(M6World.Pack(points: new[] { new ControlPointDef(1, Start, SimFix.Ratio(1, 2)) }, think: 10))
            .WithPlayer(2).WithRuleSet(MatchMode.TestNeverEndsId);

        private static byte[] Map() => WcMapWriter.Write(Wp23.Make(80, 40, 0, 4 * SimFix.Scale, null, 2 * SimFix.Scale));

        [Test]
        public void FlatOptIn_ChasesAtTickSix()
        {
            ContentPack pack = Pack(Chase);
            WorldRecipe recipe = Recipe(pack);
            var on = new M6World(recipe.WithAiRules(pack).WithRuleSet(MatchMode.TestNeverEndsId), Map());
            var off = new M6World(recipe, Map());
            Assert.That(recipe.AiPack, Is.Null);
            Assert.That(on.Recipe.TerrainPack, Is.Null);
            for (int tick = 1; tick < 6; tick++)
            {
                on.Step(); off.Step();
                Assert.That(on.Read<MoveIntent>(1).Active, Is.False, "tick " + tick);
                Assert.That(on.Read<Position>(1).Value, Is.EqualTo(Start), "tick " + tick);
            }
            on.Step(); off.Step();
            Assert.That(on.Sim.Clock.Tick, Is.EqualTo(6));
            Assert.That(on.Read<MoveIntent>(1).Target, Is.EqualTo(Enemy));
            Assert.That(on.Read<MoveIntent>(1).Active, Is.True);
            Assert.That(on.Read<Position>(1).Value.X.Raw, Is.GreaterThan(Start.X.Raw));
            Assert.That((Enemy - on.Read<Position>(1).Value).LengthSquared, Is.LessThan((Enemy - Start).LengthSquared));
            Assert.That(off.Read<Position>(1).Value, Is.EqualTo(Start));
            Assert.That(off.Read<MoveIntent>(1).Active, Is.False);
        }

        [Test]
        public void FlatWithoutOptIn_IgnoresContentAndKeepsBytes()
        {
            var plain = new M6World(Recipe(Pack(null)), Map());
            ContentPack pack = Pack(All);
            var off = new M6World(Recipe(pack).WithTerrainRules(pack), Map());
            Assert.That(L4.Systems(off), Is.EqualTo(L4.Systems(plain)));
            for (int tick = 0; tick <= 60; tick++)
            {
                Assert.That(off.Hash, Is.EqualTo(plain.Hash), "tick " + tick);
                Assert.That(off.Sim.SaveRetainedSnapshot(), Is.EqualTo(plain.Sim.SaveRetainedSnapshot()), "tick " + tick);
                Assert.That(off.Read<Position>(1).Value, Is.EqualTo(Start), "tick " + tick);
                Assert.That(off.Read<MoveIntent>(1).Active, Is.False, "tick " + tick);
                if (tick < 60) { off.Step(); plain.Step(); }
            }
        }

        [Test]
        public void FlatOptIn_InstallsAllContentRules()
        {
            ContentPack pack = Pack(All);
            var w = new M6World(Recipe(pack).WithTerrainRules(Pack(null)).WithAiRules(pack), Map());
            SameState(w.System<FailedSlotMemory>(), new FailedSlotMemory(w.Combat.Entities, L4.Moves(w), 7, (unit, squad) => false));
            var contacts = new TargetMemory(w.Combat, 11);
            SameState(w.System<TargetMemory>(), contacts);
            SameState(w.System<TargetSearch>(), new TargetSearch(w.Combat.Entities, contacts, 13));
            SameState(w.System<TargetSuppress>(), new TargetSuppress(w.Combat, contacts, L4.Moves(w), 17));
            SameState(w.System<ReactionFire>(), new ReactionFire(w.Combat, w.Combat.BuiltInAutoFire, 3, 5, 2, 19));
        }

        private static void SameState(ISimStateSerializer actual, ISimStateSerializer expected)
        {
            Assert.That(actual, Is.Not.Null, expected.StateTypeId);
            var saved = new SimStateWriter(); var wanted = new SimStateWriter();
            actual.SaveState(saved); expected.SaveState(wanted);
            Assert.That(actual.StateTypeId, Is.EqualTo(expected.StateTypeId));
            Assert.That(actual.StateVersion, Is.EqualTo(expected.StateVersion));
            Assert.That(saved.ToArray(), Is.EqualTo(wanted.ToArray()), expected.StateTypeId);
        }

        [TestCase(null)]
        [TestCase("{ }")]
        [TestCase("{ \"searchTicks\": 13, \"suppressTicks\": 17 }")]
        public void FlatOptIn_DefaultsAndMemoryDependenciesKeepBytes(string ai)
        {
            ContentPack pack = Pack(ai);
            WorldRecipe recipe = Recipe(pack);
            var plain = new M6World(recipe, Map());
            var on = new M6World(recipe.WithAiRules(pack), Map());
            Assert.That(L4.Systems(on), Is.EqualTo(L4.Systems(plain)));
            for (int tick = 0; tick <= 10; tick++)
            {
                Assert.That(on.Hash, Is.EqualTo(plain.Hash), "tick " + tick);
                Assert.That(on.Sim.SaveRetainedSnapshot(), Is.EqualTo(plain.Sim.SaveRetainedSnapshot()), "tick " + tick);
                if (tick < 10) { on.Step(); plain.Step(); }
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Terrain_KeepsExistingRuleSource(bool enabled)
        {
            WorldRecipe recipe = Recipe(Pack(null)).WithTerrainRules(L4.Pack(enabled ? All : null));
            byte[] map = WcMapWriter.Write(Wp23.Make(80, 40, 0, 4 * SimFix.Scale, null, 2 * SimFix.Scale, (x, z) => 0));
            var plain = new M6World(recipe, map);
            var on = new M6World(recipe.WithAiRules(Pack(enabled ? null : All)), map);
            Assert.That(L4.Systems(on), Is.EqualTo(L4.Systems(plain)));
            for (int tick = 0; tick <= 60; tick++)
            {
                Assert.That(on.Hash, Is.EqualTo(plain.Hash), "tick " + tick);
                Assert.That(on.Sim.SaveRetainedSnapshot(), Is.EqualTo(plain.Sim.SaveRetainedSnapshot()), "tick " + tick);
                if (tick < 60) { on.Step(); plain.Step(); }
            }
        }
    }
}
