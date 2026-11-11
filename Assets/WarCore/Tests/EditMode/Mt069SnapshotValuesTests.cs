using System;
using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class Mt069SnapshotValuesTests
    {
        [Test]
        public void ZeroEventTypeIsRejectedBeforeReadingPayload()
        {
            var writer = new SimStateWriter();
            SnapshotValues.Event(writer, SnapshotFixtures.Payload(7).At(1, 2));
            byte[] bytes = writer.ToArray();
            Array.Clear(bytes, 0, 4);
            var reader = new SimStateReader(bytes);

            Assert.Throws<SimSnapshotException>(() => SnapshotValues.Event(reader));
            Assert.That(reader.Remaining, Is.EqualTo(bytes.Length - 20));
        }

        [Test]
        public void TickZeroEventTemplateRoundTrips()
        {
            SimEvent template = SnapshotFixtures.Payload(7);
            var writer = new SimStateWriter();
            SnapshotValues.Event(writer, template);
            var reader = new SimStateReader(writer.ToArray());
            SimEvent decoded = default;

            Assert.DoesNotThrow(() => decoded = SnapshotValues.Event(reader));
            Assert.That(decoded, Is.EqualTo(template));
            Assert.That(reader.Remaining, Is.Zero);
        }
    }
}
