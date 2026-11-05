using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    [Category("Partitions")]
    [Category("PartitionHashCompatibility")]
    public sealed class PartitionHashCompatibilityTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void OwnedSpatialAssemblyReproducesTheUnmodifiedHeadHashWhenStaticIsOmitted(bool schedulerV2)
        {
            // These constants were measured on d91f7b8 with the same configured Sim and geometry.
            ulong expected = schedulerV2 ? 0xE9310EBCB7B22835UL : 0x0602C97A793FC04BUL;
            var scene = new PartitionScene(2, 8);
            EntityId id = scene.Add(SimVec3.Metres(5, 0, 0), false, 2, 0);
            scene.Sim.CompleteSpatialBoundary();
            foreach (bool stationary in new[] { false, true })
            {
                scene.Sim.Entities.Set(scene.C, id, scene.Sim.Entities.Read(scene.C, id).WithStatic(stationary));
                scene.Sim.CompleteSpatialBoundary();
                var compatibility = new StateHash(ignoreColliderLayers: false);
                if (schedulerV2)
                    scene.Sim.WriteStateV2(compatibility);
                else
                    scene.Sim.WriteState(compatibility);
                Assert.That(compatibility.Value, Is.EqualTo(expected));
            }
            TestContext.WriteLine("S1 configured-Sim compatibility: schedulerV2={0}; static false/true; d91f7b8=0x{1:X16}; exact match.", schedulerV2, expected);
        }

        [Test]
        public void PendingStaticRequestsAreHashedRestoredAndCannotFollowReusedSlots()
        {
            var scene = new PartitionScene(2, 8);
            EntityId id = scene.Add(SimVec3.Metres(5, 0, 0), false, 2, 0);
            scene.Sim.CompleteSpatialBoundary();
            ulong initial = scene.Sim.StateHash();
            ulong initialV2 = scene.Sim.StateHashV2();
            scene.Sim.RequestColliderStatic(id, true);
            Assert.That(scene.Sim.StateHash(), Is.Not.EqualTo(initial));
            Assert.That(scene.Sim.StateHashV2(), Is.Not.EqualTo(initialV2));
            var compatible = new StateHash(ignoreColliderLayers: false);
            scene.Sim.WriteState(compatible);
            Assert.That(compatible.Value, Is.EqualTo(0x0602C97A793FC04BUL));
            ulong pending = scene.Sim.StateHash();
            byte[] saved = scene.Sim.SaveSnapshot();
            scene.Sim.RequestColliderStatic(id, false);
            Assert.That(scene.Sim.StateHash(), Is.Not.EqualTo(pending));
            scene.Sim.RestoreSnapshot(saved);
            Assert.That(scene.Sim.StateHash(), Is.EqualTo(pending));
            scene.Sim.Entities.Destroy(id);
            scene.Sim.CompleteSpatialBoundary();
            EntityId reused = scene.Add(SimVec3.Metres(5, 0, 0), false, 2, 0);
            Assert.That(reused.Id, Is.EqualTo(id.Id));
            Assert.That(reused.Generation, Is.Not.EqualTo(id.Generation));
            scene.Sim.Step(default);
            Assert.That(scene.Sim.Entities.Read(scene.C, reused).IsStatic, Is.False);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void MalformedPendingStaticRequestPayloadIsExplicitlyRejected(int fault)
        {
            var scene = new PartitionScene(2, 8);
            EntityId id = scene.Add(default, false, 2);
            scene.Sim.RequestColliderStatic(id, true);
            byte[] bytes = scene.Sim.SaveSnapshot();
            if (fault == 0)
                bytes[bytes.Length - 1] = 2;
            else if (fault == 1)
                bytes[bytes.Length - 13] = 127;
            else
                bytes[bytes.Length - 17] = 127;
            Assert.Throws<SimSnapshotException>(() => scene.Sim.RestoreSnapshot(bytes));
        }
    }
}
