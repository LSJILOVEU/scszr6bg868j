using System;
using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    [Category("Partitions")]
    public sealed class Mt079SpatialPartitionRequestsTests
    {
        [Test]
        public void RequestAtReservedCapacityThrowsBoundaryException()
        {
            var scene = new PartitionScene(1, 8);
            scene.Add(default, false, 2);
            scene.Sim.Entities.Reserve(2);
            EntityId entity = scene.Add(default, false, 2);
            Assert.That(entity.Id, Is.EqualTo(1));
            Assert.Throws<SimBoundaryException>(() => scene.Sim.RequestColliderStatic(entity, true));
        }

        [TestCase(false, (byte)0)]
        [TestCase(true, (byte)1)]
        public void PendingRequestHashesCanonicalBooleanByte(bool stationary, byte expectedByte)
        {
            var scene = new PartitionScene(2, 8);
            EntityId entity = scene.Add(default, false, 2);
            var expected = new StateHash();
            scene.Sim.WriteAuthoritativeState(expected);
            expected.Add("warcore.static-requests-v1");
            expected.Add(1);
            entity.WriteState(expected);
            expected.Add(expectedByte);

            scene.Sim.RequestColliderStatic(entity, stationary);
            var actual = new StateHash();
            scene.Sim.WriteAuthoritativeState(actual);
            Assert.That(actual.Value, Is.EqualTo(expected.Value));
        }

        [Test]
        public void SnapshotRejectsAdjacentRequestsForTheSameLiveEntity()
        {
            var scene = new PartitionScene(2, 8);
            EntityId first = scene.Add(default, false, 2);
            EntityId second = scene.Add(default, false, 2);
            scene.Sim.RequestColliderStatic(first, true);
            scene.Sim.RequestColliderStatic(second, false);
            byte[] bytes = scene.Sim.SaveSnapshot();
            // Each tail entry contains a 12-byte owner and a one-byte flag.
            Array.Copy(bytes, bytes.Length - 26, bytes, bytes.Length - 13, 12);
            Assert.Throws<SimSnapshotException>(() => scene.Sim.RestoreSnapshot(bytes));
        }
    }
}
