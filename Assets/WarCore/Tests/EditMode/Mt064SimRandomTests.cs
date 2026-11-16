using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class Mt064SimRandomTests
    {
        [Test]
        public void SampleAtRejectionThresholdConsumesExactlyOneDraw()
        {
            var writer = new SimStateWriter();
            writer.UInt64(0);
            writer.Int32(1);
            writer.String("a");
            // The next increment wraps to zero, yielding the zero threshold for a bound of two.
            writer.UInt64(0x61C8864680B583EBUL);
            var random = new SimRandom(0);
            random.LoadRandom(new SimStateReader(writer.ToArray()));
            RandomStreamId stream = random.Stream("a");

            Assert.That(random.NextInt(stream, 2), Is.EqualTo(0));
            Assert.That(random.NextUInt64(stream), Is.EqualTo(0xE220A8397B1DCDAFUL));
        }

        [Test]
        public void ShortManifestReportsEmptyStreamName()
        {
            var writer = new SimStateWriter();
            writer.UInt64(0);
            writer.Int32(1);
            writer.String(string.Empty);
            writer.Int32(0);
            var random = new SimRandom(0);

            var error = Assert.Throws<SimSnapshotException>(() => random.LoadRandom(new SimStateReader(writer.ToArray())));
            Assert.That(error.Message, Is.EqualTo("Invalid RNG stream manifest."));
        }

        [Test]
        public void DuplicateStreamNamesThrowSnapshotException()
        {
            var writer = new SimStateWriter();
            writer.UInt64(0);
            writer.Int32(2);
            writer.String("a");
            writer.UInt64(1);
            writer.String("a");
            writer.UInt64(2);
            var random = new SimRandom(0);

            Assert.Throws<SimSnapshotException>(() => random.LoadRandom(new SimStateReader(writer.ToArray())));
        }
    }
}
