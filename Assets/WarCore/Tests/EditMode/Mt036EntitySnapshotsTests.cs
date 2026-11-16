using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class Mt036EntitySnapshotsTests
    {
        [Test]
        public void DeadSlotWithComponentIsRejected()
        {
            var store = new EntityStore(1);
            store.Register<Health>("health", new HealthSerializer());
            var writer = new SimStateWriter();
            writer.Int32(1);
            writer.Bool(false);
            writer.Int32(1);
            writer.Bool(false);
            writer.UInt64(1);
            writer.Int32(1);
            writer.Int32(0);
            var components = new SimStateWriter();
            components.Bool(true);
            components.Int32(37);
            writer.Bytes(components.ToArray());

            var error = Assert.Throws<SimSnapshotException>(() => store.LoadEntities(new SimStateReader(writer.ToArray())));
            Assert.That(error.Message, Is.EqualTo("A dead entity cannot own a component."));
        }

        [Test]
        public void ZeroCapacityPayloadIsRejected()
        {
            var store = new EntityStore(1);
            var writer = new SimStateWriter();
            writer.Int32(0);
            writer.Bool(false);
            writer.Int32(0);
            writer.Int32(0);

            var error = Assert.Throws<SimSnapshotException>(() => store.LoadEntities(new SimStateReader(writer.ToArray())));
            Assert.That(error.Message, Is.EqualTo("Invalid entity capacity."));
        }

        [Test]
        public void DuplicateFreeSlotWithValidCountAndHeapIsRejected()
        {
            var store = new EntityStore(2);
            var writer = new SimStateWriter();
            writer.Int32(2);
            writer.Bool(false);
            writer.Int32(2);
            writer.Bool(false);
            writer.UInt64(1);
            writer.Bool(false);
            writer.UInt64(1);
            writer.Int32(2);
            writer.Int32(0);
            writer.Int32(0);

            var error = Assert.Throws<SimSnapshotException>(() => store.LoadEntities(new SimStateReader(writer.ToArray())));
            Assert.That(error.Message, Is.EqualTo("Invalid free entity slot."));
        }
    }
}
