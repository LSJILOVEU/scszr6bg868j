using NUnit.Framework;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class Mt160SimSessionTests
    {
        [Test]
        public void RefreshView_DropsPartialTickBeforeNextAdvance()
        {
            SimSession session = SimSession.Create(M1TestWorld.Probe());
            Assert.That(session.Advance(50000), Is.EqualTo(1));
            Assert.That(session.Alpha, Is.EqualTo(0.5f));

            session.RefreshView();

            Assert.That(session.Alpha, Is.Zero);
            Assert.That(session.Previous, Is.SameAs(session.Current));
            Assert.That(session.Advance(33333), Is.Zero);
            Assert.That(session.Tick, Is.EqualTo(1));
            Assert.That(session.Advance(1), Is.EqualTo(1));
            Assert.That(session.Tick, Is.EqualTo(2));
        }

        [Test]
        public void MoveTo_PositiveXWorldBoundary_IsAcceptedAndExecuted()
        {
            SimSession session = SimSession.Create(M1TestWorld.Probe());
            long limitMm = SimVec3.WorldLimitMetres * 1000L;
            BridgeCommand command = BridgeCommand.MoveTo(1, 1, limitMm, 0);

            Assert.DoesNotThrow(() => session.Enqueue(command));
            session.StepOnce();

            Assert.That(session.Tick, Is.EqualTo(1));
            Assert.That(session.CommandLog, Is.EqualTo(new[] { command }));
        }
    }
}
