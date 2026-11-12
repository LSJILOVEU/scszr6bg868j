using NUnit.Framework;
using WarCore.Content;

namespace WarCore.Tests
{
    public sealed class Mt145TerrainRulesTests
    {
        [Test]
        public void Canonical_PreservesSurfaceSpeedAndCost()
        {
            var rules = new TerrainRules("test", 3, 4,
                new[] { new SurfaceDef("mud", true, 500, 3) });
            Assert.That(A2.Hex(rules.Canonical()),
                Is.EqualTo("030000000400000001000000030000006D756401F401000003000000"));
        }
    }
}
