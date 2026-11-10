using System;
using NUnit.Framework;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    [Category("Step2bA")]
    public sealed class Mt065SimSpatialTests
    {
        [Test]
        public void ConfigurationAfterWarmupLeavesSpatialQueryUnbound()
        {
            var sim = new Simulation(1, config: new SimConfig(entityCapacity: 2));
            var positions = sim.Entities.Register<Position>("position", new PositionSerializer());
            var colliders = sim.Entities.Register<Collider>("collider", new ColliderSerializer());
            sim.CompleteWarmup();

            Assert.Throws<InvalidOperationException>(() =>
                sim.ConfigureSpatial(positions, colliders, SimFix.FromInt(8)));
            Assert.That(sim.SpatialQuery, Is.Null);
            Assert.That(sim.Context.SpatialQuery, Is.Null);
        }

        [Test]
        public void BoundaryDuringRequestsPreservesPendingDestructionUntilPublication()
        {
            var scene = new GridOwnershipTestFixture();
            EntityId entity = scene.CreateBody(SimVec3.Metres(4, 4, 4));
            scene.InitializeIndex();
            scene.Sim.Register("boundary-probe", new GridPhaseProbeSystem(10, context =>
            {
                scene.Store.Destroy(entity);
                Assert.Throws<InvalidOperationException>(() => scene.Sim.CompleteSpatialBoundary());
                Assert.That(scene.Store.PendingDestroyCount, Is.EqualTo(1));
                Assert.That(scene.Grid.MembershipCount, Is.EqualTo(1));
            }), SimSystemPhase.Requests);

            scene.Sim.Step(default);

            Assert.That(scene.Store.PendingDestroyCount, Is.Zero);
            Assert.That(scene.Grid.MembershipCount, Is.Zero);
        }

        [Test]
        public void FailedLargerRestoreRejectsTheFirstEntityBeyondMovementCapacity()
        {
            var scene = new GridOwnershipTestFixture(1);
            EntityId mover = scene.CreateBody(default);
            scene.Store.Set(scene.Movements, mover, new Movement(MoveMode.Water, default));
            scene.AddMovement();

            var source = new Simulation(1, config: scene.Sim.Config);
            source.Entities.Reserve(2);
            var positions = source.Entities.Register<Position>("position", new PositionSerializer());
            var colliders = source.Entities.Register<Collider>("collider", new ColliderSerializer());
            var movements = source.Entities.Register<Movement>("movement", new MovementSerializer());
            source.ConfigureSpatial(positions, colliders, SimFix.FromInt(8), 256);
            var navigation = new NavProviderRegistry();
            navigation.Register(MoveMode.Ground, new FlatGroundNavProvider());
            source.Register("movement", new MovementSystem(source.Entities, positions, movements, navigation, 20, colliders));
            source.Entities.Create();
            EntityId boundary = source.Entities.Create();
            byte[] saved = source.SaveSnapshot();

            Assert.Throws<InvalidOperationException>(() => scene.Sim.Step(default));
            // Close the failed event tick so restoration can reach the capacity check.
            ((SimEventBuffer)scene.Sim.Events).EndTick();
            Assert.Throws<SimBoundaryException>(() => scene.Sim.RestoreSnapshot(saved));
            Assert.That(scene.Store.Capacity, Is.EqualTo(2));
            Assert.That(boundary.Id, Is.EqualTo(scene.Capacity));
            Assert.That(scene.Store.IsAlive(boundary), Is.True);
            Assert.Throws<SimBoundaryException>(() => scene.Sim.Context.SubmitMovement(boundary, default));
        }

        [Test]
        public void InvalidMovementLeavesBufferAvailableForValidRetry()
        {
            var scene = new GridOwnershipTestFixture();
            EntityId entity = scene.CreateBody(default);
            SimVec3 destination = SimVec3.Metres(4, 0, 0);
            scene.Sim.Register("movement-probe", new GridPhaseProbeSystem(20, context =>
            {
                Assert.Throws<ArgumentOutOfRangeException>(() =>
                    context.SubmitMovement(entity, SimVec3.Metres(SimVec3.WorldLimitMetres + 1, 0, 0)));
                context.SubmitMovement(entity, destination);
                Assert.That(scene.Store.Read(scene.Positions, entity).Value, Is.EqualTo(default(SimVec3)));
            }), SimSystemPhase.Movement);

            scene.Sim.Step(default);

            Assert.That(scene.Store.Read(scene.Positions, entity).Value, Is.EqualTo(destination));
        }
    }
}
