using NUnit.Framework;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    [Category("S38")]
    public sealed class SnapshotHeaderRegressionTests
    {
        [Test]
        public void SnapshotVersionThreeReservesAnEightByteZeroManifestHash()
        {
            var sim = new Simulation(1);
            var reader = new SimStateReader(sim.SaveSnapshot());
            Assert.That(reader.Int32(), Is.EqualTo(0x57435331));
            Assert.That(reader.Int32(), Is.EqualTo(4));
            Assert.That(reader.UInt64(), Is.Zero);
            Assert.That(reader.Int32(), Is.EqualTo(sim.Config.EventRetainedTicks));
        }

        [Test]
        public void NonzeroReservedManifestHashIsRejectedBeforePayloadMutation()
        {
            var sim = new Simulation(1);
            byte[] snapshot = sim.SaveSnapshot();
            ulong hash = sim.StateHash();
            snapshot[8] = 1;
            var error = Assert.Throws<SimSnapshotException>(() => sim.RestoreSnapshot(snapshot));
            Assert.That(error.Message, Does.Contain("content manifest"));
            Assert.That(sim.StateHash(), Is.EqualTo(hash));
        }

        [TestCase(1)]
        [TestCase(2)]
        public void OldSnapshotVersionsAreExplicitlyRejected(int version)
        {
            var sim = new Simulation(1);
            byte[] snapshot = sim.SaveSnapshot();
            snapshot[4] = (byte)version;
            var error = Assert.Throws<SimSnapshotException>(() => sim.RestoreSnapshot(snapshot));
            Assert.That(error.Message, Is.EqualTo("Snapshot version mismatch: expected 4, received " + version + "."));
        }

        [Test]
        public void EqualAuthoritativeStatesMayHaveDifferentSnapshotLayouts()
        {
            Simulation first = SnapshotFixtures.A3(false);
            Simulation second = SnapshotFixtures.A3(true);
            Assert.That(first.StateHash(), Is.EqualTo(second.StateHash()));
            Assert.That(Fixtures.Hash(first.Scheduler), Is.EqualTo(0x22CB551F19EC00FEUL));
            Assert.That(first.SaveSnapshot(), Is.Not.EqualTo(second.SaveSnapshot()));
        }
    }
}
