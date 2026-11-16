using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    [TestFixture, Category("StableIdentity")]
    public sealed class Mt100StableStateIOTests
    {
        [Test]
        public void StableTextWritesOneByteAtAsciiUpperBound()
        {
            var writer = new SimStateWriter(new EntityStore(1,IdentityMode.StableWorld),materialize:true);
            writer.String("\u007F");
            Assert.That(writer.ToArray(),Is.EqualTo(new byte[] {1,0,0,0,0,0,0,0,0x7F}));
        }

        [Test]
        public void StableTextWritesTwoBytesAtTwoByteUpperBound()
        {
            var writer = new SimStateWriter(new EntityStore(1,IdentityMode.StableWorld),materialize:true);
            writer.String("\u07FF");
            Assert.That(writer.ToArray(),Is.EqualTo(new byte[] {2,0,0,0,0,0,0,0,0xDF,0xBF}));
        }

        [Test]
        public void StableTextRejectsStandaloneContinuationByte()
        {
            var store = new EntityStore(1,IdentityMode.StableWorld);
            var reader = new SimStateReader(new byte[] {1,0,0,0,0,0,0,0,0x80},store);
            Assert.Throws<SimSnapshotException>(() => reader.String());
        }

        [Test]
        public void StableTextReadsContinuationByteAtUpperBound()
        {
            var store = new EntityStore(1,IdentityMode.StableWorld);
            var reader = new SimStateReader(new byte[] {2,0,0,0,0,0,0,0,0xC2,0xBF},store);
            string value = null;
            Assert.DoesNotThrow(() => value = reader.String());
            Assert.That(value,Is.EqualTo("\u00BF"));
        }
    }
}
