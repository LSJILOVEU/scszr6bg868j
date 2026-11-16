using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    [Category("Step2bA")]
    public sealed class Mt214SpatialMotionTests
    {
        [Test]
        public void DetachStopsFollowingAndPreservesPendingPosition()
        {
            var scene = new GridOwnershipTestFixture();
            EntityId host = scene.CreateBody(default);
            EntityId passenger = scene.CreateBody(default);
            scene.Sim.Attach(passenger, host, SimVec3.Metres(0, 4, 0));
            scene.Sim.RequestPosition(passenger, SimVec3.Metres(32, 0, 0));

            scene.Sim.Detach(passenger);
            scene.Sim.Step(default);

            Assert.That(scene.Store.Read(scene.Positions, passenger).Value, Is.EqualTo(SimVec3.Metres(32, 0, 0)));
        }

        [Test]
        public void StableWorldRejectsPositionRequestWithoutStableKey()
        {
            var sim = StableSnapshotTests.Scene(false);
            EntityId entity = sim.Entities.Resolve(new EntityLink(StableEntityKey.Placed(1, 0).Id));

            Assert.Throws<SimBoundaryException>(() => sim.RequestPosition(entity, SimVec3.Metres(7, 0, 0)));
        }

        [Test]
        public void PositionRequestRejectsLiveEntityWithoutPosition()
        {
            var scene = new GridOwnershipTestFixture();
            EntityId entity = scene.Store.Create();

            Assert.Throws<SimBoundaryException>(() => scene.Sim.RequestPosition(entity, default));
        }
    }
}
