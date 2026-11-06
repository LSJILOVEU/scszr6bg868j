using System.IO;
using System.Text;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Content;
using WarCore.Content.MapTools;
using WarCore.Content.Rwr;
using WarCore.Presentation;
using static WarCore.Tests.E1aSamples;

namespace WarCore.Tests
{
    // Acceptance 1 (the two repository descriptions bake to the existing maps and match data) and 3 (terrain combinations).
    public sealed class E1aFixtureTests
    {
        [Test]
        public void TerrainDescriptionBakesToTheTerrainDemoMapBytes()
        {
            MapBakeResult result = Bake(File.ReadAllBytes(TerrainPath));
            byte[] expected = WcMapWriter.Write(TerrainDemoMap.Build());
            Assert.That(result.WcMapBytes, Is.EqualTo(expected));
            Assert.That(result.WcMapBytes.Length, Is.EqualTo(E1aRecordTests.TerrainBytesLength));
            Assert.That(E1aRecordTests.Sha(result.WcMapBytes), Is.EqualTo(E1aRecordTests.TerrainBytesSha));
            Assert.That(result.PackId, Is.EqualTo(TerrainDemoMap.PackId));
        }

        [Test]
        public void M6DescriptionBakesToTheM6DemoMapBytes()
        {
            MapBakeResult result = Bake(File.ReadAllBytes(M6Path));
            Assert.That(result.WcMapBytes, Is.EqualTo(M6DemoMap.Bytes()));
            Assert.That(result.WcMapBytes.Length, Is.EqualTo(E1aRecordTests.M6BytesLength));
            Assert.That(E1aRecordTests.Sha(result.WcMapBytes), Is.EqualTo(E1aRecordTests.M6BytesSha));
            Assert.That(result.PackId, Is.EqualTo(RwrPack.M6Layer));
            Assert.That(result.Setup.Seed, Is.EqualTo(41UL));
            Assert.That(result.Setup.CrowdPass, Is.EqualTo("teamIdle"));
        }

        [Test]
        public void TerrainSetupStandsControlPointsOnTheGround()
        {
            MapSetup setup = Bake(File.ReadAllBytes(TerrainPath)).Setup;
            ControlPointDef[] points = setup.ControlPoints;
            Assert.That(points.Length, Is.EqualTo(2));
            Assert.That(points[0].Center.Y.Raw, Is.EqualTo(376832L));
            Assert.That(points[1].Center.Y.Raw, Is.EqualTo(0L));
            for (int i = 0; i < 2; i++)
            {
                Assert.That(points[i].Center, Is.EqualTo(TerrainDemoMap.ControlPoints[i]));
                Assert.That(points[i].Id, Is.EqualTo(i + 1));
                Assert.That(points[i].Radius.Raw, Is.EqualTo(6L * 65536));
                Assert.That(points[i].HasSpawn, Is.False);
            }
            SpawnAnchor[] bases = setup.Bases;
            Assert.That(bases.Length, Is.EqualTo(2));
            for (int i = 0; i < 2; i++)
            {
                Assert.That(bases[i].Team, Is.EqualTo(i + 1));
                Assert.That(bases[i].Point, Is.EqualTo(TerrainDemoMap.Spawns[i]));
            }
            Assert.That(setup.Soldiers.Count, Is.EqualTo(0));
        }

        [Test]
        public void M6SetupBuildsTheRecordedMatchPolicy()
        {
            MapSetup setup = Bake(File.ReadAllBytes(M6Path)).Setup;
            MatchPack match = RwrPack.Load(RwrPack.M6Layer).BuildMatch(setup.ControlPoints, setup.Bases);
            Assert.That(match.PolicyDigest, Is.EqualTo(E1aRecordTests.M6PolicyDigest));
            Assert.That(match.PolicyDigest, Is.EqualTo(RwrMatch.CreateM6().PolicyDigest));
        }

        [Test]
        public void M6SetupSoldiersMatchTheDemoRecipe()
        {
            MapSetup setup = Bake(File.ReadAllBytes(M6Path)).Setup;
            ContentPack pack = RwrPack.Load(RwrPack.M6Layer);
            var expected = DemoRecipes.M6().Units;
            Assert.That(setup.Soldiers.Count, Is.EqualTo(expected.Length));
            var text = new StringBuilder();
            int deployed = 0;
            for (int i = 0; i < expected.Length; i++)
            {
                MapSoldier s = setup.Soldiers[i];
                UnitSpawn u = expected[i];
                Assert.That(s.Key, Is.EqualTo(u.Key));
                Assert.That(s.XMm, Is.EqualTo(u.XMm));
                Assert.That(s.ZMm, Is.EqualTo(u.ZMm));
                Assert.That(s.Team, Is.EqualTo(u.Team));
                Assert.That(s.Squad, Is.EqualTo(u.Squad));
                Assert.That(s.Reserve, Is.EqualTo(u.Reserve));
                Assert.That(s.Type, Is.EqualTo(s.Team == 1 ? RwrPack.SoldierTeam1 : RwrPack.SoldierTeam2));
                int weapon = pack.WeaponOfUnitType(s.Type);
                Assert.That(weapon, Is.EqualTo(u.Weapon));
                if (!s.Reserve) deployed++;
                text.Append(E1aRecordTests.UnitLine(s.Key, s.XMm, s.ZMm, s.Team, s.Squad, s.Reserve, weapon));
            }
            Assert.That(deployed, Is.EqualTo(20));
            Assert.That(E1aRecordTests.Sha(Encoding.UTF8.GetBytes(text.ToString())), Is.EqualTo(E1aRecordTests.M6UnitsSha));
        }

        // Acceptance 3: all four (height field, surface table) combinations on a 4 x 3 map read back as described.
        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void TerrainCombinationsReadBackAsDescribed(bool withHeights, bool withSurfaces)
        {
            const int width = 4, depth = 3;
            int[][] heights = null;
            if (withHeights)
            {
                heights = new int[depth + 1][];
                for (int j = 0; j <= depth; j++)
                {
                    heights[j] = new int[width + 1];
                    for (int i = 0; i <= width; i++) heights[j][i] = (i * 3 + j * 5) % 7 * 8192;
                }
            }
            string[] rows = { "ABCA", "CBAB", "AACC" };
            MapSurfaces surfaces = withSurfaces ? new MapSurfaces(new[] { "dirt", "grass", "sand" }, rows) : null;
            var desc = new MapDesc("combo", "rwr", "rwr", new MapXZ(width, depth), new MapXZ(-2, 3), 0, 262144,
                new[] { new MapLayer("walls", 1) }, heights, surfaces,
                new[] { new MapWall(1, "wall", "walls", new MapXZ(1, 1), new MapXZ(2, 1), 196608) },
                new MapPoint[0], new MapBase[0], new MapUnit[0], new MapRun(1, "none"), new[] { "hand" }, null, null,
                new MapStamp[0], new MapSrc[0], new MapNext(2, 1, 1, 1));
            WcMap map = WcMapReader.Read(Bake(MapDescJson.Write(desc)).WcMapBytes);
            Assert.That(map.HasHeightField, Is.EqualTo(withHeights));
            Assert.That(map.HasSurfaceTable, Is.EqualTo(withSurfaces));
            Assert.That(map.Width, Is.EqualTo((uint)width));
            Assert.That(map.Depth, Is.EqualTo((uint)depth));
            if (withHeights)
                for (int j = 0; j <= depth; j++)
                    for (int i = 0; i <= width; i++) Assert.That(map.VertexRaw(i, j), Is.EqualTo(heights[j][i]));
            if (withSurfaces)
            {
                Assert.That(map.SurfaceNames, Is.EqualTo(new[] { "dirt", "grass", "sand" }));
                for (int z = 0; z < depth; z++)
                    for (int x = 0; x < width; x++)
                        Assert.That(map.SurfaceNames[map.SurfaceCells[z * width + x]], Is.EqualTo(surfaces.Names[rows[z][x] - 'A']));
            }
        }
    }
}
