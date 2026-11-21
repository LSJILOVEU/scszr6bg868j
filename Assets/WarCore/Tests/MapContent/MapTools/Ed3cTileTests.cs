using System;
using NUnit.Framework;
using WarCore.Content.MapTools;
using static WarCore.Tests.Ed3cFixtures;

namespace WarCore.Tests
{
    // ED-3c-1 acceptance A2 and A3: the .dem.json tile (read, ranges, shape, keys) and its fingerprint.
    public sealed class Ed3cTileTests
    {
        private static MapDemTile Accepts(string dem)
        {
            MapDemTile tile = null;
            Assert.DoesNotThrow(() => tile = MapDemTile.Read(dem), dem.Length > 300 ? dem.Substring(0, 300) : dem);
            return tile;
        }

        private static void Refuses(string dem, string part = null)
        {
            ArgumentException e = Assert.Throws<ArgumentException>(() => MapDemTile.Read(dem), dem.Length > 300 ? dem.Substring(0, 300) : dem);
            Assert.That(e.Message, Does.StartWith("dem"));
            if (part != null) Assert.That(e.Message, Does.Contain(part));
        }

        [Test]
        public void ExampleTileReads()
        {
            MapDemTile t = Accepts(Tile);
            Assert.That(t.Cols, Is.EqualTo(3));
            Assert.That(t.Rows, Is.EqualTo(3));
            Assert.That(t.StepMm, Is.EqualTo(30000));
            Assert.That(t.Credit, Is.EqualTo(new[] { "synthetic test tile" }));
            Assert.That(t.HeightMm(0, 0), Is.EqualTo(0));
            Assert.That(t.HeightMm(1, 0), Is.EqualTo(30000));
            Assert.That(t.HeightMm(2, 1), Is.EqualTo(60000));
            Assert.That(t.HeightMm(0, 2), Is.EqualTo(30000));
            Assert.That(t.HeightMm(2, 2), Is.EqualTo(54000));
            Assert.That(t.Fingerprint, Is.EqualTo(Fp));
            Assert.Throws<ArgumentOutOfRangeException>(() => t.HeightMm(3, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => t.HeightMm(0, 3));
            Assert.Throws<ArgumentOutOfRangeException>(() => t.HeightMm(-1, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => t.HeightMm(1, -1));
        }

        [Test]
        public void SidesAndStepAcceptBothEndsAndRefuseOneBeyond()
        {
            Accepts(Dem(Grid(2, 2))); Accepts(Dem(Grid(513, 2))); Accepts(Dem(Grid(2, 513)));
            Refuses(Dem(Grid(1, 2)), "cols"); Refuses(Dem(Grid(514, 2)), "cols");
            Refuses(Dem(Grid(2, 1)), "rows"); Refuses(Dem(Grid(2, 514)), "rows");
            Accepts(Dem(Grid(2, 2), step: 1)); Accepts(Dem(Grid(2, 2), step: 100000));
            Refuses(Dem(Grid(2, 2), step: 0), "stepMm"); Refuses(Dem(Grid(2, 2), step: 100001), "stepMm");
        }

        [Test]
        public void HeightsAcceptBothEndsAndRefuseOneBeyond()
        {
            Accepts(Dem(Grid(2, 2, 9000000))); Accepts(Dem(Grid(2, 2, -9000000)));
            Refuses(Dem(Grid(2, 2, 9000001)), "heightsMm"); Refuses(Dem(Grid(2, 2, -9000001)), "heightsMm");
            int[][] one = Grid(3, 3);
            one[2][1] = 9000001;
            Refuses(Dem(one), "heightsMm");
            Refuses(Dem(T).Replace("24000", "24000.5"), "heightsMm");
            Refuses(Dem(T).Replace("24000", "\"24000\""), "heightsMm");
            Refuses(Dem(T).Replace("24000", "007"));
        }

        [Test]
        public void CreditTakesOneToEightTexts()
        {
            Assert.That(Accepts(Dem(T, credit: "[\"a\"]")).Credit, Is.EqualTo(new[] { "a" }));
            MapDemTile eight = Accepts(Dem(T, credit: "[\"1\", \"2\", \"3\", \"4\", \"5\", \"6\", \"7\", \"8\"]"));
            Assert.That(eight.Credit.Count, Is.EqualTo(8));
            Assert.That(eight.Credit[7], Is.EqualTo("8"));
            Refuses(Dem(T, credit: "[]"), "credit");
            Refuses(Dem(T, credit: "[\"1\", \"2\", \"3\", \"4\", \"5\", \"6\", \"7\", \"8\", \"9\"]"), "credit");
            Refuses(Dem(T, credit: "[\"\"]"), "credit");
            Refuses(Dem(T, credit: "[\"" + new string('x', 256) + "\"]"), "credit");
            Accepts(Dem(T, credit: "[\"" + new string('x', 255) + "\"]"));
            Refuses(Dem(T, credit: "[\"a\\u0000b\"]"), "credit");
            Refuses(Dem(T, credit: "\"a\""), "credit");
            Refuses(Dem(T, credit: "[1]"), "credit");
        }

        [Test]
        public void ShapeMustMatchColsAndRows()
        {
            Refuses(Dem(T, rows: "2"), "heightsMm");
            Refuses(Dem(T, rows: "4"), "heightsMm");
            Refuses(Dem(T, cols: "2"), "heightsMm");
            Refuses(Dem(T, cols: "4"), "heightsMm");
            int[][] ragged = { new[] { 0, 1, 2 }, new[] { 0, 1 }, new[] { 0, 1, 2 } };
            Refuses(Dem(ragged), "heightsMm");
            Refuses(Dem(T).Replace("[[0, 30000, 45000]", "[0"), "heightsMm");
            Refuses(Dem(T).Replace("\"heightsMm\": [[", "\"heightsMm\": { \"x\": [[").Replace("]] }", "]] } }"), "heightsMm");
        }

        [Test]
        public void KeysAndVersionAreChecked()
        {
            Refuses(Dem(T, version: "2"), "version");
            Refuses(Dem(T, version: "0"), "version");
            Refuses(Dem(T).Replace("\"version\": 1, ", ""), "version");
            Refuses(Dem(T).Replace("\"stepMm\": 30000, ", ""), "stepMm");
            Refuses(Dem(T).Replace("\"credit\": " + Credit + ", ", ""), "credit");
            Refuses(Dem(T).Replace("\"cols\": 3, ", ""), "cols");
            Refuses(Dem(T).Replace("\"rows\": 3, ", ""), "rows");
            Refuses(Dem(T).Replace(", \"heightsMm\"", ", \"heightsMn\": 0, \"heightsMm\""), "heightsMn");
            Refuses(Dem(T).Replace("\"stepMm\": 30000", "\"stepMm\": 30000, \"stepMm\": 30000"), "stepMm");
            Refuses(Dem(T).Replace("\"rows\": 3", "\"rows\": 3.0"), "rows");
            Refuses("[]");
            Refuses("{ \"version\": 1");
            Assert.Throws<ArgumentNullException>(() => MapDemTile.Read(null));
        }

        [Test]
        public void FingerprintCoversTheGridOnly()
        {
            Assert.That(Accepts(Tile).Fingerprint, Is.EqualTo(Fp));
            Assert.That(Accepts(Dem(T).Replace("24000", "24001")).Fingerprint, Is.EqualTo(Fp24001));
            Assert.That(Accepts(Dem(T, credit: "[\"other\", \"words\"]")).Fingerprint, Is.EqualTo(Fp));
            string spaced = Tile.Replace(", ", " ,\n\t ").Replace("[", "[ \r\n").Replace(": ", "  :  ");
            Assert.That(Accepts(spaced).Fingerprint, Is.EqualTo(Fp));
            Assert.That(Accepts(Tile.Replace("[[0,", "[[-0,")).Fingerprint, Is.EqualTo(Fp));
            Assert.That(Accepts(Dem(Shift(T, -60000))).Fingerprint, Is.EqualTo(FpMinus60000));
            Assert.That(Accepts(Dem(T, step: 30001)).Fingerprint, Is.Not.EqualTo(Fp));
            int[][] wide = { new[] { 0, 30000, 45000, 9000 }, new[] { 24000, 60000, 30000, 45000 } };
            Assert.That(Accepts(Dem(wide)).Fingerprint, Does.Match("^[0-9a-f]{64}$"));
            Assert.That(Accepts(Dem(wide)).Fingerprint, Is.Not.EqualTo(Accepts(Dem(new[] { new[] { 0, 30000 }, new[] { 45000, 9000 },
                new[] { 24000, 60000 }, new[] { 30000, 45000 } })).Fingerprint));
        }

        [Test]
        public void LargestTileReads()
        {
            int[][] big = Grid(513, 513);
            for (int b = 0; b < 513; b++)
                for (int a = 0; a < 513; a++) big[b][a] = (a * 7919 + b * 104729) % 18000001 - 9000000;
            MapDemTile t = Accepts(Dem(big, step: 100000));
            Assert.That(t.HeightMm(512, 512), Is.EqualTo(big[512][512]));
            Assert.That(t.HeightMm(511, 3), Is.EqualTo(big[3][511]));
            Assert.That(t.Fingerprint, Does.Match("^[0-9a-f]{64}$"));
        }
    }
}
