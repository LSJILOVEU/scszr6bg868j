using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    [TestFixture, Category("StableIdentity")]
    public sealed class Mt092StableEventsTests
    {
        [Test]
        public void ReadStableRejectsIndexEqualToEventCount()
        {
            var buffer = new SimEventBuffer(retainedTicks:1,initialCapacity:1,hardLimit:1);
            buffer.EnableStableEvents();
            buffer.BeginTick(1);
            buffer.PublishStable(new StableSimEvent(new EventTypeId(100)));
            buffer.EndTick();
            Assert.That(buffer.Read(1).Length,Is.EqualTo(1));
            Assert.Throws<SimBoundaryException>(() => buffer.ReadStable(1,1));
        }

        [TestCase(0,1), TestCase(1,1)]
        public void LoadStableEventsRejectsZeroOrRepeatedSequences(long first, long second)
        {
            var store = new EntityStore(1,IdentityMode.StableWorld);
            var buffer = new SimEventBuffer(retainedTicks:1,initialCapacity:2,hardLimit:2);
            buffer.EnableStableEvents();
            var writer = new SimStateWriter(store,materialize:true);
            writer.Int64(1);
            writer.Int64(3);
            writer.Int64(0);
            writer.Int64(0);
            writer.Int32(1);
            writer.Int64(1);
            writer.Int32(2);
            var item = new StableSimEvent(new EventTypeId(100));
            writer.StableEvent(item.At(1,first));
            writer.StableEvent(item.At(1,second));
            var reader = new SimStateReader(writer.ToArray(),store);
            Assert.Throws<SimSnapshotException>(() => buffer.LoadStableEvents(reader,1));
        }
    }
}
