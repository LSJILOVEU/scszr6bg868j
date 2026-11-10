using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    [TestFixture, Category("Round4Retention")]
    public sealed class Mt058RetainedWorldOptionsTests
    {
        [Test]
        public void MaximumIdentityCapacityIsAccepted()
        {
            RetainedWorldOptions options = null;
            Assert.DoesNotThrow(() => options = new RetainedWorldOptions(identityCapacity:1 << 27));
            Assert.That(options.IdentityCapacity, Is.EqualTo(1 << 27));
        }
    }
}
