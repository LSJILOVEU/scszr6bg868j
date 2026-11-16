using NUnit.Framework;
using WarCore.Battle;
using WarCore.Interim;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class FairEOnTests
    {
        [TestCase(false, 2)]
        [TestCase(true, 1)]
        public void FairE_MeleeChaseUsesArbitration(bool enabled, int orders)
        {
            string ai = "{ \"meleeChaseRange\": 60, \"failedSlotTicks\": 100, \"fairDecisions\": " + (enabled ? "true" : "false") + " }";
            var units = new[] { A1.Unit(1, 1, 10, 18), A1.Unit(2, 1, 10, 22), A1.Unit(3, 2, 40, 20, melee: -1) };
            SimVec3 enemy = SimVec3.Metres(40, 0, 20);
            var recipe = A1.Recipe(ai, units).WithMatch(M6World.Pack(
                new[] { new ControlPointDef(1, enemy, SimFix.FromInt(8)) }, think: 1));
            var w = new M6World(recipe, A1.Map());
            var before = new SimStateWriter(); w.System<FailedSlotMemory>().SaveState(before);
            w.Step();
            int active = 0;
            for (ulong key = 1; key <= 2; key++)
            {
                MoveIntent intent = w.Read<MoveIntent>(key);
                if (!intent.Active) continue;
                active++;
                Assert.That(intent.Target, Is.EqualTo(enemy));
            }
            Assert.That(active, Is.EqualTo(orders));
            var after = new SimStateWriter(); w.System<FailedSlotMemory>().SaveState(after);
            Assert.That(after.ToArray(), Is.EqualTo(before.ToArray()));
        }

        [TestCase(41UL, 1, 2068L)]
        [TestCase(42UL, 1, 2430L)]
        [TestCase(43UL, 2, 2077L)]
        [TestCase(44UL, 1, 2130L)]
        [TestCase(45UL, 2, 2111L)]
        public void FairE_OnFixedOutcomesAndRepeat(ulong seed, int winner, long endTick)
        {
            M6World first = FairEFixture.Create("control", seed, true), repeat = FairEFixture.Create("control", seed, true);
            Assert.That(repeat.Hash, Is.EqualTo(first.Hash));
            while (!first.Sim.IsFinished)
            {
                first.Step(); repeat.Step();
                Assert.That(repeat.Hash, Is.EqualTo(first.Hash), "tick " + first.Sim.Clock.Tick);
            }
            Assert.That(first.Match.Winner, Is.EqualTo(winner));
            Assert.That(first.Match.EndTick, Is.EqualTo(endTick));
            Assert.That(repeat.Match.Winner, Is.EqualTo(winner));
            Assert.That(repeat.Match.EndTick, Is.EqualTo(endTick));
        }

        [TestCase("control")]
        [TestCase("M6")]
        public void FairE_OnSaveRestoreContinuesWithEveryTickHash(string layout)
        {
            M6World full = FairEFixture.Create(layout, 41, true);
            full.Step(300);
            byte[] snapshot = full.Sim.SaveRetainedSnapshot();
            M6World restored = FairEFixture.Create(layout, 41, true);
            restored.Sim.RestoreRetainedSnapshot(snapshot);
            Assert.That(restored.Hash, Is.EqualTo(full.Hash));
            Assert.That(restored.Sim.SaveRetainedSnapshot(), Is.EqualTo(snapshot));
            while (!full.Sim.IsFinished)
            {
                full.Step(); restored.Step();
                Assert.That(restored.Hash, Is.EqualTo(full.Hash), "tick " + full.Sim.Clock.Tick);
            }
            Assert.That(restored.Match.Winner, Is.EqualTo(full.Match.Winner));
            Assert.That(restored.Match.EndTick, Is.EqualTo(full.Match.EndTick));
        }
    }
}
