using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class Mt183GameplayBindingSerializerTests
    {
        [Test]
        public void SerializerTypeIdPreservesSnapshotContract()
        {
            Assert.That(new GameplayBindingSerializer().TypeId, Is.EqualTo(21));
        }

        [Test]
        public void SerializerVersionPreservesSnapshotContract()
        {
            Assert.That(new GameplayBindingSerializer().Version, Is.EqualTo(1));
        }
    }
}
