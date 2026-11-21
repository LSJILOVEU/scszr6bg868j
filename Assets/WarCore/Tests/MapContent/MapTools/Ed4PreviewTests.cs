using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Content.MapTools;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // ED-4 acceptance 4: the preview data of a description (shade, steepness, ground colour, entries, notes) and its JSON.
    public sealed class Ed4PreviewTests
    {
        private Ed0Dirs dirs;

        [SetUp] public void Make() => dirs = new Ed0Dirs();
        [TearDown] public void Clean() => dirs.Dispose();

        private static MapPreviewData Build(byte[] bytes, MapPalette palette = null) =>
            MapPreview.Build(bytes, Ed4Fixtures.PackOf(bytes), palette ?? Ed4Fixtures.PaletteOf(bytes), Ed4Fixtures.Messages());

        [TestCaseSource(typeof(Ed4Fixtures), nameof(Ed4Fixtures.MapNames))]
        public void EveryShippedMapGivesOneEntryPerDescriptionEntryAndOneCellPerCell(string name)
        {
            byte[] bytes = Ed4Fixtures.MapBytes(name);
            MapDesc desc = MapDescJson.Read(bytes);
            MapPreviewData d = Build(bytes);
            Assert.That(d.Id, Is.EqualTo(desc.Id));
            Assert.That(d.Pack, Is.EqualTo(desc.Pack));
            Assert.That((d.Width, d.Depth, d.OriginX, d.OriginZ), Is.EqualTo((desc.Size.X, desc.Size.Z, desc.Origin.X, desc.Origin.Z)));
            Assert.That(d.Walls.Count, Is.EqualTo(desc.Walls.Count));
            Assert.That(d.Plates.Count, Is.EqualTo(desc.Plates.Count));
            Assert.That(d.Points.Count, Is.EqualTo(desc.Points.Count));
            Assert.That(d.Bases.Count, Is.EqualTo(desc.Bases.Count));
            Assert.That(d.Units.Count, Is.EqualTo(desc.Units.Count));
            Assert.That(d.Shade.Count, Is.EqualTo(desc.Size.X * desc.Size.Z));
            Assert.That(d.Steep.Count, Is.EqualTo(desc.Size.X * desc.Size.Z));
            Assert.That(d.Ground.Count, Is.EqualTo(desc.Size.X * desc.Size.Z));
            Assert.That(d.Problems.Count, Is.EqualTo(MapGate.Check(bytes, Ed4Fixtures.PackOf(bytes)).Count));
            Assert.That(d.Notes, Is.Empty, "the Rwr palette lists every surface the shipped maps use");
        }

        [TestCaseSource(typeof(Ed4Fixtures), nameof(Ed4Fixtures.MapNames))]
        public void TheJsonOfAMapIsTheSameBytesTwiceAndReadsBack(string name)
        {
            byte[] bytes = Ed4Fixtures.MapBytes(name);
            string a = MapPreview.ToJson(Build(bytes)), b = MapPreview.ToJson(Build(bytes));
            Assert.That(Encoding.UTF8.GetBytes(a), Is.EqualTo(Encoding.UTF8.GetBytes(b)));
            JsonNode root = ContentJson.Parse(a, name);
            MapPreviewData d = Build(bytes);
            Assert.That(root.Find("width").Text, Is.EqualTo(d.Width.ToString()));
            Assert.That(root.Find("shade").Items.Count, Is.EqualTo(d.Shade.Count));
            Assert.That(root.Find("ground").Items.Count, Is.EqualTo(d.Ground.Count));
            Assert.That(root.Find("walls").Items.Count, Is.EqualTo(d.Walls.Count));
            Assert.That(root.Find("units").Items.Count, Is.EqualTo(d.Units.Count));
            Assert.That(a, Does.Not.Contain("<"));
        }

        [Test]
        public void AnIdWithAScriptEndTagIsWrittenWithoutALessThanSign()
        {
            var map = new E2Map();
            string text = Encoding.UTF8.GetString(map.Bytes()).Replace("\"e2\"", "\"</script><b>\"");
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            MapPreviewData d = MapPreview.Build(bytes, E2Fixtures.M6Pack(), MapPalette.Default, Ed4Fixtures.Messages());
            Assert.That(d.Id, Is.EqualTo("</script><b>"));
            string json = MapPreview.ToJson(d);
            Assert.That(json, Does.Not.Contain("<"));
            Assert.That(json, Does.Contain("\\u003c/script>"));
            Assert.That(ContentJson.Parse(json, "x").Find("id").Text, Is.EqualTo("</script><b>"));
        }

        [Test]
        public void QuotesBackslashesAndControlCharactersInAnIdSurviveTheJson()
        {
            var map = new E2Map();
            string text = Encoding.UTF8.GetString(map.Bytes()).Replace("\"e2\"", "\"a\\\"b\\\\c\\u0001d\\u001fe\\u2028f\\u2029g\"");
            MapPreviewData d = MapPreview.Build(Encoding.UTF8.GetBytes(text), E2Fixtures.M6Pack(), MapPalette.Default, Ed4Fixtures.Messages());
            Assert.That(d.Id, Is.EqualTo("a\"b\\c\u0001d\u001fe\u2028f\u2029g"));
            string json = MapPreview.ToJson(d);
            Assert.That(json.IndexOf('\u0001'), Is.EqualTo(-1));
            Assert.That(json.IndexOf('\u001f'), Is.EqualTo(-1));
            Assert.That(json.IndexOf('\u2028'), Is.EqualTo(-1));
            Assert.That(json.IndexOf('\u2029'), Is.EqualTo(-1));
            Assert.That(json, Does.Contain("c\\u0001d\\u001fe\\u2028f\\u2029g"));
            Assert.That(ContentJson.Parse(json, "x").Find("id").Text, Is.EqualTo(d.Id));
        }

        // A 3 x 2 map with heights that give every shade branch: both ends of the clamp, a flat cell, the two truncating
        // divisions and cells that differ if x and z (or the row stride) are mixed up. Values worked out by hand from
        //   gx = ((h10 + h11) - (h00 + h01)) / 2, gz = ((h01 + h11) - (h00 + h10)) / 2, shade = clamp(160 - (gx + gz) * 48 / 65536, 40, 240)
        // with every division truncating toward zero.
        private static E2Map HandHeights()
        {
            var map = new E2Map { Pack = "rwr-terrain-demo", Width = 3, Depth = 2, FloorRaw = -100000 };
            map.Heights = new[]
            {
                new[] { 32768, -40000, -16384, 4096 },
                new[] { -91750, -91750, 8193, -16384 },
                new[] { 0, 8193, 91750, 16384 },
            };
            return map;
        }

        private static readonly int[] HandShade = { 240, 125, 160, 87, 40, 155 };
        private static readonly bool[] HandSteep = { true, true, false, true, true, true };

        [Test]
        public void ShadeAndSteepnessOfAHandMapAreFixed()
        {
            byte[] bytes = HandHeights().Bytes();
            ContentPack pack = E2Fixtures.TerrainPack();
            MapPreviewData d = MapPreview.Build(bytes, pack, MapPalette.Default, Ed4Fixtures.Messages());
            Assert.That(d.Shade, Is.EqualTo(HandShade));
            Assert.That(d.Steep, Is.EqualTo(HandSteep));
        }

        [Test]
        public void AFlatMapIsShadedMidGreyAndNeverSteep()
        {
            var map = new E2Map { Pack = "rwr-terrain-demo", Width = 3, Depth = 2 };
            MapPreviewData d = MapPreview.Build(map.Bytes(), E2Fixtures.TerrainPack(), MapPalette.Default, Ed4Fixtures.Messages());
            Assert.That(d.Shade, Is.EqualTo(new[] { 160, 160, 160, 160, 160, 160 }));
            Assert.That(d.Steep, Is.EqualTo(new bool[6]));
        }

        [Test]
        public void AFlatHeightTableIsAlsoMidGrey()
        {
            var map = new E2Map { Pack = "rwr-terrain-demo", Width = 2, Depth = 2 };
            map.Heights = new[] { new[] { 5000, 5000, 5000 }, new[] { 5000, 5000, 5000 }, new[] { 5000, 5000, 5000 } };
            MapPreviewData d = MapPreview.Build(map.Bytes(), E2Fixtures.TerrainPack(), MapPalette.Default, Ed4Fixtures.Messages());
            Assert.That(d.Shade, Is.EqualTo(new[] { 160, 160, 160, 160 }));
        }

        // Light comes from -x -z: a rise towards +x or +z darkens a cell, a fall brightens it, by the same amount for either axis.
        [TestCase(8192, 0, 154)]
        [TestCase(0, 8192, 154)]
        [TestCase(-8192, 0, 166)]
        [TestCase(0, -8192, 166)]
        [TestCase(8192, 8192, 148)]
        [TestCase(1, 0, 160)]
        [TestCase(-1, 0, 160)]
        public void ShadeFollowsTheSlopeOfOneCell(int rise, int riseZ, int expected)
        {
            // The plane h = rise * u + riseZ * v over the cell: h00 = 0, h10 = rise, h01 = riseZ, h11 = rise + riseZ, so gx = rise and gz = riseZ.
            var map = new E2Map { Pack = "rwr-terrain-demo", Width = 1, Depth = 1, FloorRaw = -100000 };
            map.Heights = new[] { new[] { 0, rise }, new[] { riseZ, rise + riseZ } };
            MapPreviewData d = MapPreview.Build(map.Bytes(), E2Fixtures.TerrainPack(), MapPalette.Default, Ed4Fixtures.Messages());
            Assert.That(d.Shade.Single(), Is.EqualTo(expected));
        }

        // An odd corner sum is halved toward zero, not toward minus infinity: -2731 / 2 = -1365 puts the cell at shade 160, and
        // -1366 would give 161 (1365 * 48 / 65536 truncates to 0, 1366 * 48 / 65536 to 1).
        [Test]
        public void AnOddCornerSumIsHalvedTowardZero()
        {
            Assert.That(OneCellShade(0, -1366, 0, -1365), Is.EqualTo(160));   // gx numerator -2731, gz numerator 1
            Assert.That(OneCellShade(0, 0, -1366, -1365), Is.EqualTo(160));   // gz numerator -2731, gx numerator 1
            Assert.That(OneCellShade(0, 1366, 0, 1365), Is.EqualTo(160));
            Assert.That(OneCellShade(0, 0, 1366, 1365), Is.EqualTo(160));
            Assert.That(OneCellShade(0, -1368, 0, -1366), Is.EqualTo(161));   // -2734 / 2 = -1367 * 48 / 65536 = -1
        }

        private static int OneCellShade(int h00, int h10, int h01, int h11)
        {
            var map = new E2Map { Pack = "rwr-terrain-demo", Width = 1, Depth = 1, FloorRaw = -100000 };
            map.Heights = new[] { new[] { h00, h10 }, new[] { h01, h11 } };
            return MapPreview.Build(map.Bytes(), E2Fixtures.TerrainPack(), MapPalette.Default, Ed4Fixtures.Messages()).Shade.Single();
        }

        // A pack without terrain rules uses the legal-surface bound 2/1: a rise of 2 m over a 1 m cell is walkable, one raw unit more is steep.
        [Test]
        public void ThePackWithoutTerrainRulesUsesTheDefaultBoundOfTwoToOne()
        {
            Assert.That(OneCellSteepWith(E2Fixtures.M6Pack(), 131072), Is.False);
            Assert.That(OneCellSteepWith(E2Fixtures.M6Pack(), 131073), Is.True);
            Assert.That(OneCellSteepWith(E2Fixtures.M6Pack(), -131072), Is.False);
            Assert.That(OneCellSteepWith(E2Fixtures.M6Pack(), -131073), Is.True);
        }

        private static bool OneCellSteepWith(ContentPack pack, int rise)
        {
            var map = new E2Map { Width = 1, Depth = 1, FloorRaw = -300000 };
            map.Heights = new[] { new[] { 0, rise }, new[] { 0, rise } };
            return MapPreview.Build(map.Bytes(), pack, MapPalette.Default, Ed4Fixtures.Messages()).Steep.Single();
        }

        // Slope exactly at the pack's 3/4 is walkable; one raw unit more is steep.
        [Test]
        public void SteepnessHasNoGapAtTheWalkableLimit()
        {
            Assert.That(OneCellSteep(49152), Is.False);
            Assert.That(OneCellSteep(49153), Is.True);
            Assert.That(OneCellSteep(-49152), Is.False);
            Assert.That(OneCellSteep(-49153), Is.True);
        }

        private static bool OneCellSteep(int rise)
        {
            var map = new E2Map { Pack = "rwr-terrain-demo", Width = 1, Depth = 1, FloorRaw = -100000 };
            map.Heights = new[] { new[] { 0, rise }, new[] { 0, rise } };
            return MapPreview.Build(map.Bytes(), E2Fixtures.TerrainPack(), MapPalette.Default, Ed4Fixtures.Messages()).Steep.Single();
        }

        [Test]
        public void SteepnessUsesThePacksOwnSlopeNotAFixedOne()
        {
            byte[] bytes = Ed4Fixtures.MapBytes("terrain-demo");
            MapPreviewData loose = MapPreview.Build(bytes, E2Fixtures.TerrainPack(), MapPalette.Default, Ed4Fixtures.Messages());
            MapPreviewData tight = MapPreview.Build(bytes, E2Fixtures.TerrainPack(E2Fixtures.SteepPackJson), MapPalette.Default, Ed4Fixtures.Messages());
            Assert.That(tight.Steep.Count(s => s), Is.GreaterThan(loose.Steep.Count(s => s)));
        }

        [TestCase("terrain-demo")]
        [TestCase("door-terrain-demo")]
        public void SteepnessMatchesTheDerivationCellForCell(string name)
        {
            byte[] bytes = Ed4Fixtures.MapBytes(name);
            ContentPack pack = Ed4Fixtures.PackOf(bytes);
            MapBakeResult baked = MapBake.Run(bytes);
            WcMap map = WcMapReader.Read(baked.WcMapBytes);
            NavGrid nav = TerrainDerivation.Derive(map, pack).Nav;
            MapDesc desc = MapDescJson.Read(bytes);
            MapPreviewData d = Build(bytes);
            int steep = 0, walls = 0;
            for (int z = 0; z < d.Depth; z++)
                for (int x = 0; x < d.Width; x++)
                {
                    bool underWall = desc.Walls.Any(w => x >= w.Cell.X && x < w.Cell.X + w.Size.X && z >= w.Cell.Z && z < w.Cell.Z + w.Size.Z);
                    bool walkable = nav.IsPassable(x, z);
                    if (underWall) { walls++; continue; }
                    if (d.Steep[z * d.Width + x]) steep++;
                    Assert.That(d.Steep[z * d.Width + x], Is.EqualTo(!walkable), name + " (" + x + ", " + z + ")");
                }
            Assert.That(steep, Is.GreaterThan(0), name);
            Assert.That(steep, Is.LessThan(d.Width * d.Depth - walls), name);
        }

        [Test]
        public void GroundColourIsThePalettesColourOfTheCellsSurfaceName()
        {
            byte[] bytes = Ed4Fixtures.MapBytes("terrain-demo");
            MapDesc desc = MapDescJson.Read(bytes);
            MapPalette palette = Ed4Fixtures.PaletteOf(bytes);
            MapPreviewData d = Build(bytes);
            var seen = new HashSet<int>();
            for (int z = 0; z < desc.Size.Z; z++)
                for (int x = 0; x < desc.Size.X; x++)
                {
                    char c = desc.Surfaces.Rows[z][x];
                    int index = c >= 'A' && c <= 'Z' ? c - 'A' : c >= 'a' && c <= 'z' ? c - 'a' + 26 : c >= '0' && c <= '9' ? c - '0' + 52 : c == '+' ? 62 : 63;
                    int expected = palette.GroundColor(desc.Surfaces.Names[index], out _);
                    Assert.That(d.Ground[z * desc.Size.X + x], Is.EqualTo(expected), "(" + x + ", " + z + ")");
                    seen.Add(expected);
                }
            Assert.That(seen.Count, Is.EqualTo(3), "terrain-demo shows grass, mud and road");
        }

        [Test]
        public void AMapWithoutASurfaceTableUsesTheNoGroundColour()
        {
            byte[] bytes = Ed4Fixtures.MapBytes("m6-demo");
            MapPalette palette = Ed4Fixtures.PaletteOf(bytes);
            Assert.That(Build(bytes).Ground.Distinct(), Is.EqualTo(new[] { palette.NoGround }));
        }

        [Test]
        public void ASurfaceNameTheGroundSectionLacksGetsOneNoteAndOneThatIsListedGetsNone()
        {
            string dir = Ed4Fixtures.Cartridge(dirs, "Half", "{ \"ground\": { \"grass\": [\"#112233\"] } }");
            byte[] bytes = Ed4Fixtures.MapBytes("terrain-demo");
            MapPalette half = MapPalette.ForLayer(new ContentCatalog(new[] { dir }), "half1");
            MapMessages table = Ed4Fixtures.Messages();
            MapPreviewData d = Build(bytes, half);
            Assert.That(d.Notes.Select(n => n.Name), Is.EqualTo(new[] { "mud", "road" }));
            foreach (MapPreviewNote note in d.Notes)
            {
                Assert.That(note.Key, Is.EqualTo("MAPPREVIEW_GROUND_FALLBACK"));
                Assert.That(note.Text, Is.EqualTo(table.Text("MAPPREVIEW_GROUND_FALLBACK")));
            }
            Assert.That(d.Problems.Select(p => p.Code + p.Id + p.Text), Is.EqualTo(Build(bytes).Problems.Select(p => p.Code + p.Id + p.Text)), "notes are not problems");
            MapDesc desc = MapDescJson.Read(bytes);
            Assert.That(d.Ground[0], Is.EqualTo(half.GroundColor(desc.Surfaces.Names[desc.Surfaces.Rows[0][0] - 'A'], out _)));
            Assert.That(Build(bytes, MapPalette.Default).Notes.Select(n => n.Name), Is.EqualTo(new[] { "grass", "mud", "road" }));
        }

        [Test]
        public void ThePointsOfLanesPlaytestGetNeutralAndTheTwoTeamColoursByOwner()
        {
            byte[] bytes = Ed4Fixtures.MapBytes("lanes-playtest");
            MapDesc desc = MapDescJson.Read(bytes);
            MapPalette palette = Ed4Fixtures.PaletteOf(bytes);
            MapPreviewData d = Build(bytes);
            var owners = new HashSet<int>();
            for (int i = 0; i < desc.Points.Count; i++)
            {
                MapPoint p = desc.Points[i];
                int expected = p.InitialOwner == 0 ? palette.Neutral : palette.Team[p.InitialOwner - 1];
                Assert.That(d.Points[i].Owner, Is.EqualTo(p.InitialOwner));
                Assert.That(d.Points[i].Color, Is.EqualTo(expected), "point " + p.Id);
                Assert.That((d.Points[i].Id, d.Points[i].XMm, d.Points[i].ZMm, d.Points[i].RadiusMm), Is.EqualTo((p.Id, p.At.X, p.At.Z, (long)p.RadiusMm)));
                owners.Add(p.InitialOwner);
            }
            Assert.That(owners, Is.EquivalentTo(new[] { 0, 1, 2 }));
            Assert.That(palette.Neutral, Is.Not.EqualTo(palette.Team[0]));
            Assert.That(palette.Team[0], Is.Not.EqualTo(palette.Team[1]));
        }

        [Test]
        public void BasesAndUnitsKeepTheirPlacesAndTakeTheirTeamColour()
        {
            byte[] bytes = Ed4Fixtures.MapBytes("lanes-playtest");
            MapDesc desc = MapDescJson.Read(bytes);
            MapPalette palette = Ed4Fixtures.PaletteOf(bytes);
            MapPreviewData d = Build(bytes);
            for (int i = 0; i < desc.Bases.Count; i++)
            {
                MapBase b = desc.Bases[i];
                Assert.That((d.Bases[i].Id, d.Bases[i].Team, d.Bases[i].XMm, d.Bases[i].ZMm), Is.EqualTo((b.Id, b.Team, b.At.X, b.At.Z)));
                Assert.That(d.Bases[i].Color, Is.EqualTo(palette.Team[b.Team - 1]));
            }
            for (int i = 0; i < desc.Units.Count; i++)
            {
                MapUnit u = desc.Units[i];
                MapPreviewUnit v = d.Units[i];
                Assert.That((v.Key, v.Type, v.Team, v.Squad, v.XMm, v.ZMm, v.Reserve), Is.EqualTo((u.Key, u.Type, u.Team, u.Squad, u.At.X, u.At.Z, u.Reserve)));
                Assert.That(v.Color, Is.EqualTo(palette.Team[u.Team - 1]));
            }
            Assert.That(d.Units.Any(u => u.Reserve) && d.Units.Any(u => !u.Reserve), Is.True);
        }

        [Test]
        public void WallsAndPlatesCarryTheirShapeAndTheStructureColours()
        {
            byte[] bytes = Ed4Fixtures.MapBytes("plate-demo");
            MapDesc desc = MapDescJson.Read(bytes);
            MapPalette palette = Ed4Fixtures.PaletteOf(bytes);
            MapPreviewData d = Build(bytes);
            for (int i = 0; i < desc.Walls.Count; i++)
            {
                MapWall w = desc.Walls[i];
                MapPreviewWall v = d.Walls[i];
                Assert.That((v.Id, v.CellX, v.CellZ, v.SizeX, v.SizeZ, v.HeightRaw, v.Raised),
                    Is.EqualTo((w.Id, w.Cell.X, w.Cell.Z, w.Size.X, w.Size.Z, w.HeightRaw, w.BaseRaw.HasValue)));
                Assert.That((v.SideColor, v.TopColor), Is.EqualTo((palette.StructSide, palette.StructTop)));
            }
            MapPlate plate = desc.Plates.First(p => p.Slope != null);
            MapPreviewPlate pp = d.Plates[desc.Plates.ToList().IndexOf(plate)];
            Assert.That((pp.Id, pp.CellX, pp.CellZ, pp.SizeX, pp.SizeZ, pp.Axis, pp.FromRaw, pp.ToRaw, pp.ThickRaw),
                Is.EqualTo((plate.Id, plate.Cell.X, plate.Cell.Z, plate.Size.X, plate.Size.Z, plate.Slope.Axis, plate.Slope.FromRaw, plate.Slope.ToRaw, plate.ThickRaw)));
            MapPlate flat = desc.Plates.First(p => p.YRaw.HasValue);
            MapPreviewPlate pf = d.Plates[desc.Plates.ToList().IndexOf(flat)];
            Assert.That((pf.Axis, pf.FromRaw, pf.ToRaw), Is.EqualTo(("", flat.YRaw.Value, flat.YRaw.Value)));
        }

        [Test]
        public void ARaisedWallSaysSoAndKeepsItsBottom()
        {
            var map = new E2Map();
            map.Walls.Add(new MapWall(1, "wall", "walls", new MapXZ(3, 3), new MapXZ(2, 1), 131072, baseRaw: 65536));
            MapPreviewData d = MapPreview.Build(map.Bytes(), E2Fixtures.M6Pack(), MapPalette.Default, Ed4Fixtures.Messages());
            Assert.That(d.Walls.Single().Raised, Is.True);
            Assert.That(d.Walls.Single().BaseRaw, Is.EqualTo(65536));
            Assert.That(Build(Ed4Fixtures.MapBytes("m6-demo")).Walls.All(w => !w.Raised), Is.True);
        }

        [Test]
        public void TheMapsOwnPacksPaletteIsUsedForTeamColoursWhenTheMapNamesFewTeams()
        {
            string dir = Ed4Fixtures.Cartridge(dirs, "Odd", "{ \"team\": [\"#0000AA\", \"#00BB00\"], \"neutral\": \"#CCCCCC\" }");
            MapPalette odd = MapPalette.ForLayer(new ContentCatalog(new[] { dir }), "odd1");
            MapPreviewData d = Build(Ed4Fixtures.MapBytes("lanes-playtest"), odd);
            Assert.That(d.Bases.Select(b => Ed4Fixtures.Hex(b.Color)), Is.EqualTo(new[] { "0000AA", "00BB00" }));
            Assert.That(d.Points.Select(p => Ed4Fixtures.Hex(p.Color)).Distinct().OrderBy(s => s, StringComparer.Ordinal),
                Is.EqualTo(new[] { "0000AA", "00BB00", "CCCCCC" }));
        }

        // The 64 characters of the surface rows in table order: ground colour follows the base64 value of the character.
        [Test]
        public void EveryBase64CharacterOfASurfaceRowPicksItsOwnName()
        {
            const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
            var names = Enumerable.Range(0, 64).Select(i => "s" + i.ToString("D2")).ToArray();
            var map = new E2Map { Width = 64, Depth = 1, Surfaces = new MapSurfaces(names, new[] { alphabet }) };
            MapPreviewData d = MapPreview.Build(map.Bytes(), E2Fixtures.M6Pack(), MapPalette.Default, Ed4Fixtures.Messages());
            for (int i = 0; i < 64; i++) Assert.That(d.Ground[i], Is.EqualTo(MapPalette.FallbackColor(names[i])), alphabet[i].ToString());
            Assert.That(d.Notes.Select(n => n.Name), Is.EqualTo(names));
        }

        private static long N(JsonNode node, string key) => long.Parse(node.Find(key).Text);

        [TestCase("plate-demo")]
        [TestCase("lanes-playtest")]
        [TestCase("terrain-demo")]
        public void TheJsonCarriesEveryValueOfTheDataInAFixedOrder(string name)
        {
            MapPreviewData d = Build(Ed4Fixtures.MapBytes(name));
            JsonNode root = ContentJson.Parse(MapPreview.ToJson(d), name);
            Assert.That(root.Members.Select(m => m.Name), Is.EqualTo(new[]
                { "id", "pack", "width", "depth", "originX", "originZ", "shade", "steep", "groundPalette", "ground", "walls", "plates", "points", "bases", "units", "pins", "problems", "notes" }));
            Assert.That(root.Find("id").Text, Is.EqualTo(d.Id));
            Assert.That(root.Find("pack").Text, Is.EqualTo(d.Pack));
            Assert.That((N(root, "width"), N(root, "depth"), N(root, "originX"), N(root, "originZ")), Is.EqualTo(((long)d.Width, (long)d.Depth, (long)d.OriginX, (long)d.OriginZ)));
            Assert.That(root.Find("shade").Items.Select(i => int.Parse(i.Text)), Is.EqualTo(d.Shade));
            Assert.That(root.Find("steep").Items.Select(i => int.Parse(i.Text)), Is.EqualTo(Enumerable.Range(0, d.Steep.Count).Where(i => d.Steep[i])));
            string[] colours = root.Find("groundPalette").Items.Select(i => i.Text).ToArray();
            Assert.That(root.Find("ground").Items.Select(i => Convert.ToInt32(colours[int.Parse(i.Text)].Substring(1), 16)), Is.EqualTo(d.Ground));
            Assert.That(colours.Distinct().Count(), Is.EqualTo(colours.Length));
            JsonNode[] walls = root.Find("walls").Items.ToArray();
            for (int i = 0; i < walls.Length; i++)
            {
                MapPreviewWall w = d.Walls[i];
                Assert.That(new[] { N(walls[i], "id"), N(walls[i], "x"), N(walls[i], "z"), N(walls[i], "w"), N(walls[i], "d"), N(walls[i], "h"), N(walls[i], "base") },
                    Is.EqualTo(new[] { (long)w.Id, w.CellX, w.CellZ, w.SizeX, w.SizeZ, w.HeightRaw, w.BaseRaw }));
                Assert.That(walls[i].Find("raised").Flag, Is.EqualTo(w.Raised));
                Assert.That((walls[i].Find("side").Text, walls[i].Find("top").Text), Is.EqualTo(("#" + Ed4Fixtures.Hex(w.SideColor), "#" + Ed4Fixtures.Hex(w.TopColor))));
            }
            JsonNode[] plates = root.Find("plates").Items.ToArray();
            for (int i = 0; i < plates.Length; i++)
            {
                MapPreviewPlate p = d.Plates[i];
                Assert.That(new[] { N(plates[i], "id"), N(plates[i], "x"), N(plates[i], "z"), N(plates[i], "w"), N(plates[i], "d"), N(plates[i], "from"), N(plates[i], "to"), N(plates[i], "thick") },
                    Is.EqualTo(new[] { (long)p.Id, p.CellX, p.CellZ, p.SizeX, p.SizeZ, p.FromRaw, p.ToRaw, p.ThickRaw }));
                Assert.That(plates[i].Find("axis").Text, Is.EqualTo(p.Axis));
            }
            JsonNode[] points = root.Find("points").Items.ToArray();
            for (int i = 0; i < points.Length; i++)
            {
                MapPreviewPoint p = d.Points[i];
                Assert.That(new[] { N(points[i], "id"), N(points[i], "x"), N(points[i], "z"), N(points[i], "r"), N(points[i], "owner") },
                    Is.EqualTo(new[] { p.Id, p.XMm, p.ZMm, p.RadiusMm, p.Owner }));
                Assert.That(points[i].Find("color").Text, Is.EqualTo("#" + Ed4Fixtures.Hex(p.Color)));
            }
            JsonNode[] bases = root.Find("bases").Items.ToArray();
            for (int i = 0; i < bases.Length; i++)
            {
                MapPreviewBase b = d.Bases[i];
                Assert.That(new[] { N(bases[i], "id"), N(bases[i], "team"), N(bases[i], "x"), N(bases[i], "z") }, Is.EqualTo(new[] { b.Id, b.Team, b.XMm, b.ZMm }));
                Assert.That(bases[i].Find("color").Text, Is.EqualTo("#" + Ed4Fixtures.Hex(b.Color)));
            }
            JsonNode[] units = root.Find("units").Items.ToArray();
            for (int i = 0; i < units.Length; i++)
            {
                MapPreviewUnit u = d.Units[i];
                Assert.That(new[] { N(units[i], "key"), N(units[i], "team"), N(units[i], "squad"), N(units[i], "x"), N(units[i], "z") },
                    Is.EqualTo(new[] { (long)u.Key, u.Team, u.Squad, u.XMm, u.ZMm }));
                Assert.That(units[i].Find("type").Text, Is.EqualTo(u.Type));
                Assert.That(units[i].Find("reserve").Flag, Is.EqualTo(u.Reserve));
                Assert.That(units[i].Find("color").Text, Is.EqualTo("#" + Ed4Fixtures.Hex(u.Color)));
            }
        }

        [Test]
        public void TheJsonOfAHandMapListsItsSteepCellsByIndexAndItsPinsAndNotes()
        {
            MapPreviewData d = MapPreview.Build(HandHeights().Bytes(), E2Fixtures.TerrainPack(), MapPalette.Default, Ed4Fixtures.Messages());
            JsonNode root = ContentJson.Parse(MapPreview.ToJson(d), "hand");
            Assert.That(root.Find("steep").Items.Select(i => i.Text), Is.EqualTo(new[] { "0", "1", "3", "4", "5" }));
            Assert.That(root.Find("shade").Items.Select(i => i.Text), Is.EqualTo(HandShade.Select(i => i.ToString())));
            Assert.That(root.Find("groundPalette").Items.Single().Text, Is.EqualTo("#" + Ed4Fixtures.Hex(MapPalette.Default.NoGround)));
        }
    }
}
