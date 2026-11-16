using System;
using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    [TestFixture]
    public sealed class Mt209SimStateIOTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void NullStringIsRejected(bool stable)
        {
            var writer = stable
                ? new SimStateWriter(new EntityStore(1, IdentityMode.StableWorld), materialize: true)
                : new SimStateWriter();
            Assert.Throws<ArgumentNullException>(() => writer.String(null));
        }

        [Test]
        public void StableByteBlockHasExactLengthAndPayload()
        {
            var writer = new SimStateWriter(new EntityStore(1, IdentityMode.StableWorld), materialize: true);
            writer.Bytes(new byte[] { 1, 128, 255 });
            Assert.That(writer.ToArray(), Is.EqualTo(new byte[] { 3, 0, 0, 0, 0, 0, 0, 0, 1, 128, 255 }));
        }
    }
}
