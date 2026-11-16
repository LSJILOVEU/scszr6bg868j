using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    [Category("Step2bA")]
    public sealed class Mt078SpatialMotionTests
    {
        [Test]
        public void PositionRequestSerializerKeepsSnapshotTypeId()
        {
            Assert.That(new SpatialPositionRequestSerializer().TypeId, Is.EqualTo(12));
        }

        [Test]
        public void AttachmentSerializerKeepsSnapshotTypeId()
        {
            Assert.That(new SpatialAttachmentSerializer().TypeId, Is.EqualTo(13));
        }

        [Test]
        public void LastSlotPassengerFollowsHostOnSuccessiveTicks()
        {
            var scene = new GridOwnershipTestFixture(capacity: 2);
            EntityId host = scene.CreateBody(default);
            EntityId passenger = scene.CreateBody(SimVec3.Metres(0, 4, 0));
            Assert.That(passenger.Id, Is.EqualTo(scene.Store.Capacity - 1));
            scene.Sim.Attach(passenger, host, SimVec3.Metres(0, 4, 0));
            scene.InitializeIndex();

            for (int tick = 1; tick <= 2; tick++)
            {
                scene.Sim.RequestPosition(host, SimVec3.Metres(8 * tick, 0, 0));
                scene.Sim.Step(default);
                Assert.That(scene.Store.Read(scene.Positions, host).Value, Is.EqualTo(SimVec3.Metres(8 * tick, 0, 0)));
                Assert.That(scene.Store.Read(scene.Positions, passenger).Value, Is.EqualTo(SimVec3.Metres(8 * tick, 4, 0)));
            }
        }
    }
}
