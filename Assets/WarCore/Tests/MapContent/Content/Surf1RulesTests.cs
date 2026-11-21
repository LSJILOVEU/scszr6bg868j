using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
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

        // S2: the old layers are untouched.
        [Test]
        public void OldTerrainLayer_StillHasThreeSurfaces()
        {
            TerrainRules rules = RwrPack.Load("rwr-terrain-demo").Terrain;
            Assert.That(rules.SurfaceCount, Is.EqualTo(3));
            Assert.That(Enumerable.Range(0, 3).Select(i => rules.Surface(i).Id).ToArray(), Is.EqualTo(new[] { "grass", "mud", "road" }));
            Assert.That(rules.TryFind("bush", out _), Is.False);
            Assert.That(RwrPack.Load("rwr-ai-playtest").Terrain, Is.Not.Null);
        }

        [TestCase("rwr-terrain-demo.json", "599a1df8c173f1d796d44b06cb1bc094cf51159b781c5c96cd5d8cf28f16e4c2")]
        [TestCase("rwr-ai-playtest.json", "d99b5203cca6824a50bca16b3c11eae93fd81c865529cdb9eb12fb905b058376")]
        [TestCase("editor.json", "a436555f701be532b0e9b7a9b6e82e889c94f84af2fd55927e6b2b72f7760ebe")]
        public void OldLayerFiles_AreByteIdentical(string file, string sha256)
        {
            string text = File.ReadAllText(Path.Combine(ArchitectureTests.AssetRoot(), "WarCore/Content/Rwr/Data", file)).Replace("\r\n", "\n");
            string hex = BitConverter.ToString(SHA256.Create().ComputeHash(new UTF8Encoding(false).GetBytes(text))).Replace("-", "").ToLowerInvariant();
            Assert.That(hex, Is.EqualTo(sha256), file);
        }
    }
}
