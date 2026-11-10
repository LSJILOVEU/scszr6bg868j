using NUnit.Framework;

namespace WarCore.Tests
{
    public sealed class FairEOnTests
    {
        [TestCase(41UL, 1, 2068L, 0xFB076F56436B98A9UL)]
        [TestCase(42UL, 1, 2430L, 0xDBA601F738614D3CUL)]
        [TestCase(43UL, 2, 2077L, 0x05AE5E58B4262524UL)]
        [TestCase(44UL, 1, 2130L, 0x711FC31CFF2EB2F3UL)]
        [TestCase(45UL, 2, 2111L, 0xD342CB6EE380EC6BUL)]
        public void FairE_On(ulong seed, int winner, long tick, ulong hash)
        {
            M6World w = FairE.World("control", seed, true);
            FairE.Finish(w);
            Assert.That((w.Match.Winner, w.Match.EndTick), Is.EqualTo((winner, tick)));
            Assert.That(w.Hash, Is.EqualTo(hash));
        }

        [TestCase("control", 1, 2068L)]
        [TestCase("M6", 1, 2600L)]
        public void FairE_OnReplayAndRestore(string layout, int winner, long endTick)
        {
            M6World straight = FairE.World(layout, 41, true), repeat = FairE.World(layout, 41, true), restored = FairE.World(layout, 41, true);
            Assert.That(repeat.Hash, Is.EqualTo(straight.Hash));
            for (long tick = 1; tick <= endTick; tick++)
            {
                straight.Step(); repeat.Step();
                Assert.That(repeat.Hash, Is.EqualTo(straight.Hash), "repeat tick " + tick);
                if (tick == 300)
                {
                    byte[] snapshot = straight.Sim.SaveRetainedSnapshot();
                    restored.Sim.RestoreRetainedSnapshot(snapshot);
                    Assert.That(restored.Sim.SaveRetainedSnapshot(), Is.EqualTo(snapshot));
                }
                else if (tick > 300) restored.Step();
                if (tick >= 300) Assert.That(restored.Hash, Is.EqualTo(straight.Hash), "restored tick " + tick);
            }
            Assert.That((straight.Match.Winner, straight.Match.EndTick), Is.EqualTo((winner, endTick)));
            Assert.That((repeat.Match.Winner, repeat.Match.EndTick), Is.EqualTo((winner, endTick)));
            Assert.That((restored.Match.Winner, restored.Match.EndTick), Is.EqualTo((winner, endTick)));
        }
    }
}
