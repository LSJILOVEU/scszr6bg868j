using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    [TestFixture, Category("M0c1"), Category("M0c1Mutation")]
    public sealed class Mt051PlacedCatalogTests
    {
        [Test]
        public void MaximumCountIs16777216()
        {
            Assert.That(PlacedCatalog.MaximumCount,Is.EqualTo(16777216));
        }

        [Test]
        public void EntryAtCountThrowsBoundaryException()
        {
            var catalog=M0c1Fixture.Create(out _).Placed.Catalog;
            Assert.Throws<SimBoundaryException>(()=>catalog.Entry(catalog.Count));
        }
    }
}
