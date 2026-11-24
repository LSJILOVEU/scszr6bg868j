using System.Linq;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Content.Rwr;

namespace WarCore.Tests
{
    // SURF-1: the layer "rwr-surfaces" (rwr-terrain-demo plus seven surfaces). Every number below is written out here,
    // never read back from the layer, so a changed value in the data turns a test red.
    public sealed class Surf1RulesTests
    {
        internal const string Layer = "rwr-surfaces";
        internal static readonly string[] Names =
            { "asphalt", "bush", "grass", "mud", "road", "rock", "sand", "snow", "water-deep", "water-shallow" };

        // S1 (rows: id, passable 1/0, speedMilli, cost, concealMilli; a blocked surface is stored as speed 0, cost 0)
        [Test]
        public void Layer_LoadsWithTenSurfacesInOrdinalOrder()
        {
            ContentPack pack = RwrPack.Load(Layer);
            TerrainRules rules = pack.Terrain;
            Assert.That(rules, Is.Not.Null);
            Assert.That(rules.SurfaceCount, Is.EqualTo(10));
            var ids = new string[rules.SurfaceCount];
            for (int i = 0; i < ids.Length; i++) ids[i] = rules.Surface(i).Id;
            Assert.That(ids, Is.EqualTo(Names));
            Assert.That(rules.MaxWalkableNum, Is.EqualTo(3));
            Assert.That(rules.MaxWalkableDen, Is.EqualTo(4));
            Assert.That(pack.HasMatch, Is.True);
        }

        [TestCase("asphalt", 1, 1250, 1, 1000)]
        [TestCase("bush", 1, 800, 2, 500)]
        [TestCase("grass", 1, 1000, 1, 1000)]
        [TestCase("mud", 1, 500, 3, 1000)]
        [TestCase("road", 1, 1250, 1, 1000)]
        [TestCase("rock", 1, 900, 2, 1000)]
        [TestCase("sand", 1, 800, 2, 1000)]
        [TestCase("snow", 1, 700, 3, 1000)]
        [TestCase("water-deep", 0, 0, 0, 1000)]
        [TestCase("water-shallow", 1, 500, 4, 1000)]
        public void Layer_EachSurfaceHasItsFields(string id, int passable, int speed, int cost, int conceal)
        {
            Assert.That(RwrPack.Load(Layer).Terrain.TryFind(id, out SurfaceDef s), Is.True, id);
            Assert.That(new[] { s.Passable ? 1 : 0, s.SpeedMilli, s.Cost, s.ConcealMilli },
                Is.EqualTo(new[] { passable, speed, cost, conceal }), id);
        }

        // S2: the old terrain layer keeps its three surfaces.
        [Test]
        public void OldTerrainLayer_StillHasThreeSurfaces()
        {
            TerrainRules rules = RwrPack.Load("rwr-terrain-demo").Terrain;
            Assert.That(rules.SurfaceCount, Is.EqualTo(3));
            Assert.That(Enumerable.Range(0, 3).Select(i => rules.Surface(i).Id).ToArray(), Is.EqualTo(new[] { "grass", "mud", "road" }));
            Assert.That(rules.TryFind("bush", out _), Is.False);
            Assert.That(RwrPack.Load("rwr-ai-playtest").Terrain, Is.Not.Null);
        }
    }
}
