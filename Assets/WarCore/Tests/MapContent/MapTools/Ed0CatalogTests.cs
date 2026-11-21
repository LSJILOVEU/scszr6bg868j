using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Presentation;

namespace WarCore.Tests
{
    // ED-0 acceptance 5: cartridges and layers of throw-away data directories.
    public sealed class Ed0CatalogTests
    {
        private Ed0Dirs dirs;

        [SetUp] public void Make() => dirs = new Ed0Dirs();
        [TearDown] public void Clean() => dirs.Dispose();

        // Twelve layers written in reverse order, so neither the file system's order nor the order given can pass for ordinal.
        private static readonly string[] Twelve = { "l00", "l01", "l02", "l03", "l04", "l05", "l06", "l07", "l08", "l09", "l10", "l11" };

        private string TwelveLayers(string relative)
        {
            string dir = dirs.Dir(relative);
            for (int i = Twelve.Length - 1; i >= 0; i--) Ed0Dirs.Layer(dir, Twelve[i]);
            return dir;
        }

        [Test]
        public void CartridgeNamesFollowTheDataFolderRuleInOrdinalOrder()
        {
            string alpha = dirs.Dir("alpha");
            Ed0Dirs.Layer(alpha, "a1");
            string zeta = dirs.Dir("Zeta/Data");
            Ed0Dirs.Layer(zeta, "z1");
            var catalog = new ContentCatalog(new[] { alpha, zeta });
            Assert.That(catalog.Cartridges, Is.EqualTo(new[] { "Zeta", "alpha" }));
            Assert.That(catalog.CartridgeOf("z1"), Is.EqualTo("Zeta"));
            Assert.That(catalog.CartridgeOf("a1"), Is.EqualTo("alpha"));
        }

        [Test]
        public void LayersAreOrdinalAndLeaveTheReservedFilesOut()
        {
            string dir = TwelveLayers("Big/Data");
            File.WriteAllText(Path.Combine(dir, "palette.json"), "{}");
            Ed0Dirs.Editor(dir, "{ \"defaultLayer\": \"l07\" }");
            File.WriteAllText(Path.Combine(dir, "notes.txt"), "not a layer");
            var catalog = new ContentCatalog(new[] { dir });
            Assert.That(catalog.Layers("Big"), Is.EqualTo(Twelve));
            foreach (string id in Twelve) Assert.That(catalog.Load(id).Id, Is.EqualTo(id));
            Assert.That(catalog.DefaultLayer("Big"), Is.EqualTo("l07"));
        }

        [Test]
        public void ExtendsStaysInsideTheHoldersDirectory()
        {
            string one = dirs.Dir("One"), two = dirs.Dir("Two");
            Ed0Dirs.Layer(one, "base1", "", "one.soldier");
            File.WriteAllText(Path.Combine(one, "child.json"), "{ \"id\": \"child\", \"extends\": \"base1\" }");
            Ed0Dirs.Layer(two, "base2", "", "two.soldier");
            var catalog = new ContentCatalog(new[] { two, one });
            ContentPack child = catalog.Load("child");
            Assert.That(child.UnitTypeCount, Is.EqualTo(1));
            Assert.That(child.UnitTypeId(0), Is.EqualTo("one.soldier"));
        }

        [Test]
        public void TwoDirectoriesWithOneCartridgeNameAreRefused()
        {
            string a = dirs.Dir("x/Same/Data"), b = dirs.Dir("y/Same");
            var e = Assert.Throws<ContentException>(() => new ContentCatalog(new[] { a, b }));
            StringAssert.Contains("'Same'", e.Message);
        }

        [Test]
        public void ALayerInTwoCartridgesNamesBoth()
        {
            string a = dirs.Dir("Aa"), b = dirs.Dir("Bb");
            Ed0Dirs.Layer(a, "dup");
            Ed0Dirs.Layer(b, "dup");
            var catalog = new ContentCatalog(new[] { a, b });
            var e = Assert.Throws<ContentException>(() => catalog.Load("dup"));
            StringAssert.Contains("'dup'", e.Message);
            StringAssert.Contains("Aa", e.Message);
            StringAssert.Contains("Bb", e.Message);
        }

        [Test]
        public void AMissingLayerNamesTheCartridgesLookedIn()
        {
            string a = dirs.Dir("Aa");
            Ed0Dirs.Layer(a, "here");
            var catalog = new ContentCatalog(new[] { a });
            var e = Assert.Throws<ContentException>(() => catalog.Load("gone"));
            StringAssert.Contains("'gone'", e.Message);
            StringAssert.Contains("Aa", e.Message);
            Assert.Throws<ContentException>(() => catalog.Layers("Bb"));
            Assert.Throws<ContentException>(() => catalog.DefaultLayer("Bb"));
        }

        [Test]
        public void AMissingDirectoryIsRefused()
        {
            string missing = Path.Combine(dirs.Root, "nowhere");
            var e = Assert.Throws<ContentException>(() => new ContentCatalog(new[] { missing }));
            StringAssert.Contains(missing, e.Message);
        }

        [TestCase("{ \"defaultLayer\": \"ok\", \"extra\": 1 }", "'extra'")]
        [TestCase("{ \"defaultLayer\": \"elsewhere\" }", "'elsewhere'")]
        [TestCase("[ \"ok\" ]", "JSON object")]
        [TestCase("{ \"defaultLayer\": 3 }", "must be a string")]
        public void ABadEditorFileFailsItsCartridgeOnly(string text, string expected)
        {
            string bad = dirs.Dir("Bad"), good = dirs.Dir("Good");
            Ed0Dirs.Layer(bad, "ok");
            Ed0Dirs.Layer(dirs.Dir("Other"), "elsewhere");
            Ed0Dirs.Editor(bad, text);
            Ed0Dirs.Layer(good, "fine");
            Ed0Dirs.Editor(good, "{ \"defaultLayer\": \"fine\" }");
            var catalog = new ContentCatalog(new[] { bad, good, Path.Combine(dirs.Root, "Other") });
            var e = Assert.Throws<ContentException>(() => catalog.DefaultLayer("Bad"));
            StringAssert.Contains(expected, e.Message);
            Assert.That(catalog.DefaultLayer("Good"), Is.EqualTo("fine"));
            Assert.That(catalog.Load("ok").Id, Is.EqualTo("ok"));
        }

        [Test]
        public void WithoutAnEditorFileTheFirstLayerWithTerrainAndMatchIsTheDefault()
        {
            string dir = dirs.Dir("Mixed");
            Ed0Dirs.Layer(dir, "a-terrain", ", " + Ed0Dirs.Terrain);
            Ed0Dirs.Layer(dir, "b-match", ", " + Ed0Dirs.Match);
            Ed0Dirs.Layer(dir, "c-both", ", " + Ed0Dirs.Match + ", " + Ed0Dirs.Terrain);
            Ed0Dirs.Layer(dir, "d-both", ", " + Ed0Dirs.Match + ", " + Ed0Dirs.Terrain);
            string none = dirs.Dir("None");
            Ed0Dirs.Layer(none, "e-terrain", ", " + Ed0Dirs.Terrain);
            Ed0Dirs.Layer(none, "f-match", ", " + Ed0Dirs.Match);
            var catalog = new ContentCatalog(new[] { dir, none });
            Assert.That(catalog.DefaultLayer("Mixed"), Is.EqualTo("c-both"));
            Assert.That(catalog.DefaultLayer("None"), Is.Null);
        }

        [Test]
        public void AnEditorFileWithoutADefaultLayerHasNone()
        {
            string dir = dirs.Dir("Quiet");
            Ed0Dirs.Layer(dir, "both", ", " + Ed0Dirs.Match + ", " + Ed0Dirs.Terrain);
            Ed0Dirs.Editor(dir, "{}");
            Assert.That(new ContentCatalog(new[] { dir }).DefaultLayer("Quiet"), Is.Null);
        }

        [Test]
        public void TheFacadeReadsNamesFromTheData()
        {
            string dir = dirs.Dir("Facade/Data");
            Ed0Dirs.Layer(dir, "plain", "", "p.one", "p.two");
            Ed0Dirs.Layer(dir, "ground", ", " + Ed0Dirs.Match + ", " + Ed0Dirs.Terrain);
            Ed0Dirs.UseList(dir);
            Assert.That(MapCatalog.Cartridges(), Is.EqualTo(new[] { "Facade" }));
            Assert.That(MapCatalog.Layers("Facade"), Is.EqualTo(new[] { "ground", "plain" }));
            Assert.That(MapCatalog.DefaultLayer("Facade"), Is.EqualTo("ground"));
            Assert.That(MapCatalog.UnitTypeNames("plain"), Is.EqualTo(new[] { "p.one", "p.two" }));
            IReadOnlyList<string> plainSurfaces = null;
            Assert.DoesNotThrow(() => plainSurfaces = MapCatalog.SurfaceNames("plain"));
            Assert.That(plainSurfaces, Is.Empty);
            Assert.That(MapCatalog.SurfaceNames("ground"), Is.EqualTo(new[] { "grass", "mud" }));
            Assert.That(MapCatalog.LayerHasTerrain("plain"), Is.False);
            Assert.That(MapCatalog.LayerHasTerrain("ground"), Is.True);
            Assert.That(MapCatalog.LayerHasMatch("plain"), Is.False);
            Assert.That(MapCatalog.LayerHasMatch("ground"), Is.True);

            // A unit type added to the data shows up with no code change.
            Ed0Dirs.Layer(dir, "plain", "", "p.one", "p.two", "p.three");
            Assert.That(MapCatalog.UnitTypeNames("plain"), Is.EqualTo(new[] { "p.one", "p.two", "p.three" }));
        }
    }
}
