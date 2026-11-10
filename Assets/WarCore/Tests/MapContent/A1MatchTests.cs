using NUnit.Framework;
using WarCore.Battle;

namespace WarCore.Tests
{
    public sealed class A1MatchTests
    {
        // Recorded before melee chasing on the same 9000-tick fixture.
        private const ulong OffFold = 0xC5DD0A803B8B3F24UL, OffFinal = 0x991489B8123FAF6EUL;
        private const string OffSnapshot = "70D9E6B8331D94CAD3C15E66F3BAE3833CDE6D2FF8739B4C70287A15EF51330E";

        [Test]
        public void A1_OffIsUnchanged()
        {
            M6World omitted = A1.World(null), zero = A1.World("{ \"meleeChaseRange\": 0 }");
            Assert.That(zero.Sim.SaveRetainedSnapshot(), Is.EqualTo(omitted.Sim.SaveRetainedSnapshot()));
            ulong fold = 14695981039346656037UL;
            for (int tick = 1; tick <= A1.MaxTicks; tick++)
            {
                Assert.That(omitted.Mode.IsFinished, Is.False, "before tick " + tick);
                Assert.That(zero.Mode.IsFinished, Is.False, "before tick " + tick);
                omitted.Step(); zero.Step();
                Assert.That(zero.Hash, Is.EqualTo(omitted.Hash), "tick " + tick);
                fold = (fold ^ omitted.Hash) * 1099511628211UL;
            }
            Assert.That(fold, Is.EqualTo(OffFold));
            Assert.That(omitted.Hash, Is.EqualTo(OffFinal));
            Assert.That(zero.Sim.SaveRetainedSnapshot(), Is.EqualTo(omitted.Sim.SaveRetainedSnapshot()));
            Assert.That(C1c.Sha256(omitted.Sim.SaveRetainedSnapshot()), Is.EqualTo(OffSnapshot));
            Assert.That(omitted.Mode.EndReason, Is.EqualTo(MatchEndReason.Draw));
            Assert.That(omitted.Mode.EndTick, Is.EqualTo(9000));
            Assert.That(new[] { A1.Deaths(omitted, 1), A1.Deaths(omitted, 2) }, Is.EqualTo(new[] { 0, 0 }));
        }

        [Test]
        public void A1_ChaseEndsTheMatch()
        {
            M6World w = A1.World("{ \"meleeChaseRange\": 80 }");
            while (!w.Mode.IsFinished && w.Sim.Clock.Tick < A1.MaxTicks) w.Step();
            Assert.That(w.Mode.IsFinished, Is.True);
            Assert.That(w.Mode.EndReason, Is.EqualTo(MatchEndReason.Exhausted));
            Assert.That(w.Mode.Winner, Is.EqualTo(1));
            Assert.That(w.Mode.EndTick, Is.EqualTo(628));
            Assert.That(w.Mode.EndTick, Is.LessThan(A1.MaxTicks));
            Assert.That(new[] { A1.Deaths(w, 1), A1.Deaths(w, 2) }, Is.EqualTo(new[] { 7, 10 }));
        }
    }
}
