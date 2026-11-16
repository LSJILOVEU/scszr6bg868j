using System;
using NUnit.Framework;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    [TestFixture, Category("StableIdentity")]
    public sealed class Mt096StablePostureTests
    {
        private static PostureSystem SystemFor(Simulation sim) => new PostureSystem(sim.Entities,
            sim.Entities.Resolve<Posture>(), sim.Entities.Resolve<Position>(), sim.Entities.Resolve<Collider>(),
            sim.SpatialIndex.Postures, new EventTypeId(400), 1);

        [Test]
        public void ZeroDurationIsRejectedEvenWhenTargetIsCurrentPosture()
        {
            Simulation sim = StableSystemTests.Make(false);
            var target = new EntityLink(StableEntityKey.Placed(1, 0).Id);
            Assert.Throws<SimBoundaryException>(() => SystemFor(sim).BeginStableTransition(sim.Context, target, 0, 0, 99));
        }

        [Test]
        public void TransitionStartsAndCompletesAtExactDueTick()
        {
            Simulation sim = StableSystemTests.Make(false);
            EntityId target = sim.Entities.Resolve(new EntityLink(StableEntityKey.Placed(1, 1).Id));
            var postures = sim.Entities.Resolve<Posture>();
            Assert.That(sim.Entities.TryGet(postures, target, out Posture posture), Is.True);
            Assert.That(posture.CurrentId, Is.EqualTo(0));
            Assert.That(posture.TargetId, Is.EqualTo(1));
            Assert.That(posture.DueTick, Is.EqualTo(5));
            Assert.That(posture.IsTransitioning, Is.True);
            TaskKey key = posture.StableTask;
            for (int tick = 1; tick <= 5; tick++)
            {
                sim.Step(default);
                posture = sim.Entities.Read(postures, target);
                Assert.That(posture.CurrentId, Is.EqualTo(tick < 5 ? 0 : 1));
                Assert.That(posture.IsTransitioning, Is.EqualTo(tick < 5));
            }
            Assert.That(posture.DueTick, Is.EqualTo(0));
            Assert.That(sim.Scheduler.TryGetPendingStable(key, out _), Is.False);
        }

        [Test]
        public void RestoreValidationRejectsPendingTaskAtCurrentTick()
        {
            Simulation sim = StableSystemTests.Make(false);
            EntityId target = sim.Entities.Resolve(new EntityLink(StableEntityKey.Placed(1, 1).Id));
            Assert.That(sim.Entities.TryGet(sim.Entities.Resolve<Posture>(), target, out Posture posture), Is.True);
            Assert.That(posture.DueTick, Is.EqualTo(5));
            for (int tick = 1; tick < 5; tick++)
                sim.Step(default);

            ((SimEventBuffer)sim.Events).BeginTick(5);
            Assert.Throws<InvalidOperationException>(() => sim.Step(default));
            Assert.That(sim.Clock.Tick, Is.EqualTo(5));
            Assert.That(sim.Scheduler.TryGetPendingStable(posture.StableTask, out _), Is.True);
            Assert.Throws<SimSnapshotException>(() => SystemFor(sim).ValidateRestoredState(sim.Context));
        }
    }
}
