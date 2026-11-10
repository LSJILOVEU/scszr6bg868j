using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    [TestFixture, Category("M0c2S1")]
    public sealed class Mt115GameplayBindingSerializerTests
    {
        [Test]
        public void SerializerTypeIdIs21()
        {
            Assert.That(new GameplayBindingSerializer().TypeId, Is.EqualTo(21));
        }

        [Test]
        public void SerializerVersionIs1()
        {
            Assert.That(new GameplayBindingSerializer().Version, Is.EqualTo(1));
        }
    }
}
