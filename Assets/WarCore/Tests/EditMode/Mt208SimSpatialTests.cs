using System;
using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    [Category("Step2bA")]
    public sealed class Mt208SimSpatialTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void RepeatedConfigurationRejectsBeforeReplacingPublishedBodies(bool warmup)
        {
            var scene = new GridOwnershipTestFixture();
            scene.CreateBody(default);
            scene.InitializeIndex();
            if (warmup)
                scene.Sim.CompleteWarmup();

            Assert.Throws<InvalidOperationException>(() =>
                scene.Sim.ConfigureSpatial(scene.Positions, scene.Colliders, SimFix.FromInt(8), 256));
            Assert.That(scene.Sim.SpatialQuery.Sphere(default, SimFix.FromInt(1), new EntityId[scene.Capacity]),
                Is.EqualTo(1));
        }

        [Test]
        public void SpatialBoundaryRejectsDuringRunningTick()
        {
            var scene = new GridOwnershipTestFixture();
            scene.Sim.Register("boundary", new GridPhaseProbeSystem(10, context =>
                scene.Sim.CompleteSpatialBoundary()));

            Assert.Throws<InvalidOperationException>(() => scene.Sim.Step(default));
        }

        [Test]
        public void SpatialBoundaryRejectsAfterFaultedTick()
        {
            var scene = new GridOwnershipTestFixture();
            scene.Sim.Register("fault", new GridPhaseProbeSystem(10, context =>
                throw new InvalidOperationException("Tick failed.")));

            Assert.Throws<InvalidOperationException>(() => scene.Sim.Step(default));
            Assert.Throws<InvalidOperationException>(() => scene.Sim.CompleteSpatialBoundary());
        }

        [Test]
        public void DuplicateMovementRejectsAndCommitsFirstResult()
        {
            var scene = new GridOwnershipTestFixture();
            EntityId entity = scene.CreateBody(default);
            scene.Sim.Register("movement", new GridPhaseProbeSystem(20, context =>
            {
                context.SubmitMovement(entity, SimVec3.Metres(1, 0, 0));
                Assert.Throws<SimBoundaryException>(() =>
                    context.SubmitMovement(entity, SimVec3.Metres(2, 0, 0)));
            }), SimSystemPhase.Movement);

            scene.Sim.Step(default);
            Assert.That(scene.Store.Read(scene.Positions, entity).Value, Is.EqualTo(SimVec3.Metres(1, 0, 0)));
        }
    }
}
