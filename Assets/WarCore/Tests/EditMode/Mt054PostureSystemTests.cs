using System;
using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class Mt054PostureSystemTests
    {
        [Test]
        public void StateVersionIsOne()
        {
            var f = new PostureFixture();
            Assert.That(f.System.StateVersion, Is.EqualTo(1));
        }

        [Test]
        public void ConstructorRejectsDefaultEventType()
        {
            var f = new PostureFixture();
            Assert.Throws<ArgumentException>(() => new PostureSystem(f.Sim.Entities, f.Postures,
                f.Positions, f.Colliders, f.Geometry, default(EventTypeId), 10));
        }

        [Test]
        public void RestoreValidationRejectsPendingTransitionAtCurrentTick()
        {
            var f = new PostureFixture();
            long task = f.System.BeginTransition(f.Sim.Context, f.Unit, 2, 1);
            // An open event tick stops the step after the clock advances but before scheduler dispatch.
            ((SimEventBuffer)f.Sim.Events).BeginTick(1);
            Assert.Throws<InvalidOperationException>(() => f.Sim.Step(default));
            Assert.That(f.Sim.Clock.Tick, Is.EqualTo(1));
            Assert.That(f.Sim.Scheduler.TryGetPending(task, out ScheduledTask pending), Is.True);
            Assert.That(pending.Event.Tick, Is.EqualTo(1));
            Assert.Throws<SimSnapshotException>(() => f.System.ValidateRestoredState(f.Sim.Context));
        }
    }
}
