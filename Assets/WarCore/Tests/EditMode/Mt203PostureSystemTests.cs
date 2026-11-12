using System;
using NUnit.Framework;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    public sealed class Mt203PostureSystemTests
    {
        [Test]
        public void CancellationRejectsAnotherSimulationContext()
        {
            var f = new PostureFixture();
            var other = new PostureFixture();
            long task = f.System.BeginTransition(f.Sim.Context, f.Unit, 2, 1);

            Assert.Throws<ArgumentException>(() => f.System.CancelTransition(other.Sim.Context, f.Unit));
            Assert.That(f.Sim.Entities.Get(f.Postures, f.Unit), Is.EqualTo(new Posture(1, 2, 1, task)));
        }

        [Test]
        public void StableCancellationRemovesThePendingTransitionTask()
        {
            var sim = new Simulation(12, config: new SimConfig(entityCapacity: 8),
                identityMode: IdentityMode.StableWorld, identityCapacity: 16);
            var positions = sim.Entities.Register<Position>("position", new PositionSerializer());
            var colliders = sim.Entities.Register<Collider>("collider", new ColliderSerializer());
            var postures = sim.Entities.Register<Posture>("posture", new PostureSerializer());
            EntityId unit = sim.Entities.Create(StableEntityKey.Placed(1, 0));
            sim.Entities.Set(positions, unit, new Position(SimVec3.Metres(0, 2, 0)));
            sim.Entities.Set(colliders, unit, new Collider(SimFix.Ratio(1, 4), SimFix.FromInt(3)));
            sim.Entities.Set(postures, unit, new Posture(1));
            var geometry = new PostureGeometry(sim.Entities, postures, PostureFixture.Definitions());
            var system = new PostureSystem(sim.Entities, postures, positions, colliders, geometry,
                new EventTypeId(400), 1);
            TaskKey task = system.BeginStableTransition(sim.Context, sim.Entities.Link(unit), 2, 5, 99);

            Assert.That(sim.Scheduler.TryGetPendingStable(task, out _), Is.True);
            Assert.That(system.CancelTransition(sim.Context, unit), Is.True);
            Assert.That(sim.Scheduler.TryGetPendingStable(task, out _), Is.False);
            Assert.That(sim.Entities.Get(postures, unit), Is.EqualTo(new Posture(1)));
        }

        [TestCase(2, 2)]
        [TestCase(1, 3)]
        public void TransitionRejectsMismatchedDeadlineOrPayload(int dueTick, int targetId)
        {
            var f = new PostureFixture();
            long task = f.System.BeginTransition(f.Sim.Context, f.Unit, 2, 1);
            var posture = new Posture(1, targetId, dueTick, task);
            f.Sim.Entities.Set(f.Postures, f.Unit, posture);

            Assert.Throws<InvalidOperationException>(() => f.Sim.Step(default));
            Assert.That(f.Sim.Entities.Get(f.Postures, f.Unit), Is.EqualTo(posture));
        }
    }
}
