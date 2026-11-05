using System;
using NUnit.Framework;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    public sealed class LayerStateTests
    {
        [Test]
        public void DefaultsIncludeZeroInitializedStructsAndExplicitZeroRemainsDistinct()
        {
            Assert.That(default(Collider).Layer, Is.EqualTo(1u));
            Assert.That(new Collider().Layer, Is.EqualTo(1u));
            Assert.That(new Collider(SimFix.FromInt(1), SimFix.FromInt(2)).Layer, Is.EqualTo(1u));
            Assert.That(default(Collider).WithLayer(0).Layer, Is.Zero);
            Assert.That(default(SpatialRequest).LayerMask, Is.EqualTo(uint.MaxValue));
            Assert.That(LayerQueryFixture.Request(SpatialQueryKind.Sphere).LayerMask, Is.EqualTo(uint.MaxValue));
            Assert.That(new SpatialRequest(SpatialQueryKind.Sphere, default, default, default, default, default, 0, 0).LayerMask, Is.Zero);
            Assert.That(Simulation.SnapshotVersion, Is.EqualTo(4));
            Assert.That(new ColliderSerializer().Version, Is.EqualTo(3));
        }

        [TestCase(0u)]
        [TestCase(1u)]
        [TestCase(0x80000000u)]
        [TestCase(uint.MaxValue)]
        public void ColliderHashIsV1BytesFollowedByEightZeroExtendedLayerBytes(uint layer)
        {
            Collider collider = Example(layer);
            byte[] expected = HashBytes(layer);
            for (int i = 28; i < 32; i++)
                Assert.That(expected[i], Is.Zero);
            var production = new StateHash(ignoreColliderLayers: false);
            collider.WriteState(production);
            Assert.That(production.Value, Is.EqualTo(Fnv(expected, 32)));
            var ignored = new StateHash(ignoreColliderLayers: true);
            collider.WriteState(ignored);
            Assert.That(ignored.Value, Is.EqualTo(Fnv(expected, 24)));
            Assert.That(ignored.Value, Is.Not.EqualTo(production.Value));
            if ((layer & 0x80000000u) != 0)
            {
                for (int i = 28; i < 32; i++)
                    expected[i] = 255;
                Assert.That(production.Value, Is.Not.EqualTo(Fnv(expected, 32)));
            }
        }

        [TestCase(0u)]
        [TestCase(1u)]
        [TestCase(0x80000000u)]
        [TestCase(uint.MaxValue)]
        public void ColliderSnapshotHasExactEightByteUnsignedLayerAndRoundTrips(uint layer)
        {
            var serializer = new ColliderSerializer();
            Collider collider = Example(layer);
            var writer = new SimStateWriter();
            serializer.Write(writer, in collider);
            byte[] expected = new byte[29];
            Put(expected, 4, (ulong)collider.Radius.Raw);
            Put(expected, 12, (ulong)collider.Height.Raw);
            Put(expected, 20, layer);
            byte[] snapshot = writer.ToArray();
            Assert.That(snapshot, Is.EqualTo(expected));
            for (int i = 24; i < 28; i++)
                Assert.That(snapshot[i], Is.Zero);
            var reader = new SimStateReader(snapshot);
            Collider restored = serializer.Read(reader);
            reader.End();
            Assert.That(restored.Layer, Is.EqualTo(layer));
            Assert.That(restored.Radius, Is.EqualTo(collider.Radius));
            Assert.That(restored.Height, Is.EqualTo(collider.Height));
            Assert.That(restored.ShapeKind, Is.EqualTo(collider.ShapeKind));
        }

        [TestCase(0x100000000UL)]
        [TestCase(0x8000000000000000UL)]
        [TestCase(ulong.MaxValue)]
        public void InvalidHighLayerBitsAreRejectedBeforeNarrowing(ulong layer)
        {
            var writer = new SimStateWriter();
            writer.Int32(0);
            writer.Int64(0);
            writer.Int64(0);
            writer.UInt64(layer);
            var reader = new SimStateReader(writer.ToArray());
            var error = Assert.Throws<SimSnapshotException>(() => new ColliderSerializer().Read(reader));
            Assert.That(error.Message, Is.EqualTo("Collider layer must have zero high 32 bits."));
        }

        [Test]
        public void FullSnapshotRejectsHighLayerBitsInComponentPayload()
        {
            var fixture = new LayerQueryFixture();
            Collider collider = Example(0x80000000u);
            fixture.Add(default, collider);
            byte[] snapshot = fixture.Sim.SaveSnapshot();
            var writer = new SimStateWriter();
            new ColliderSerializer().Write(writer, in collider);
            byte[] payload = writer.ToArray();
            int offset = -1;
            int matches = 0;
            for (int i = 0; i <= snapshot.Length - payload.Length; i++)
            {
                bool same = true;
                for (int j = 0; j < payload.Length; j++)
                    same &= snapshot[i + j] == payload[j];
                if (same)
                {
                    offset = i;
                    matches++;
                }
            }
            Assert.That(matches, Is.EqualTo(1));
            snapshot[offset + 24] = 1;
            var error = Assert.Throws<SimSnapshotException>(() => fixture.Sim.RestoreSnapshot(snapshot));
            Assert.That(error.Message, Does.Contain("zero high 32 bits"));
        }

        [Test]
        public void VersionOneIsRejectedClearlyBeforeAnyStateMutation()
        {
            var fixture = new LayerQueryFixture();
            fixture.Populate();
            fixture.Sim.Step(default);
            ulong before = fixture.Sim.StateHash();
            byte[] snapshot = fixture.Sim.SaveSnapshot();
            Assert.That(snapshot[4], Is.EqualTo(4));
            snapshot[4] = 1;
            var error = Assert.Throws<SimSnapshotException>(() => fixture.Sim.RestoreSnapshot(snapshot));
            Assert.That(error.Message, Is.EqualTo("Snapshot version mismatch: expected 4, received 1."));
            Assert.That(fixture.Sim.StateHash(), Is.EqualTo(before));
            fixture.Sim.Step(default);
        }

        [Test]
        public void LayerOnlyMutationChangesBothStateHashesButNotIgnoredHash()
        {
            var fixture = new LayerQueryFixture();
            EntityId entity = fixture.Add(default, Example(1));
            ulong full = fixture.Sim.StateHash();
            ulong authority = Fixtures.Authority(fixture.Sim);
            ulong ignored = LayerHashFixture.Hash(fixture.Sim, true);
            fixture.Sim.Entities.Get(fixture.Colliders, entity) = Example(0x80000000u);
            Assert.That(fixture.Sim.StateHash(), Is.Not.EqualTo(full));
            Assert.That(Fixtures.Authority(fixture.Sim), Is.Not.EqualTo(authority));
            Assert.That(LayerHashFixture.Hash(fixture.Sim, true), Is.EqualTo(ignored));
            fixture.Sim.Entities.Get(fixture.Colliders, entity) = Example(1);
            Assert.That(fixture.Sim.StateHash(), Is.EqualTo(full));
        }

        [Test]
        public void LegacyWholeMatchAndSchedulerVectorsRemainUnchangedInBothPaths()
        {
            Simulation sim = Fixtures.Create(123);
            for (int tick = 1; tick <= 1000; tick++)
                sim.Step(Fixtures.Input(tick));
            Assert.That(sim.StateHash(), Is.EqualTo(0x8166D6848CDEB66CUL));
            Assert.That(LayerHashFixture.Hash(sim, true), Is.EqualTo(0x8166D6848CDEB66CUL));
            Simulation heap = SnapshotFixtures.A3(false);
            var ignored = new StateHash(ignoreColliderLayers: true);
            heap.Scheduler.WriteState(ignored);
            Assert.That(Fixtures.Hash(heap.Scheduler), Is.EqualTo(0x22CB551F19EC00FEUL));
            Assert.That(ignored.Value, Is.EqualTo(0x22CB551F19EC00FEUL));
        }

        private static Collider Example(uint layer) => new Collider(SimFix.FromRaw(0x12345), SimFix.FromRaw(0x45678), layer);

        private static byte[] HashBytes(uint layer)
        {
            var bytes = new byte[32];
            Put(bytes, 8, 0x12345);
            Put(bytes, 16, 0x45678);
            Put(bytes, 24, layer);
            return bytes;
        }

        private static void Put(byte[] bytes, int start, ulong value)
        {
            for (int i = 0; i < 8; i++)
            {
                bytes[start + i] = unchecked((byte)value);
                value >>= 8;
            }
        }

        private static ulong Fnv(byte[] bytes, int count)
        {
            ulong result = 14695981039346656037UL;
            for (int i = 0; i < count; i++)
                result = unchecked((result ^ bytes[i]) * 1099511628211UL);
            return result;
        }
    }
}
