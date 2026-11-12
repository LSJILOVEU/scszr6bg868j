using System;
using NUnit.Framework;
using WarCore.Content;

namespace WarCore.Tests
{
    public sealed class Mt144TerrainRulesTests
    {
        [TestCase(1)]
        [TestCase(2000)]
        public void SurfaceSpeed_AcceptsInclusiveBounds(int speedMilli)
        {
            SurfaceDef surface = default;
            Assert.DoesNotThrow(() => surface = new SurfaceDef("dirt", true, speedMilli, 1));
            Assert.That(surface.SpeedMilli, Is.EqualTo(speedMilli));
        }

        [TestCase(1, 1)]
        [TestCase(0, 2)]
        public void SlopeRule_AcceptsMinimumNumeratorAndDenominator(int num, int den)
        {
            TerrainRules rules = null;
            Assert.DoesNotThrow(() => rules = new TerrainRules("terrain", num, den, Array.Empty<SurfaceDef>()));
            Assert.That((rules.MaxWalkableNum, rules.MaxWalkableDen), Is.EqualTo((num, den)));
        }

        [Test]
        public void Surfaces_AreInOrdinalOrder()
        {
            var rules = new TerrainRules("terrain", 3, 4, new[]
            {
                new SurfaceDef("grass", true, 1000, 1),
                new SurfaceDef("Road", true, 1000, 1),
                new SurfaceDef("dirt", true, 1000, 1),
            });
            Assert.That(rules.SurfaceCount, Is.EqualTo(3));
            Assert.That(new[] { rules.Surface(0).Id, rules.Surface(1).Id, rules.Surface(2).Id },
                Is.EqualTo(new[] { "Road", "dirt", "grass" }));
        }

        [Test]
        public void TryFind_RequiresExactCase()
        {
            var rules = new TerrainRules("terrain", 3, 4, new[] { new SurfaceDef("dirt", true, 1000, 1) });
            Assert.That(rules.TryFind("dirt", out SurfaceDef found), Is.True);
            Assert.That(found.Id, Is.EqualTo("dirt"));
            Assert.That(rules.TryFind("Dirt", out SurfaceDef missing), Is.False);
            Assert.That(missing, Is.EqualTo(default(SurfaceDef)));
        }

        [TestCase(1000, "")]
        [TestCase(500, "434E434CF4010000")]
        public void Canonical_StoresSlopeAndOptionalConcealment(int concealMilli, string tail)
        {
            var rules = new TerrainRules("terrain", 3, 4,
                new[] { new SurfaceDef("dirt", true, 1000, 1, concealMilli) });
            Assert.That(A2.Hex(rules.Canonical()),
                Is.EqualTo("030000000400000001000000040000006469727401E803000001000000" + tail));
        }
    }
}
