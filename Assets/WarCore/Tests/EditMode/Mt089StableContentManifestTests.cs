using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    [TestFixture, Category("StableIdentity")]
    public sealed class Mt089StableContentManifestTests
    {
        [Test]
        public void DigestUsesAscendingOrdinalNames()
        {
            var manifest = new StableContentManifest(7,new StableContentKey("b",2),new StableContentKey("a",1));
            Assert.That(manifest.Digest,Is.EqualTo(150559394548958314UL));
        }
    }
}
