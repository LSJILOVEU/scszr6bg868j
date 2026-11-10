using System;
using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    [TestFixture, Category("Mt086SpawnCapture")]
    public sealed class Mt086SpawnCaptureTests : IComponentSerializer<Position>
    {
        private EntityStore store;
        private EntityId entity;

        [Test]
        public void SpawnValuesDefaultOwnershipIsZero()
        {
            var values = new SpawnValues(SimVec3.Metres(3, 4, 5));
            Assert.That(values.Ownership, Is.EqualTo(0UL));
        }

        [Test]
        public void SpawnValuesRejectsPositionOutsideWorld()
        {
            var position = SimVec3.Metres(SimVec3.WorldLimitMetres + 1, 0, 0);
            Assert.Throws<ArgumentOutOfRangeException>(() => new SpawnValues(position));
        }

        [Test]
        public void SpawnValuesRejectsFacingOutsideWorld()
        {
            var facing = SimVec3.Metres(0, 0, -SimVec3.WorldLimitMetres - 1);
            Assert.Throws<ArgumentOutOfRangeException>(() => new SpawnValues(default, facing));
        }

        [Test]
        public void CaptureIgnoresPositionRemovedDuringLegacyLoad()
        {
            store = new EntityStore(1);
            var positions = store.Register<Position>("position", this);
            entity = store.Create();
            store.Set(positions, entity, new Position(SimVec3.Metres(3, 4, 5)));
            var writer = new SimStateWriter();
            store.SaveEntities(writer);
            store.LoadEntities(new SimStateReader(writer.ToArray()));

            Assert.That(store.TryGet(positions, entity, out Position _), Is.False);
            Assert.That(store.CaptureSpawnValues(entity).Position, Is.EqualTo(default(SimVec3)));
        }

        int IComponentSerializer<Position>.TypeId => 1;
        int IComponentSerializer<Position>.Version => 1;
        void IComponentSerializer<Position>.Write(SimStateWriter writer, in Position value)
            => SnapshotValues.Vector(writer, value.Value);
        Position IComponentSerializer<Position>.Read(SimStateReader reader)
        {
            var value = new Position(SnapshotValues.Vector(reader));
            store.Remove<Position>(entity);
            return value;
        }
    }
}
