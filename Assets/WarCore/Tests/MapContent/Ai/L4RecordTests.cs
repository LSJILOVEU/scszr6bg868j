using NUnit.Framework;
using WarCore.Battle;
using WarCore.Sim;

namespace WarCore.Tests
{
    // Recorded on the base code before the failed-slot memory existed: fixture A without an ai key must keep these exact
    // orders and hashes.
    public sealed class L4RecordTests
    {
        // Ticks 0..600 of fixture A (thinking interval 10, no ai key), every StateHash folded (L4.HashRun).
        internal const ulong AHashFold600 = 11984728949722066259UL;
        internal const ulong AHash600 = 8145834564478837919UL;

        [Test]
        public void FixtureA_HasOneSlot_StandableButUnreachable()
        {
            Assert.That(GuardSlots.Build(L4.ACenter, L4.APoint.Radius).Length, Is.EqualTo(1));
            M6World w = L4.A(10, null);
            Assert.That(L4.Moves(w).CanStandAt(L4.ACenter), Is.True, "the walled cell is standable");
            Assert.That(L4.Reachable(L4.Terrain(L4.Map(L4.AWalls)), L4.AStart, L4.ACenter), Is.False, "no route from the unit");
            Assert.That(L4.Reachable(L4.Terrain(L4.Map(L4.AWalls)), L4.AStart, L4.Cell(30, 10)), Is.True, "the open ground is connected");
        }

        // Acceptance 2, without an ai key: every thinking tick orders the walled slot again, 120 times in 1200 ticks.
        [Test]
        public void FixtureA_WithoutAiKey_OrdersTheWalledSlotEveryThinkingTick()
        {
            M6World w = L4.A(10, null);
            L4.Calls calls = L4.Observe(w);
            long t0 = L4.FirstThink(w, 10);
            w.Step(1200);
            var ticks = calls.TicksTo(L4.ACenter);
            Assert.That(t0, Is.InRange(1, 10));
            Assert.That(ticks.Count, Is.EqualTo(120));
            for (int k = 0; k < 120; k++) Assert.That(ticks[k], Is.EqualTo(t0 + 10 * k), "order " + k);
            Assert.That(calls.Orders.Count, Is.EqualTo(120), "no other orders");
        }

        // Acceptance 3(a): the per-tick hashes of ticks 0..600 without an ai key.
        [Test]
        public void FixtureA_WithoutAiKey_HashesAsRecorded()
        {
            M6World w = L4.A(10, null);
            ulong fold = L4.HashRun(w, 600);
            Assert.That(fold, Is.EqualTo(AHashFold600));
            Assert.That(w.Hash, Is.EqualTo(AHash600));
        }
    }
}
