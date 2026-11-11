using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    [TestFixture, Category("StableIdentity")]
    public sealed class Mt093StableIdentityTests
    {
        [Test]
        public void ConstructorRejectsZeroSourceDomain()
        {
            Assert.Throws<SimBoundaryException>(() => new StableEntityKey(0, 7, 11));
        }
    }
}
