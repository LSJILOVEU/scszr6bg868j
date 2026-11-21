using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Content.MapTools;
using WarCore.Presentation;
using static WarCore.Tests.Ed3Fixtures;
using static WarCore.Tests.Ed3cFixtures;

namespace WarCore.Tests
{
    // ED-3c-1 acceptance A10: the facade (WithTerrain with a tile, MapDemTiles).
    public sealed class Ed3cDocumentTests
    {
        [Test]
        public void WithTerrainTakesTheTileText()
        {
            MapDesc d = Map(6, 6);
            MapDocument doc = MapDocument.Open(MapDescJson.Write(d));
            string gen = GenB(6, 6, Hill(2, 2, 2, 300));
            MapTerrainResult r = Ok(() => doc.WithTerrain(gen, Tile));
            MapDesc after = MapDescJson.Read(r.Document.Save());
            MapDesc expected = Apply(d, ComposeB(d, gen, Tile));
            Assert.That(MapDescJson.Write(after), Is.EqualTo(MapDescJson.Write(expected)));
            Assert.That(H(after, 6, 0), Is.EqualTo(294912));
            Assert.That(r.HeightsSkipped, Is.EqualTo(0));
        }

        [Test]
        public void WithTerrainRefusesBaseWithoutTileAndTileWithoutBase()
        {
            byte[] bytes = MapDescJson.Write(Map(6, 6));
            MapDocument doc = MapDocument.Open(bytes);
            ArgumentException noTile = Assert.Throws<ArgumentException>(() => doc.WithTerrain(GenB(6, 6)));
            Assert.That(noTile.Message, Does.Contain("dem"));
            ArgumentException noBase = Assert.Throws<ArgumentException>(() => doc.WithTerrain(Gen(6, 6, ""), Tile));
            Assert.That(noBase.Message, Does.Contain("dem"));
            ArgumentException badTile = Assert.Throws<ArgumentException>(() => doc.WithTerrain(GenB(6, 6), "{}"));
            Assert.That(badTile.Message, Does.StartWith("dem"));
            Assert.That(doc.Save(), Is.EqualTo(bytes));
            MapTerrainResult plain = Ok(() => doc.WithTerrain(Gen(6, 6, ""), null));
            Assert.That(plain.Document.Save(), Is.EqualTo(Ok(() => doc.WithTerrain(Gen(6, 6, ""))).Document.Save()));
        }

        [Test]
        public void DemTilesGiveFingerprintAndCredit()
        {
            Assert.That(MapDemTiles.Fingerprint(Tile), Is.EqualTo(Fp));
            Assert.That(MapDemTiles.Credit(Tile), Is.EqualTo(new[] { "synthetic test tile" }));
            Assert.That(MapDemTiles.Credit(Dem(T, credit: "[\"a\", \"b\"]")), Is.EqualTo(new[] { "a", "b" }));
            Assert.Throws<ArgumentException>(() => MapDemTiles.Fingerprint("{}"));
        }

        [Test]
        public void PublicSurfaceStaysPlain()
        {
            List<string> problems = E1bDocumentTests.SurfaceViolations(typeof(MapDemTiles), typeof(MapDocument).Assembly, out int members);
            Assert.That(problems, Is.Empty);
            Assert.That(members, Is.GreaterThanOrEqualTo(2));
            problems = E1bDocumentTests.SurfaceViolations(typeof(MapDocument), typeof(MapDocument).Assembly, out members);
            Assert.That(problems, Is.Empty);
        }
    }
}
