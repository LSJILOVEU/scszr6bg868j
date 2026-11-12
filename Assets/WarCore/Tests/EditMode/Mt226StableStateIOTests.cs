using System.Reflection;
using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    [TestFixture, Category("StableIdentity")]
    public sealed class Mt226StableStateIOTests
    {
        [Test]
        public void StableByteRejectsWritesAfterTheLastAvailableByte()
        {
            var writer = new SimStateWriter(new EntityStore(1,IdentityMode.StableWorld),materialize:true);
            // Seed the byte boundary without writing a multi-gigabyte record.
            typeof(SimStateWriter).GetField("<Length>k__BackingField",BindingFlags.NonPublic|BindingFlags.Instance)
                .SetValue(writer,int.MaxValue-1L);
            writer.Byte(7);
            Assert.That(writer.ToArray(),Is.EqualTo(new byte[] {7}));
            Assert.Throws<SimBoundaryException>(() => writer.Byte(8));
            Assert.That(writer.ToArray(),Is.EqualTo(new byte[] {7}));
        }

        [Test]
        public void ThreeByteUnicodeCharacterHasTheExactUtf8LengthAndPayload()
        {
            var writer = new SimStateWriter(new EntityStore(1,IdentityMode.StableWorld),materialize:true);
            writer.String("\u0800");
            Assert.That(writer.ToArray(),Is.EqualTo(new byte[] {3,0,0,0,0,0,0,0,0xE0,0xA0,0x80}));
        }

        [Test]
        public void StableTriggerSerializationRejectsAnExtensionWithoutAStableSchema()
        {
            var registry = new TriggerRegistry();
            registry.RegisterCondition(1,new ProbeCondition(1));
            var writer = new SimStateWriter(new EntityStore(1,IdentityMode.StableWorld),materialize:true);
            Assert.Throws<SimBoundaryException>(() => registry.SaveState(writer));
        }
    }
}
