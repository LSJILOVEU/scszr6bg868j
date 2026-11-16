using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    [TestFixture, Category("StableIdentity")]
    public sealed class Mt220StableContentManifestTests
    {
        [Test]
        public void ContentNameRejectsUnpairedLowSurrogate()
        {
            Assert.Throws<SimBoundaryException>(() => new StableContentKey("\uDC00",1));
        }

        [Test]
        public void LookupFindsNameBelowMiddleCandidate()
        {
            var manifest = new StableContentManifest(7,
                new StableContentKey("c",33),new StableContentKey("a",11),new StableContentKey("b",22));
            ulong key = 0;
            Assert.DoesNotThrow(() => key = manifest.Key("a"));
            Assert.That(key,Is.EqualTo(11UL));
        }

        [Test]
        public void LookupRejectsMissingName()
        {
            var manifest = new StableContentManifest(7,new StableContentKey("a",11));
            Assert.Throws<SimBoundaryException>(() => manifest.Key("missing"));
        }
    }
}
