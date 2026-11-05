using System;
using NUnit.Framework;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    public sealed class SnapshotContractTests
    {
        private sealed class ContractSystem : ISimSystem, ISimStateSerializer
        {
            public Action<SimContext> Action;
            public int Order
            {
                get;
                set;
            }

            public string StateTypeId
            {
                get;
                set;
            } = "test.contract";
            public int StateVersion
            {
                get;
                set;
            } = 1;
            public void Step(SimContext context) => Action?.Invoke(context);
            public void WriteState(StateHash hash)
            {
            }

            public void SaveState(SimStateWriter writer)
            {
            }

            public void LoadState(SimStateReader reader)
            {
            }
        }

        private sealed class HashOnlySystem : ISimSystem
        {
            public int Order => 1;
            public void Step(SimContext context)
            {
            }

            public void WriteState(StateHash hash)
            {
            }
        }

        private sealed class HashOnlyMode : IMatchMode
        {
            public bool IsFinished => false;
            public void Start(SimContext context)
            {
            }

            public void Step(SimContext context)
            {
            }

            public void WriteState(StateHash hash)
            {
            }
        }

        private sealed class VersionedHealth : IComponentSerializer<Health>
        {
            public int TypeId
            {
                get;
                set;
            } = 3000;
            public int Version
            {
                get;
                set;
            } = 1;
            public void Write(SimStateWriter writer, in Health value) => writer.Int32(value.Value);
            public Health Read(SimStateReader reader) => new Health { Value = reader.Int32() };
        }

        [Test]
        public void G12_VersionMismatchReportsExpectedAndReceivedBeforeChangingState()
        {
            Simulation sim = Fixtures.Create(123);
            sim.Step(Fixtures.Input(1));
            ulong hash = sim.StateHash();
            byte[] snapshot = sim.SaveSnapshot();
            snapshot[4] = 99;
            var error = Assert.Throws<SimSnapshotException>(() => sim.RestoreSnapshot(snapshot));
            Assert.That(error.Message, Does.Contain("version mismatch"));
            Assert.That(error.Message, Does.Contain("expected 4"));
            Assert.That(error.Message, Does.Contain("received 99"));
            Assert.That(sim.StateHash(), Is.EqualTo(hash));
            sim.Step(Fixtures.Input(2));
        }

        [Test]
        public void MissingComponentSystemAndModeSerializersFailExplicitly()
        {
            Simulation component = Fixtures.Empty(1);
            component.Entities.Register<Health>("unencoded-health");
            Assert.That(Assert.Throws<SimSnapshotException>(() => component.SaveSnapshot()).Message, Does.Contain("unencoded-health"));
            Simulation system = Fixtures.Empty(1);
            system.Register("unencoded-system", new HashOnlySystem());
            Assert.That(Assert.Throws<SimSnapshotException>(() => system.SaveSnapshot()).Message, Does.Contain("unencoded-system"));
            var mode = new Simulation(1, new HashOnlyMode(), "unencoded-mode", config: new SimConfig(1, 1, 1));
            Assert.That(Assert.Throws<SimSnapshotException>(() => mode.SaveSnapshot()).Message, Does.Contain("unencoded-mode"));
        }

        [TestCase("name")]
        [TestCase("id")]
        [TestCase("version")]
        [TestCase("count")]
        public void ComponentManifestMismatchPreservesDestination(string mismatch)
        {
            Simulation source = Fixtures.Empty(1);
            source.Entities.Register<Health>("health", new VersionedHealth());
            byte[] snapshot = source.SaveSnapshot();
            Simulation target = Fixtures.Empty(2);
            if (mismatch != "count")
                target.Entities.Register<Health>(mismatch == "name" ? "other" : "health",
                    new VersionedHealth { TypeId = mismatch == "id" ? 3001 : 3000, Version = mismatch == "version" ? 2 : 1 });
            ulong hash = target.StateHash();
            Assert.That(Assert.Throws<SimSnapshotException>(() => target.RestoreSnapshot(snapshot)).Message, Does.Contain("manifest"));
            Assert.That(target.StateHash(), Is.EqualTo(hash));
        }

        [TestCase("id")]
        [TestCase("order")]
        [TestCase("type")]
        [TestCase("version")]
        [TestCase("count")]
        public void SystemManifestMismatchPreservesDestination(string mismatch)
        {
            Simulation source = Fixtures.Empty(1);
            source.Register("system", new ContractSystem());
            byte[] snapshot = source.SaveSnapshot();
            Simulation target = Fixtures.Empty(2);
            if (mismatch != "count")
                target.Register(mismatch == "id" ? "other" : "system", new ContractSystem
                {
                    Order = mismatch == "order" ? 2 : 0,
                    StateTypeId = mismatch == "type" ? "test.other" : "test.contract",
                    StateVersion = mismatch == "version" ? 2 : 1
                });
            ulong hash = target.StateHash();
            Assert.That(Assert.Throws<SimSnapshotException>(() => target.RestoreSnapshot(snapshot)).Message, Does.Contain("manifest"));
            Assert.That(target.StateHash(), Is.EqualTo(hash));
        }

        [Test]
        public void ModeAndConfigurationMismatchAreClear()
        {
            Simulation source = Fixtures.Empty(1);
            byte[] snapshot = source.SaveSnapshot();
            var modeMismatch = new Simulation(1, modeId: "other", eventBuffer: new SimEventBuffer(8, 64, 256));
            Assert.That(Assert.Throws<SimSnapshotException>(() => modeMismatch.RestoreSnapshot(snapshot)).Message, Does.Contain("Mode"));
            var configMismatch = new Simulation(1, config: new SimConfig(8, 64, 512));
            Assert.That(Assert.Throws<SimSnapshotException>(() => configMismatch.RestoreSnapshot(snapshot)).Message, Does.Contain("SimConfig"));
            Assert.Throws<ArgumentException>(() => new Simulation(1, eventBuffer: new SimEventBuffer(1, 1, 1), config: new SimConfig(1, 1, 1)));
        }

        [Test]
        public void SerializerRegistrationIsExplicitUniqueAndFrozenAfterWarmup()
        {
            var store = new EntityStore();
            var health = store.Register<Health>("health");
            store.RegisterSerializer(health, new HealthSerializer());
            Assert.Throws<InvalidOperationException>(() => store.RegisterSerializer(health, new HealthSerializer()));
            Assert.Throws<InvalidOperationException>(() => store.Register<Position>("position", new DuplicateIdPosition()));
            Assert.Throws<ArgumentOutOfRangeException>(() => new EntityStore().Register<Health>("health", new VersionedHealth { TypeId = 0 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => new EntityStore().Register<Health>("health", new VersionedHealth { Version = 0 }));
            var other = new EntityStore();
            Assert.Throws<InvalidOperationException>(() => other.RegisterSerializer(health, new HealthSerializer()));
            store.CompleteWarmup();
            Assert.Throws<InvalidOperationException>(() => store.RegisterSerializer(health, new HealthSerializer()));
        }

        private sealed class DuplicateIdPosition : IComponentSerializer<Position>
        {
            public int TypeId => 1001;
            public int Version => 1;
            public void Write(SimStateWriter writer, in Position value)
            {
            }

            public Position Read(SimStateReader reader) => default;
        }

        [Test]
        public void SaveAndRestoreAreRejectedInsideStepAndOpenEventTicks()
        {
            Simulation sim = Fixtures.Empty(1);
            var system = new ContractSystem();
            sim.Register("contract", system);
            byte[] initial = sim.SaveSnapshot();
            int checkedBoundaries = 0;
            system.Action = context =>
            {
                Assert.That(Assert.Throws<SimSnapshotException>(() => sim.SaveSnapshot()).Message, Does.Contain("boundary"));
                Assert.That(Assert.Throws<SimSnapshotException>(() => sim.RestoreSnapshot(initial)).Message, Does.Contain("boundary"));
                checkedBoundaries++;
            };
            sim.Step(default);
            Assert.That(checkedBoundaries, Is.EqualTo(1));
            var buffer = new SimEventBuffer(1, 1, 1);
            var open = new Simulation(1, eventBuffer: buffer);
            buffer.BeginTick(1);
            Assert.Throws<SimSnapshotException>(() => open.SaveSnapshot());
        }

        [Test]
        public void BinaryFieldsHaveFixedLittleEndianEncodingAndMalformedReadsFailExplicitly()
        {
            var writer = new SimStateWriter();
            writer.Int32(0x12345678);
            writer.Int64(long.MinValue);
            writer.UInt64(ulong.MaxValue);
            writer.Bool(true);
            writer.String("abc");
            byte[] bytes = writer.ToArray();
            Assert.That(bytes, Is.EqualTo(new byte[]
            {
                0x78, 0x56, 0x34, 0x12, 0, 0, 0, 0, 0, 0, 0, 0x80,
                255, 255, 255, 255, 255, 255, 255, 255, 1, 3, 0, 0, 0, 97, 98, 99
            }));
            var reader = new SimStateReader(bytes);
            Assert.That(reader.Int32(), Is.EqualTo(0x12345678));
            Assert.That(reader.Int64(), Is.EqualTo(long.MinValue));
            Assert.That(reader.UInt64(), Is.EqualTo(ulong.MaxValue));
            Assert.That(reader.Bool(), Is.True);
            Assert.That(reader.String(), Is.EqualTo("abc"));
            reader.End();
            Assert.Throws<SimSnapshotException>(() => reader.Byte());
            Assert.Throws<SimSnapshotException>(() => new SimStateReader(new byte[] { 2 }).Bool());
            Assert.Throws<SimSnapshotException>(() => new SimStateReader(new byte[] { 255, 255, 255, 127 }).Bytes());
            Assert.Throws<SimSnapshotException>(() => new SimStateReader(new byte[] { 255, 255, 255, 255 }).Bytes());
        }

        [Test]
        public void MalformedPayloadFaultsSimulationUntilAValidSnapshotRestoresIt()
        {
            Simulation sim = Fixtures.Create(123);
            sim.Step(Fixtures.Input(1));
            byte[] good = sim.SaveSnapshot();
            byte[] trailing = new byte[good.Length + 1];
            Array.Copy(good, trailing, good.Length);
            Assert.That(Assert.Throws<SimSnapshotException>(() => sim.RestoreSnapshot(trailing)).Message, Does.Contain("trailing"));
            Assert.Throws<InvalidOperationException>(() => sim.Step(default));
            Assert.Throws<SimSnapshotException>(() => sim.SaveSnapshot());
            sim.RestoreSnapshot(good);
            Assert.That(sim.SaveSnapshot(), Is.EqualTo(good));
            sim.Step(Fixtures.Input(2));
        }
    }
}
