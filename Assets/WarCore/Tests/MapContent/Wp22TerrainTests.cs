using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class Wp22TerrainTests
    {
        private const long S = SimFix.Scale;

        private static ContentPack Pack(string json = TerrainDemoMap.PackJson)
        {
            string id = "rwr-terrain-demo";
            return ContentLoader.Load(new MemoryContentSource(new Dictionary<string, string> { [id] = json }, FileContentSource.Rwr()), id);
        }

        private static string Variant(string from, string to)
        {
            Assert.That(TerrainDemoMap.PackJson, Does.Contain(from), "test text drifted");
            return TerrainDemoMap.PackJson.Replace(from, to);
        }

        private static TerrainTables Derive(WcMap map, string json = TerrainDemoMap.PackJson) => TerrainDerivation.Derive(map, Pack(json));

        private static bool Walkable(TerrainTables t, int x, int z) => t.Nav.IsPassable(x, z);

        // ---- content rules ----

        [Test]
        public void Rules_AreParsedWithDefaultsAndNormalised()
        {
            ContentPack pack = Pack();
            TerrainRules rules = pack.Terrain;
            Assert.That(rules.MaxWalkableNum, Is.EqualTo(3)); Assert.That(rules.MaxWalkableDen, Is.EqualTo(4));
            Assert.That(rules.SurfaceCount, Is.EqualTo(3));
            Assert.That(rules.TryFind("grass", out SurfaceDef grass), Is.True);
            Assert.That(new[] { grass.Passable ? 1 : 0, grass.SpeedMilli, grass.Cost }, Is.EqualTo(new[] { 1, 1000, 1 }));
            rules.TryFind("mud", out SurfaceDef mud); Assert.That(new[] { mud.SpeedMilli, mud.Cost }, Is.EqualTo(new[] { 500, 3 }));
            rules.TryFind("road", out SurfaceDef road); Assert.That(new[] { road.SpeedMilli, road.Cost }, Is.EqualTo(new[] { 1250, 1 }));
        }

        [Test]
        public void Rules_BlockedSurfaceIsNormalisedToZeroAndExplicitValuesAreHonoured()
        {
            ContentPack pack = Pack(Variant("{ \"id\": \"mud\", \"speedMilli\": 500, \"cost\": 3 }", "{ \"id\": \"mud\", \"passable\": false, \"speedMilli\": 777, \"cost\": 9 }"));
            pack.Terrain.TryFind("mud", out SurfaceDef mud);
            Assert.That(mud.Passable, Is.False); Assert.That(mud.SpeedMilli, Is.Zero); Assert.That(mud.Cost, Is.Zero);
            // Explicit values are values, not "missing": speedMilli 1000 and cost 1 spelled out equal the defaults.
            ContentPack same = Pack(Variant("{ \"id\": \"grass\" }", "{ \"id\": \"grass\", \"passable\": true, \"speedMilli\": 1000, \"cost\": 1 }"));
            Assert.That(Derive(TerrainDemoMap.Build()).Digest, Is.EqualTo(TerrainDerivation.Derive(TerrainDemoMap.Build(), same).Digest));
        }

        [Test]
        public void Rules_AbsentSectionLeavesPacksUntouched()
        {
            Assert.That(ContentLoader.Load(FileContentSource.Rwr(), "rwr").Terrain, Is.Null);
            Assert.That(ContentLoader.Load(FileContentSource.Rwr(), "rwr-melee").Terrain, Is.Null);
        }

        [TestCase("\"speedMilli\": 500", "\"speedMilli\": 0", "speedMilli")]
        [TestCase("\"speedMilli\": 500", "\"speedMilli\": 2001", "speedMilli")]
        [TestCase("\"cost\": 3", "\"cost\": 0", "cost")]
        [TestCase("\"cost\": 3", "\"cost\": 256", "cost")]
        [TestCase("\"maxWalkableNum\": 3", "\"maxWalkableNum\": 9", "maxWalkableNum")]
        [TestCase("\"maxWalkableDen\": 4", "\"maxWalkableDen\": 4097", "maxWalkableDen")]
        [TestCase("\"cost\": 3", "\"costs\": 3", "costs")]
        public void Rules_BadValuesNamePackEntryAndField(string from, string to, string field)
        {
            string message = Assert.Throws<ContentException>(() => Pack(Variant(from, to))).Message;
            Assert.That(message, Does.Contain("rwr-terrain-demo").And.Contain(field));
        }

        [Test]
        public void Rules_DuplicateSurfaceIdIsRejected()
        {
            Assert.Throws<ContentException>(() => Pack(Variant("{ \"id\": \"grass\" }", "{ \"id\": \"mud\" }")));
        }

        // ---- derivation ----

        // Naive reference: wall bit, surface, and slope via the cross-product oracle, cell by cell.
        private static void AssertMatchesReference(WcMap map, TerrainTables t, int num, int den, Func<int, SurfaceDef> surface)
        {
            int w = (int)map.Width, d = (int)map.Depth;
            for (int z = 0; z < d; z++)
                for (int x = 0; x < w; x++)
                {
                    int key = z * w + x;
                    SurfaceDef def = surface(map.SurfaceCells[key]);
                    int Hp(int i, int j) => map.HeightPoints[j * (w + 1) + i];
                    bool expected = map.IsPassable(x, z) && def.Passable
                        && Wp22Oracle.CellWithin(Hp(x, z), Hp(x + 1, z), Hp(x, z + 1), Hp(x + 1, z + 1), num, den);
                    Assert.That(t.Nav.IsPassable(x, z), Is.EqualTo(expected), "cell " + x + "," + z);
                    Assert.That((int)t.CostAt(key), Is.EqualTo(expected ? def.Cost : 0), "cost " + x + "," + z);
                    Assert.That((int)t.SpeedMilliAt(key), Is.EqualTo(expected ? def.SpeedMilli : 0), "speed " + x + "," + z);
                }
        }

        [Test]
        public void Demo_DerivationMatchesTheNaiveReference_AndKnownCells()
        {
            WcMap map = TerrainDemoMap.Build();
            ContentPack pack = Pack();
            TerrainTables t = TerrainDerivation.Derive(map, pack);
            AssertMatchesReference(map, t, 3, 4, i => { pack.Terrain.TryFind(map.SurfaceNames[i], out SurfaceDef s); return s; });
            Assert.That(Walkable(t, 6, 10), Is.False, "wall");
            Assert.That(Walkable(t, 20, 22), Is.False, "cliff band");
            Assert.That(Walkable(t, 13, 25), Is.False, "plateau side");
            Assert.That(Walkable(t, 20, 25), Is.True, "plateau top is walkable but cut off");
            Assert.That(Walkable(t, 15, 16), Is.True, "ramp at 0.5");
            Assert.That(t.SpeedMilliAt(16 * 48 + 30), Is.EqualTo(1250)); Assert.That(t.CostAt(16 * 48 + 30), Is.EqualTo(1));
            Assert.That(t.SpeedMilliAt(8 * 48 + 38), Is.EqualTo(500)); Assert.That(t.CostAt(8 * 48 + 38), Is.EqualTo(3));
            Assert.That(t.SpeedMilliAt(0), Is.EqualTo(1000));
            Assert.That(t.Ground, Is.Not.Null);
            Assert.That(t.SurfaceName(1), Is.EqualTo("mud"));
        }

        private static void AssertOnlyChanged(WcMap map, TerrainTables a, TerrainTables b, Func<int, bool> changedCell, bool bits, bool cost, bool speed)
        {
            for (int key = 0; key < map.Width * map.Depth; key++)
            {
                int x = key % 48, z = key / 48;
                bool c = changedCell(key);
                Assert.That(a.Nav.IsPassable(x, z) != b.Nav.IsPassable(x, z), Is.EqualTo(c && bits), "bit " + x + "," + z);
                Assert.That(a.CostAt(key) != b.CostAt(key), Is.EqualTo(c && cost), "cost " + x + "," + z);
                Assert.That(a.SpeedMilliAt(key) != b.SpeedMilliAt(key), Is.EqualTo(c && speed), "speed " + x + "," + z);
            }
            Assert.That(b.Digest, Is.Not.EqualTo(a.Digest));
        }

        [Test]
        public void SingleFactor_EachRuleChangesOnlyItsOwnCells()
        {
            WcMap map = TerrainDemoMap.Build();
            TerrainTables baseline = Derive(map);
            bool Mud(int key) => map.SurfaceCells[key] == 1 && map.IsPassable(key % 48, key / 48);
            Func<int, bool> mudOpen = key => Mud(key) && baseline.Nav.IsPassable(key % 48, key / 48);
            AssertOnlyChanged(map, baseline, Derive(map, Variant("\"speedMilli\": 500", "\"speedMilli\": 600")), mudOpen, false, false, true);
            AssertOnlyChanged(map, baseline, Derive(map, Variant("\"cost\": 3", "\"cost\": 4")), mudOpen, false, true, false);
            AssertOnlyChanged(map, baseline, Derive(map, Variant("{ \"id\": \"mud\", \"speedMilli\": 500, \"cost\": 3 }", "{ \"id\": \"mud\", \"passable\": false }")), mudOpen, true, true, true);
            // Slope 1/2 blocks nothing more than 3/4 does on this map (no cell lies between), but the digest still moves.
            TerrainTables half = Derive(map, Variant("\"maxWalkableNum\": 3", "\"maxWalkableNum\": 1").Replace("\"maxWalkableDen\": 4", "\"maxWalkableDen\": 2"));
            AssertOnlyChanged(map, baseline, half, key => false, false, false, false);
            // Slope 2/5 blocks every ramp cell; compare against the reference, not a hand list.
            ContentPack steep = Pack(Variant("\"maxWalkableNum\": 3", "\"maxWalkableNum\": 2").Replace("\"maxWalkableDen\": 4", "\"maxWalkableDen\": 5"));
            AssertMatchesReference(map, TerrainDerivation.Derive(map, steep), 2, 5, i => { steep.Terrain.TryFind(map.SurfaceNames[i], out SurfaceDef s); return s; });
            Assert.That(TerrainDerivation.Derive(map, steep).Digest, Is.Not.EqualTo(baseline.Digest));
            Assert.That(Derive(map).Digest, Is.EqualTo(baseline.Digest));
        }

        [Test]
        public void Digest_CoversHeightsAndSurfaceCells()
        {
            WcMap map = TerrainDemoMap.Build();
            ulong baseline = Derive(map).Digest;
            int[] points = map.HeightPoints.ToArray(); points[5] += 1;
            WcMap bumped = new WcMap(map.MapId, map.ContentId, map.Min, map.Max, map.ChunkSize, map.FlatHeight, map.Width, map.Depth,
                map.Layers.ToArray(), map.Walls.ToArray(), map.Passability.ToArray(), points, map.SurfaceNames.ToArray(), map.SurfaceCells.ToArray());
            Assert.That(Derive(bumped).Digest, Is.Not.EqualTo(baseline));
            // Same names, indices swapped between mud and road: the table was "reordered without remapping".
            byte[] cells = map.SurfaceCells.Select(c => c == 1 ? (byte)2 : c == 2 ? (byte)1 : c).ToArray();
            WcMap swapped = new WcMap(map.MapId, map.ContentId, map.Min, map.Max, map.ChunkSize, map.FlatHeight, map.Width, map.Depth,
                map.Layers.ToArray(), map.Walls.ToArray(), map.Passability.ToArray(), map.HeightPoints.ToArray(), map.SurfaceNames.ToArray(), cells);
            Assert.That(Derive(swapped).Digest, Is.Not.EqualTo(baseline));
        }

        [Test]
        public void TerrainTables_DigestCoversEveryCostAndSpeedValue()
        {
            var nav = new NavGrid(2, 1, SimFix.FromInt(0), SimFix.FromInt(0), new byte[] { 3 });
            TerrainTables Make(byte c0, ushort s0, byte c1 = 1, ushort s1 = 1000)
                => new TerrainTables(null, nav, new[] { c0, c1 }, new[] { s0, s1 }, new string[0], new byte[0], new byte[0]);
            ulong baseline = Make(1, 1000).Digest;
            Assert.That(Make(2, 1000).Digest, Is.Not.EqualTo(baseline));
            Assert.That(Make(1, 999).Digest, Is.Not.EqualTo(baseline));
            Assert.That(Make(1, 1000, 2).Digest, Is.Not.EqualTo(baseline));
            Assert.That(Make(1, 1000, 1, 999).Digest, Is.Not.EqualTo(baseline));
            Assert.That(Make(1, 1000).Digest, Is.EqualTo(baseline));
            Assert.Throws<ArgumentException>(() => Make(0, 1000));        // zero cost on a walkable cell
        }

        [Test]
        public void DanglingReferences_NamePackEntryAndField()
        {
            WcMap map = TerrainDemoMap.Build();
            string dangling = Assert.Throws<ContentException>(() => Derive(map, Variant(", { \"id\": \"road\", \"speedMilli\": 1250 }", ""))).Message;
            Assert.That(dangling, Does.Contain("rwr-terrain-demo").And.Contain("surfaces").And.Contain("road"));
            string none = Assert.Throws<ContentException>(() => TerrainDerivation.Derive(map, ContentLoader.Load(FileContentSource.Rwr(), "rwr"))).Message;
            Assert.That(none, Does.Contain("'rwr'").And.Contain("no terrain section"));
        }

        [Test]
        public void MapWithoutTerrainData_DerivesDefaults()
        {
            WcMap flat = M2MapFixture.Model();
            TerrainTables t = TerrainDerivation.Derive(flat, null, "none");
            Assert.That(t.Ground, Is.Null); Assert.That(t.SurfaceCount, Is.Zero);
            for (int z = 0; z < flat.Depth; z++)
                for (int x = 0; x < flat.Width; x++)
                {
                    Assert.That(t.Nav.IsPassable(x, z), Is.EqualTo(flat.IsPassable(x, z)));
                    Assert.That((int)t.SpeedMilliAt(z * 8 + x), Is.EqualTo(flat.IsPassable(x, z) ? 1000 : 0));
                }
        }

        // ---- anchors ----

        private static WcMap WithSurfaces(WcMap map, Func<int, int, byte> cell)
        {
            var cells = new byte[map.Width * map.Depth];
            for (int z = 0; z < map.Depth; z++) for (int x = 0; x < map.Width; x++) cells[z * map.Width + x] = cell(x, z);
            return new WcMap(map.MapId, map.ContentId, map.Min, map.Max, map.ChunkSize, map.FlatHeight, map.Width, map.Depth,
                map.Layers.ToArray(), map.Walls.ToArray(), map.Passability.ToArray(), map.HeightPoints.ToArray(), map.SurfaceNames.ToArray(), cells);
        }

        [Test]
        public void Anchors_DemoSpawnsReachEveryControlPoint()
        {
            Assert.DoesNotThrow(() => TerrainAnchors.Validate(Derive(TerrainDemoMap.Build()), TerrainDemoMap.Spawns, TerrainDemoMap.ControlPoints));
        }

        [Test]
        public void Anchors_AMudBandThatBlocks_CutsTheOnlyRoad()
        {
            WcMap map = TerrainDemoMap.Build();
            // A mud column across the whole depth at x = 41..42, east of both control points.
            WcMap cut = WithSurfaces(map, (x, z) => x >= 41 && x < 43 ? (byte)1 : map.SurfaceCells[z * 48 + x]);
            Assert.DoesNotThrow(() => TerrainAnchors.Validate(Derive(cut), TerrainDemoMap.Spawns, TerrainDemoMap.ControlPoints), "mud is slow, not blocked");
            string message = Assert.Throws<InvalidDataException>(() => TerrainAnchors.Validate(
                Derive(cut, Variant("{ \"id\": \"mud\", \"speedMilli\": 500, \"cost\": 3 }", "{ \"id\": \"mud\", \"passable\": false }")),
                TerrainDemoMap.Spawns, new[] { TerrainDemoMap.ControlPoints[0] })).Message;
            Assert.That(message, Does.StartWith("TERRAIN_UNREACHABLE"));
        }

        [Test]
        public void Anchors_SlopeBlockedAndWallBlockedAnchorsFail()
        {
            WcMap map = TerrainDemoMap.Build();
            TerrainTables steep = Derive(map, Variant("\"maxWalkableNum\": 3", "\"maxWalkableNum\": 2").Replace("\"maxWalkableDen\": 4", "\"maxWalkableDen\": 5"));
            Assert.That(Assert.Throws<InvalidDataException>(() => TerrainAnchors.Validate(steep, TerrainDemoMap.Spawns, TerrainDemoMap.ControlPoints)).Message,
                Does.StartWith("TERRAIN_ANCHOR_BLOCKED"));
            var inWall = new[] { new SimVec3(SimFix.FromRaw(6 * S + S / 2), SimFix.FromRaw(0), SimFix.FromRaw(10 * S + S / 2)) };
            Assert.That(Assert.Throws<InvalidDataException>(() => TerrainAnchors.Validate(Derive(map), inWall, TerrainDemoMap.ControlPoints)).Message,
                Does.StartWith("TERRAIN_ANCHOR_BLOCKED"));
        }

        [Test]
        public void Anchors_AWalkablePlateauCutOffByCliffsIsUnreachable()
        {
            WcMap map = TerrainDemoMap.Build();
            var onPlateau = new[] { new SimVec3(SimFix.FromRaw(20 * S + S / 2), SimFix.FromRaw(0), SimFix.FromRaw(25 * S + S / 2)) };
            TerrainTables t = Derive(map);
            Assert.That(Walkable(t, 20, 25), Is.True);
            Assert.That(Assert.Throws<InvalidDataException>(() => TerrainAnchors.Validate(t, TerrainDemoMap.Spawns, onPlateau)).Message,
                Does.StartWith("TERRAIN_UNREACHABLE"));
        }

        // ---- the runtime refuses terrain maps until T1a-2 ----

        [Test]
        public void WorldBuilder_RefusesTerrainMaps_AndKeepsFlatMapsRunning()
        {
            var recipe = new WorldRecipe(1, MoverKind.InterimPath, new UnitSpawn[0], new BridgeCommand[0]);
            string message = Assert.Throws<NotSupportedException>(() => WorldBuilder.Create(recipe, WcMapWriter.Write(TerrainDemoMap.Build()))).Message;
            Assert.That(message, Does.Contain("T1a-2"));
            Assert.DoesNotThrow(() => WorldBuilder.Create(recipe, M2MapFixture.GoldenBytes));
        }
    }
}
