using NUnit.Framework;
using WarCore.Presentation;

namespace WarCore.Tests
{
    public sealed class M1ProbeTests
    {
        [Test]
        public void M1_MoveTo_MovesAtThreeMetresPerSecondThroughInput()
        {
            SimSession session = SimSession.Create(M1TestWorld.Probe());
            Assert.That(M1TestWorld.IsAt(session, 0, 0), Is.True);
            session.StepOnce();
            Assert.That(M1TestWorld.IsAt(session, 0, 0), Is.True);
            session.Enqueue(BridgeCommand.MoveTo(2, 1, 10000, 0));
            session.StepOnce();
            Assert.That(session.Tick, Is.EqualTo(2));
            Assert.That(M1TestWorld.XMm(session), Is.EqualTo(99));
            for (int i = 1; i < 30; i++)
                session.StepOnce();
            Assert.That(M1TestWorld.XMm(session), Is.EqualTo(2999));
            Assert.That(M1TestWorld.ZMm(session), Is.Zero);
        }

        [Test]
        public void M1_SameRecipe_HasIdenticalHashesEveryTick()
        {
            SimSession first = SimSession.Create(M1TestWorld.Probe(41));
            SimSession second = SimSession.Create(M1TestWorld.Probe(41));
            first.Enqueue(BridgeCommand.MoveTo(1, 1, 10000, 0));
            second.Enqueue(BridgeCommand.MoveTo(1, 1, 10000, 0));
            first.Enqueue(BridgeCommand.MoveTo(150, 1, 10000, 10000));
            second.Enqueue(BridgeCommand.MoveTo(150, 1, 10000, 10000));
            Assert.That(first.StateHash, Is.EqualTo(second.StateHash));
            for (int tick = 1; tick <= 600; tick++)
            {
                first.StepOnce();
                second.StepOnce();
                Assert.That(first.Tick, Is.EqualTo(tick));
                Assert.That(second.StateHash, Is.EqualTo(first.StateHash), "Tick " + tick);
            }
            Assert.That(M1TestWorld.IsAt(first, 10000, 10000), Is.True);
        }

        [TestCase(10000, 0)]
        [TestCase(-10001, 7333)]
        public void M1_Arrival_EqualsTargetRawBitsAndStaysThere(long xMm, long zMm)
        {
            SimSession session = SimSession.Create(M1TestWorld.Probe());
            session.Enqueue(BridgeCommand.MoveTo(1, 1, xMm, zMm));
            for (int tick = 0; tick < 200; tick++)
                session.StepOnce();
            Assert.That(M1TestWorld.IsAt(session, xMm, zMm), Is.True);
            for (int tick = 0; tick < 30; tick++)
            {
                session.StepOnce();
                Assert.That(M1TestWorld.IsAt(session, xMm, zMm), Is.True);
            }
        }
    }
}
