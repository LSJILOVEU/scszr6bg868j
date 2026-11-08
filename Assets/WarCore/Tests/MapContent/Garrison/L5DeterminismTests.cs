using NUnit.Framework;
using WarCore.Interim;
using WarCore.Sim;

namespace WarCore.Tests
{
    // L5 acceptance 5 and 6 on fixture G: the same recipe in reverse creation order, and snapshots restored mid-run onto a
    // reversed physical allocation, give the same per-tick hash and the same bytes.
    public sealed class L5DeterminismTests
    {
        [Test]
        public void L5_ForwardAndReverseCreation_SameEveryTick_SameBytes()
        {
            M6World a = L5.World(), b = L5.World(reverse: true);
            Assert.That(b.Unit(1), Is.Not.EqualTo(a.Unit(1)), "physical allocation must actually change");
            ulong fa = L5.FoldSeed, fb = L5.FoldSeed;
            for (int tick = 0; tick <= L5.Ticks; tick++)
            {
                if (tick > 0) { a.Step(); b.Step(); }
                Assert.That(b.Hash, Is.EqualTo(a.Hash), "tick " + tick);
                fa = L5.Fold(fa, a.Hash); fb = L5.Fold(fb, b.Hash);
            }
            Assert.That(fb, Is.EqualTo(fa));
            Assert.That(b.Sim.SaveRetainedSnapshot(), Is.EqualTo(a.Sim.SaveRetainedSnapshot()));
            SimVec3[] pa = L5.Positions(a), pb = L5.Positions(b);
            for (int i = 0; i < pa.Length; i++) Assert.That(pb[i], Is.EqualTo(pa[i]), "unit " + L5.Table[i].key);
        }

        [TestCase(90)]
        [TestCase(600)]
        public void L5_SnapshotRestoredMidRun_ContinuesIdentically(int at)
        {
            M6World a = L5.World();
            a.Step(at);
            if (at == 90)
            {
                // Evidence that tick 90 is mid-walk: team 1 needs at least 123 ticks to reach any slot of its point.
                for (int i = 0; i < 10; i++)
                    Assert.That(a.Read<MoveIntent>(L5.Table[i].key).Active, Is.True, "team 1 unit " + L5.Table[i].key + " still walking");
                bool nonZero = false;
                foreach (var row in L5.Table) nonZero |= a.Read<MoveProgress>(row.key).BestDist2 != 0;
                Assert.That(nonZero, Is.True, "some unit carries a non-zero BestDist2 into the snapshot");
            }
            byte[] snapshot = a.Sim.SaveRetainedSnapshot();
            M6World c = L5.World();
            c.Sim.RestoreRetainedSnapshot(snapshot, reverseAllocation: true);
            Assert.That(c.Unit(1), Is.Not.EqualTo(a.Unit(1)), "physical allocation must actually change");
            Assert.That(c.Sim.SaveRetainedSnapshot(), Is.EqualTo(snapshot));
            Assert.That(c.Hash, Is.EqualTo(a.Hash));
            for (int tick = at + 1; tick <= L5.Ticks; tick++)
            {
                a.Step(); c.Step();
                Assert.That(c.Hash, Is.EqualTo(a.Hash), "tick " + tick);
            }
            Assert.That(c.Sim.SaveRetainedSnapshot(), Is.EqualTo(a.Sim.SaveRetainedSnapshot()));
        }
    }
}
