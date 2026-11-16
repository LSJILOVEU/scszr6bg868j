using System;
using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class Mt062SimEventsTests
    {
        [Test]
        public void ZeroEventTypeIdIsRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new EventTypeId(0));
        }

        [Test]
        public void OverflowSummaryUsesProtocolIdOne()
        {
            var buffer = new SimEventBuffer(1, 1, 1);
            buffer.BeginTick(1);
            buffer.Publish(SnapshotFixtures.Payload(7));
            buffer.Publish(SnapshotFixtures.Payload(8));
            buffer.EndTick();

            Assert.That(buffer.Read(1).Length, Is.EqualTo(2));
            Assert.That(buffer.Read(1)[1].Type.Value, Is.EqualTo(1));
        }

        [Test]
        public void TruncatedRestoreDoesNotExposeExpiredEvents()
        {
            var buffer = new SimEventBuffer(3, 1, 1);
            buffer.BeginTick(1);
            buffer.Publish(SnapshotFixtures.Payload(7));
            buffer.EndTick();

            var writer = new SimStateWriter();
            buffer.SaveEvents(writer);
            byte[] truncated = writer.ToArray();
            Array.Resize(ref truncated, truncated.Length - 1);

            for (int tick = 2; tick <= 4; tick++)
            {
                buffer.BeginTick(tick);
                buffer.EndTick();
            }

            Assert.Throws<SimSnapshotException>(() =>
                buffer.LoadEvents(new SimStateReader(truncated), 1));
            Assert.That(buffer.Read(1).Length, Is.Zero);
        }
    }
}
