using System;
using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class Mt205SimBoundaryTests
    {
        [Test]
        public void ExhaustedClockRejectsAdvanceWithoutWrapping()
        {
            var clock = new SimClock();
            clock.Restore(long.MaxValue);

            Assert.Throws<SimBoundaryException>(() => clock.Advance());
            Assert.That(clock.Tick, Is.EqualTo(long.MaxValue));
        }

        [Test]
        public void MovementSubmissionOutsideMovementPhaseIsRejected()
        {
            var scene = new GridOwnershipTestFixture();
            EntityId entity = scene.CreateBody(default);

            Assert.Throws<InvalidOperationException>(() =>
                scene.Sim.Context.SubmitMovement(entity, SimVec3.Metres(1, 0, 0)));
            Assert.That(scene.Store.Read(scene.Positions, entity).Value, Is.EqualTo(default(SimVec3)));
        }

        [Test]
        public void StateHashDuringTickIsRejected()
        {
            var sim = Fixtures.Empty(1);
            int checkedBoundaries = 0;
            sim.Register("hash-probe", new GridPhaseProbeSystem(1, context =>
            {
                Assert.Throws<InvalidOperationException>(() => Fixtures.Hash(sim));
                checkedBoundaries++;
            }));

            sim.Step(default);

            Assert.That(checkedBoundaries, Is.EqualTo(1));
            Assert.That(sim.Clock.Tick, Is.EqualTo(1));
        }
    }
}
