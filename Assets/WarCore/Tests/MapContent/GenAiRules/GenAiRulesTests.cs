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
    // GEN-AI-RULES, RC1-K2: a world reads the AI rules of its one pack, on a flat map as on a terrain map.
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
            .WithPack(pack).WithStandingOrders().WithRuleSet(MatchMode.TestNeverEndsId);

        private static byte[] Map(bool terrain = false) => WcMapWriter.Write(Wp23.Make(80, 40, 0, 4 * SimFix.Scale,
            Array.Empty<(int, int, int, int)>(), 2 * SimFix.Scale, terrain ? (Func<int, int, int>)((x, z) => 0) : null));

        private static void Step(M6World w) => w.Sim.Step(w.Sim.Clock.Tick == 0 ? P1.Command(SquadCommandKind.Hold, 2) : default);

        // A flat world whose pack names a chase range chases at its first think tick; under a pack without an AI section the
        // same unit stays.
        [Test]
        public void AFlatWorldChasesAtItsFirstThinkTick()
        {
            var on = new M6World(Recipe(Pack(Chase)), Map());
            var off = new M6World(Recipe(Pack(null)), Map());
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
        }

        // Every AI rule of the pack reaches its system, on a flat map and on a terrain map.
        [TestCase(false)]
        [TestCase(true)]
        public void EveryContentRuleIsAssembled(bool terrain)
        {
            var w = new M6World(Recipe(Pack(All)), Map(terrain));
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

        // An AI section that names nothing is no AI section: the same bytes at every tick, and none of the systems.
        [TestCase(false)]
        [TestCase(true)]
        public void AnEmptyAiSectionKeepsBytes(bool terrain)
        {
            var off = new M6World(Recipe(Pack(null)), Map(terrain));
            var on = new M6World(Recipe(Pack("{ }")), Map(terrain));
            Assert.That(on.System<FailedSlotMemory>(), Is.Null);
            Assert.That(on.System<TargetMemory>(), Is.Null);
            Assert.That(on.System<TargetSearch>(), Is.Null);
            Assert.That(on.System<TargetSuppress>(), Is.Null);
            Assert.That(on.System<ReactionFire>(), Is.Null);
            for (int tick = 0; tick <= 30; tick++)
            {
                if (tick > 0) { Step(off); Step(on); }
                Assert.That(on.Hash, Is.EqualTo(off.Hash), "tick " + tick);
                Assert.That(on.Sim.SaveRetainedSnapshot(), Is.EqualTo(off.Sim.SaveRetainedSnapshot()), "tick " + tick);
                Assert.That(on.Read<Position>(1).Value, Is.EqualTo(Start), "tick " + tick);
                Assert.That(on.Read<MoveIntent>(1).Active, Is.False, "tick " + tick);
            }
        }
    }
}
