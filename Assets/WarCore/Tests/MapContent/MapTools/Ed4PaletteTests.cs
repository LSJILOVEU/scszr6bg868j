using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Presentation;

namespace WarCore.Tests
{
    // ED-4 acceptance 3: palette.json (read per cartridge, defaults for what is missing, errors that name cartridge and field),
    // the fall-back colour of a surface the palette does not list, and the reserved file name.
    public sealed class Ed4PaletteTests
    {
        private Ed0Dirs dirs;

        [SetUp] public void Make() => dirs = new Ed0Dirs();
        [TearDown] public void Clean() => dirs.Dispose();

        private MapPalette Read(string json, string cartridge = "Tint")
        {
            string dir = Ed4Fixtures.Cartridge(dirs, cartridge, json);
            return MapPalette.Load(Path.Combine(dir, "palette.json"), cartridge);
        }

        // A valid file must load: a refusal is an assertion failure here, not an error of the test.
        private MapPalette ReadOk(string json, string cartridge = "Tint")
        {
            MapPalette palette = null;
            Assert.DoesNotThrow(() => palette = Read(json, cartridge));
            return palette;
        }

        [Test]
        public void TheRwrPaletteGivesThreeGroundColoursCliffStructureTeamsAndNeutral()
        {
            MapPalette p = MapPalette.ForLayer(ContentCatalog.Find(), "rwr");
            Assert.That(p.Ground.OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => g.Key + "=" + Ed4Fixtures.Hex(g.Value)),
                Is.EqualTo(new[] { "grass=8FA06A", "mud=6E5A44", "road=B5A47E" }));
            Assert.That(Ed4Fixtures.Hex(p.Cliff), Is.EqualTo("8A8378"));
            Assert.That(Ed4Fixtures.Hex(p.StructSide), Is.EqualTo("A39E95"));
            Assert.That(Ed4Fixtures.Hex(p.StructTop), Is.EqualTo("B8B3AA"));
            Assert.That(p.Team.Select(Ed4Fixtures.Hex), Is.EqualTo(new[] { "2F6FD6", "D6452F" }));
            Assert.That(Ed4Fixtures.Hex(p.Neutral), Is.EqualTo("E8E4D8"));
        }

        [Test]
        public void EveryLayerOfACartridgeUsesTheCartridgesOnePalette()
        {
            MapPalette a = MapPalette.ForLayer(ContentCatalog.Find(), "rwr"), b = MapPalette.ForLayer(ContentCatalog.Find(), "rwr-m7-playtest");
            Assert.That(b.Ground.OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => g.Key + g.Value),
                Is.EqualTo(a.Ground.OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => g.Key + g.Value)));
        }

        [Test]
        public void AMissingFileGivesTheDefaultsWithNoGroundColoursAtAll()
        {
            MapPalette p = MapPalette.ForLayer(new ContentCatalog(new[] { Ed4Fixtures.Cartridge(dirs, "Bare", null) }), "bare1");
            Assert.That(p.Ground, Is.Empty);
            Assert.That(Ed4Fixtures.Hex(p.Cliff), Is.EqualTo("8A8378"));
            Assert.That(Ed4Fixtures.Hex(p.StructSide), Is.EqualTo("A39E95"));
            Assert.That(Ed4Fixtures.Hex(p.StructTop), Is.EqualTo("B8B3AA"));
            Assert.That(p.Team.Select(Ed4Fixtures.Hex), Is.EqualTo(new[] { "2F6FD6", "D6452F" }));
            Assert.That(Ed4Fixtures.Hex(p.Neutral), Is.EqualTo("E8E4D8"));
            Assert.That(Ed4Fixtures.Hex(p.NoGround), Is.EqualTo("9A9A8C"));
            int colour = p.GroundColor("grass", out bool fallback);
            Assert.That(fallback, Is.True);
            Assert.That(Ed4Fixtures.Hex(colour), Is.EqualTo("8D7C7F"));
            Assert.That(MapPalette.Load(Path.Combine(dirs.Root, "nowhere.json"), "Bare").Ground, Is.Empty);
        }

        [Test]
        public void AMissingSectionKeepsTheDefaultAndTheOthersAreRead()
        {
            MapPalette p = ReadOk("{ \"version\": 1, \"cliff\": \"#102030\", \"team\": [\"#010203\", \"#040506\", \"#070809\"] }");
            Assert.That(Ed4Fixtures.Hex(p.Cliff), Is.EqualTo("102030"));
            Assert.That(p.Team.Select(Ed4Fixtures.Hex), Is.EqualTo(new[] { "010203", "040506", "070809" }));
            Assert.That(Ed4Fixtures.Hex(p.Neutral), Is.EqualTo("E8E4D8"));
            Assert.That(Ed4Fixtures.Hex(p.StructTop), Is.EqualTo("B8B3AA"));
            Assert.That(p.Ground, Is.Empty);
            Assert.That(Ed4Fixtures.Hex(ReadOk("{}").Cliff), Is.EqualTo("8A8378"));
        }

        [Test]
        public void UnknownTopLevelSectionsAreIgnoredWhateverTheyHold()
        {
            MapPalette p = ReadOk("{ \"version\": 1, \"prop\": { \"crate\": \"nonsense\" }, \"unit\": 5, \"fx\": [1, 2], \"future\": true, "
                + "\"ground\": { \"grass\": [\"#112233\"] } }");
            Assert.That(Ed4Fixtures.Hex(p.GroundColor("grass", out bool fallback)), Is.EqualTo("112233"));
            Assert.That(fallback, Is.False);
        }

        [Test]
        public void TheStructureSectionReadsOnlySlotOneAndAMissingSlotOneIsTheDefault()
        {
            MapPalette one = ReadOk("{ \"struct\": { \"1\": { \"side\": \"#111111\", \"top\": \"#222222\" }, \"2\": \"not a colour\" } }");
            Assert.That(Ed4Fixtures.Hex(one.StructSide), Is.EqualTo("111111"));
            Assert.That(Ed4Fixtures.Hex(one.StructTop), Is.EqualTo("222222"));
            MapPalette none = ReadOk("{ \"struct\": { \"2\": { \"side\": \"#333333\", \"top\": \"#444444\" } } }");
            Assert.That(Ed4Fixtures.Hex(none.StructSide), Is.EqualTo("A39E95"));
            Assert.That(Ed4Fixtures.Hex(none.StructTop), Is.EqualTo("B8B3AA"));
            MapPalette half = ReadOk("{ \"struct\": { \"1\": { \"top\": \"#555555\" } } }");
            Assert.That(Ed4Fixtures.Hex(half.StructSide), Is.EqualTo("A39E95"));
            Assert.That(Ed4Fixtures.Hex(half.StructTop), Is.EqualTo("555555"));
        }

        [Test]
        public void ColoursMayBeWrittenInEitherCase()
        {
            Assert.That(Ed4Fixtures.Hex(ReadOk("{ \"cliff\": \"#aBcDeF\" }").Cliff), Is.EqualTo("ABCDEF"));
        }

        // All sixteen digits, each in both cases, at both ends of each range (0, 9, A, F, a, f).
        [Test]
        public void EveryHexDigitInBothCasesReadsItsValue()
        {
            MapPalette p = ReadOk("{ \"ground\": { \"a\": [\"#012345\"], \"b\": [\"#6789AB\"], \"c\": [\"#CDEFab\"], \"d\": [\"#cdef09\"] } }");
            Assert.That(Ed4Fixtures.Hex(p.GroundColor("a", out _)), Is.EqualTo("012345"));
            Assert.That(p.GroundColor("a", out _), Is.EqualTo(0x012345));
            Assert.That(p.GroundColor("b", out _), Is.EqualTo(0x6789AB));
            Assert.That(p.GroundColor("c", out _), Is.EqualTo(0xCDEFAB));
            Assert.That(p.GroundColor("d", out _), Is.EqualTo(0xCDEF09));
            Assert.That(ReadOk("{ \"cliff\": \"#9fAf0F\" }").Cliff, Is.EqualTo(0x9FAF0F));
        }

        [Test]
        public void GroundOfOneAndTwoColoursAndTeamsOfTwoAndFourAreAccepted()
        {
            MapPalette p = ReadOk("{ \"ground\": { \"a\": [\"#000001\"], \"b\": [\"#000002\", \"#000003\"] }, "
                + "\"team\": [\"#000004\", \"#000005\", \"#000006\", \"#000007\"] }");
            Assert.That(Ed4Fixtures.Hex(p.GroundColor("a", out _)), Is.EqualTo("000001"));
            Assert.That(Ed4Fixtures.Hex(p.GroundColor("b", out _)), Is.EqualTo("000002"));
            Assert.That(p.Team.Count, Is.EqualTo(4));
            Assert.That(ReadOk("{ \"team\": [\"#000004\", \"#000005\"] }").Team.Count, Is.EqualTo(2));
        }

        // Each broken file is refused with an InvalidDataException that names the cartridge and the field.
        [TestCase("{ \"ground\": { \"mud\": \"#6E5A44\" } }", "ground.mud", TestName = "GroundValueNotAnArray")]
        [TestCase("{ \"ground\": { \"mud\": [] } }", "ground.mud", TestName = "GroundNoColour")]
        [TestCase("{ \"ground\": { \"mud\": [\"#000001\", \"#000002\", \"#000003\"] } }", "ground.mud", TestName = "GroundThreeColours")]
        [TestCase("{ \"ground\": { \"mud\": [\"6E5A44\"] } }", "ground.mud[0]", TestName = "GroundColourWithoutHash")]
        [TestCase("{ \"ground\": { \"mud\": [\"#6E5A44\", \"#GG0000\"] } }", "ground.mud[1]", TestName = "GroundSecondColourNotHex")]
        [TestCase("{ \"ground\": { \"mud\": [\"#6E5A4\"] } }", "ground.mud[0]", TestName = "GroundColourTooShort")]
        [TestCase("{ \"ground\": { \"mud\": [\"#6E5A4400\"] } }", "ground.mud[0]", TestName = "GroundColourWithAlpha")]
        [TestCase("{ \"ground\": [] }", "ground", TestName = "GroundNotAnObject")]
        [TestCase("{ \"version\": 2 }", "version", TestName = "VersionTwo")]
        [TestCase("{ \"version\": 0 }", "version", TestName = "VersionZero")]
        [TestCase("{ \"version\": 1.5 }", "version", TestName = "VersionNotAnInteger")]
        [TestCase("{ \"version\": 1.0 }", "version", TestName = "VersionWithDecimalPoint")]
        [TestCase("{ \"version\": \"1\" }", "version", TestName = "VersionAString")]
        [TestCase("{ \"team\": [\"#000001\"] }", "team", TestName = "OneTeam")]
        [TestCase("{ \"team\": [\"#000001\", \"#000002\", \"#000003\", \"#000004\", \"#000005\"] }", "team", TestName = "FiveTeams")]
        [TestCase("{ \"team\": [\"#000001\", \"blue\"] }", "team[1]", TestName = "TeamColourNotHex")]
        [TestCase("{ \"team\": \"#000001\" }", "team", TestName = "TeamNotAnArray")]
        [TestCase("{ \"cliff\": \"red\" }", "cliff", TestName = "CliffNotHex")]
        [TestCase("{ \"cliff\": \"x6E5A44\" }", "cliff", TestName = "CliffWithoutTheHashSign")]
        [TestCase("{ \"cliff\": 5 }", "cliff", TestName = "CliffNotAString")]
        [TestCase("{ \"neutral\": [\"#000001\"] }", "neutral", TestName = "NeutralNotAString")]
        [TestCase("{ \"struct\": { \"1\": { \"side\": \"#12\" } } }", "struct.1.side", TestName = "StructSideNotHex")]
        [TestCase("{ \"struct\": { \"1\": { \"top\": 3 } } }", "struct.1.top", TestName = "StructTopNotAString")]
        [TestCase("{ \"struct\": { \"1\": \"#000001\" } }", "struct.1", TestName = "StructSlotNotAnObject")]
        [TestCase("{ \"struct\": 7 }", "struct", TestName = "StructNotAnObject")]
        [TestCase("{ \"struct\": [] }", "struct", TestName = "StructAnEmptyList")]
        [TestCase("{ \"struct\": \"x\" }", "struct", TestName = "StructAString")]
        [TestCase("{ \"struct\": { \"1\": [] } }", "struct.1", TestName = "StructSlotAnEmptyList")]
        [TestCase("[ \"#000001\" ]", "palette.json", TestName = "RootNotAnObject")]
        public void ABrokenFileIsRefusedNamingCartridgeAndField(string json, string field)
        {
            var e = Assert.Throws<InvalidDataException>(() => Read(json, "Tint"));
            StringAssert.Contains("Tint palette.json", e.Message);
            StringAssert.Contains(field, e.Message);
        }

        [Test]
        public void AFileThatIsNotJsonIsRefusedAsInvalidDataWithTheCartridgeName()
        {
            var e = Assert.Throws<InvalidDataException>(() => Read("{ \"ground\": ", "Tint"));
            StringAssert.Contains("Tint", e.Message);
            var nullValue = Assert.Throws<InvalidDataException>(() => Read("{ \"cliff\": null }", "Tint"));
            StringAssert.Contains("Tint", nullValue.Message);
        }

        [Test]
        public void TheCartridgeNameInTheMessageIsTheOneGiven()
        {
            string dir = Ed4Fixtures.Cartridge(dirs, "Other", "{ \"version\": 3 }");
            var e = Assert.Throws<InvalidDataException>(() => MapPalette.ForLayer(new ContentCatalog(new[] { dir }), "other1"));
            StringAssert.Contains("Other palette.json version", e.Message);
        }

        // FNV-1a over the UTF-8 bytes, lowest byte = red; each channel 112 + (byte & 31). Expected values worked out by hand.
        [TestCase("grass", "8D7C7F")]
        [TestCase("mud", "797074")]
        [TestCase("road", "8B7C8F")]
        [TestCase("\u6c99", "887D8E")]
        public void TheFallbackColourOfASurfaceNameIsFixed(string name, string rgb)
        {
            Assert.That(Ed4Fixtures.Hex(MapPalette.FallbackColor(name)), Is.EqualTo(rgb));
        }

        [Test]
        public void EveryFallbackChannelStaysBetween112And143()
        {
            for (int i = 0; i < 2000; i++)
            {
                int c = MapPalette.FallbackColor("surface-" + i);
                foreach (int channel in new[] { c >> 16 & 255, c >> 8 & 255, c & 255 })
                    Assert.That(channel, Is.InRange(112, 143), "surface-" + i);
                Assert.That(c >> 24, Is.EqualTo(0));
            }
            Assert.That(Enumerable.Range(0, 2000).Select(i => MapPalette.FallbackColor("surface-" + i)).Distinct().Count(), Is.GreaterThan(1000));
        }

        [Test]
        public void ANameInGroundIsNotAFallbackAndAnotherNameIs()
        {
            MapPalette p = ReadOk("{ \"ground\": { \"grass\": [\"#112233\"] } }");
            p.GroundColor("grass", out bool listed);
            p.GroundColor("mud", out bool unlisted);
            Assert.That(listed, Is.False);
            Assert.That(unlisted, Is.True);
        }

        [Test]
        public void PaletteJsonIsNotALayerAndTheCatalogNamesTheDirectoryOfACartridge()
        {
            string dir = Ed4Fixtures.Cartridge(dirs, "Tint", "{}");
            var catalog = new ContentCatalog(new[] { dir });
            Assert.That(catalog.Layers("Tint"), Is.EqualTo(new[] { "tint1" }));
            Assert.That(ContentCatalog.ColorsFile, Is.EqualTo("palette"));
            Assert.That(catalog.DirectoryOf("Tint"), Is.EqualTo(dir));
            string other = Ed4Fixtures.Cartridge(dirs, "Zed", null);
            var two = new ContentCatalog(new[] { other, dir });
            Assert.That(two.DirectoryOf("Tint"), Is.EqualTo(dir));
            Assert.That(two.DirectoryOf("Zed"), Is.EqualTo(other));
            Assert.Throws<ContentException>(() => two.DirectoryOf("Nothing"));
        }

        [Test]
        public void TheShippedRwrDirectoryIsContentRwrData()
        {
            string dir = Path.GetFullPath(ContentCatalog.Find().DirectoryOf("Rwr"));
            Assert.That(dir.Replace('\\', '/'), Does.EndWith("Assets/WarCore/Content/Rwr/Data"));
            Assert.That(File.Exists(Path.Combine(dir, "palette.json")), Is.True);
            Assert.That(ContentCatalog.Find().Layers("Rwr"), Does.Not.Contain("palette"));
        }

        [Test]
        public void ThePaletteFileIsNotReadByTheContentLoaderSoNoHashMoves()
        {
            string dir = Ed4Fixtures.Cartridge(dirs, "Tint", "{ \"cliff\": \"#102030\" }");
            string before = new FileContentSource(dir).TryRead("tint1", out string json, out _) ? json : null;
            File.WriteAllText(Path.Combine(dir, "palette.json"), "{ \"cliff\": \"#ABCDEF\", \"version\": 1 }", new UTF8Encoding(false));
            string after = new FileContentSource(dir).TryRead("tint1", out json, out _) ? json : null;
            Assert.That(after, Is.EqualTo(before));
            Assert.That(new ContentCatalog(new[] { dir }).Load("tint1").Id, Is.EqualTo("tint1"));
        }
    }
}
