using System.Linq;
using System.Text;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Content.MapTools;
using static WarCore.Tests.E2Fixtures;
using static WarCore.Tests.E2GateTests;

namespace WarCore.Tests
{
    // Acceptance 2 (one hand-computed fixture per code), 3 (order of several problems) and 4 (agreement with the runtime anchors).
    public sealed class E2FixtureTests
    {
        private static MapProblem[] Check(byte[] bytes, ContentPack pack) => MapGate.Check(bytes, pack).ToArray();

        // Cell (2,7) closed in by four walls (ids 1..4); a base at its centre stands, 0.5 m from every wall, but goes nowhere.
        private static E2Map Enclosure() => new E2Map().Wall(1, 6, 3, 1).Wall(1, 8, 3, 1).Wall(1, 7, 1, 1).Wall(3, 7, 1, 1);

        private static string[] Rows(int width, int depth, char c) => Enumerable.Repeat(new string(c, width), depth).ToArray();

        [Test]
        public void MudBandCutsTheRoad() =>
            Assert.That(Check(MudBand(), TerrainPack(MudBlockedPackJson)),
                Is.EqualTo(new[] { P("GATE_UNREACHABLE", "base 2 point 1", 44, 16, 44500, 16500) }));

        [Test]
        public void SecondBaseOfATeamIsCheckedToo()
        {
            E2Map m = Enclosure().Point(1, 10500, 2500).Point(2, 15500, 7500)
                .Base(1, 1, 2500, 2500).Base(2, 1, 2500, 7500).Base(3, 2, 17500, 2500);
            Assert.That(Check(m.Bytes(), M6Pack()), Is.EqualTo(new[]
            {
                P("GATE_UNREACHABLE", "base 2 point 1", 2, 7, 2500, 7500),
                P("GATE_UNREACHABLE", "base 2 point 2", 2, 7, 2500, 7500),
            }));
        }

        internal static E2Map PointBesideAWall() =>
            new E2Map().Wall(10, 4, 1, 2).Point(1, 9900, 4500).Point(2, 5500, 4500).Base(1, 1, 2500, 2500).Base(2, 2, 17500, 7500);

        internal static E2Map PointInAWall() =>
            new E2Map().Wall(10, 4, 1, 2).Point(1, 10500, 4500).Point(2, 5500, 4500).Base(1, 1, 2500, 2500).Base(2, 2, 17500, 7500);

        // The point 0.1 m west of the wall; and a point inside the wall cell, which step 3 must not try to reach.
        [Test]
        public void BlockedPointsAreReportedOnceAndSkipReachability()
        {
            Assert.That(Check(PointBesideAWall().Bytes(), M6Pack()), Is.EqualTo(new[] { P("GATE_POINT_BLOCKED", "point 1", 9, 4, 9900, 4500) }));
            Assert.That(Check(PointInAWall().Bytes(), M6Pack()), Is.EqualTo(new[] { P("GATE_POINT_BLOCKED", "point 1", 10, 4, 10500, 4500) }));
        }

        // Wp23SpawnTests corner: wall box x 10, z 2..4; (9850, 1850) is inside the 0.25 m circle around the corner (10, 2).
        [Test]
        public void BaseInsideTheWallCornerCircle()
        {
            E2Map m = new E2Map { Width = 16, Depth = 8 }.Wall(10, 2, 1, 3).Point(1, 5500, 4500).Base(1, 1, 9850, 1850).Base(2, 2, 14500, 6500);
            Assert.That(Check(m.Bytes(), M6Pack()), Is.EqualTo(new[] { P("GATE_SPAWN_FOOTPRINT", "base 1", 9, 1, 9850, 1850) }));
            E2Map outside = new E2Map { Width = 16, Depth = 8 }.Wall(10, 2, 1, 3).Point(1, 5500, 4500).Base(1, 1, 9823, 1823).Base(2, 2, 14500, 6500);
            Assert.That(Check(outside.Bytes(), M6Pack()), Is.Empty);
        }

        // Height points all 1 m: a wall needs heightRaw >= 2 m. 2 raw short is a problem and never reaches the writer.
        [TestCase(131070L, true)]
        [TestCase(131072L, false)]
        public void WallTooLowOverTheTerrain(long heightRaw, bool low)
        {
            var m = new E2Map { Width = 4, Depth = 3, Heights = Enumerable.Repeat(Enumerable.Repeat(65536, 5).ToArray(), 4).ToArray() }
                .Wall(1, 1, 1, 1, heightRaw);
            MapProblem[] problems = Check(m.Bytes(), M6Pack());
            if (low) Assert.That(problems, Is.EqualTo(new[] { P("GATE_WALL_TERRAIN", "wall 1", 1, 1) }));
            else Assert.That(problems, Is.Empty);
        }

        [Test]
        public void TeamWithoutABaseAndWallOnTheMoverLayer()
        {
            E2Map noBase = new E2Map().Point(1, 10500, 4500).Base(1, 1, 2500, 2500);
            Assert.That(Check(noBase.Bytes(), M6Pack()), Is.EqualTo(new[] { P("GATE_MATCH", "team 2") }));
            E2Map mover = new E2Map { LayerBit = 0 }.Wall(5, 5, 1, 1);
            Assert.That(Check(mover.Bytes(), M6Pack()), Is.EqualTo(new[] { P("GATE_LAYER", "layer walls") }));
        }

        // Without control points the reserve unit is placed at the start, so its footprint counts; with a match it does not.
        [Test]
        public void ReserveOnAWallCountsOnlyWithoutPoints()
        {
            E2Map noMatch = new E2Map().Wall(5, 5, 1, 1).Unit(1, 5500, 5500, reserve: true);
            Assert.That(Check(noMatch.Bytes(), M6Pack()), Is.EqualTo(new[] { P("GATE_SPAWN_FOOTPRINT", "unit 1", 5, 5, 5500, 5500) }));
            E2Map match = new E2Map().Wall(5, 5, 1, 1).Unit(1, 5500, 5500, reserve: true).Point(1, 10500, 4500).Base(1, 1, 2500, 2500).Base(2, 2, 17500, 7500);
            Assert.That(Check(match.Bytes(), M6Pack()), Is.Empty);
        }

        [Test]
        public void UnknownUnitTypeAndSurface()
        {
            E2Map unit = new E2Map().Unit(1, 3500, 4500, type: "rwr.nope");
            Assert.That(Check(unit.Bytes(), M6Pack()), Is.EqualTo(new[] { P("GATE_REF", "unit 1", 3, 4, 3500, 4500) }));

            E2Map lava = new E2Map { Pack = "rwr-terrain-demo", Surfaces = new MapSurfaces(new[] { "lava" }, Rows(20, 10, 'A')) };
            Assert.That(Check(lava.Bytes(), TerrainPack()), Is.EqualTo(new[] { P("GATE_REF", "surface lava") }));

            // A broken surface table skips every navigation check (the point inside the wall is not reported).
            lava.Unit(1, 3500, 4500, type: "rwr.nope2").Wall(10, 4, 1, 2).Point(1, 10500, 4500).Base(1, 1, 2500, 2500);
            Assert.That(Check(lava.Bytes(), TerrainPack()), Is.EqualTo(new[]
            {
                P("GATE_MATCH", "team 2"),
                P("GATE_REF", "surface lava"),
                P("GATE_REF", "unit 1", 3, 4, 3500, 4500),
            }));
        }

        [Test]
        public void PackWithoutTerrainNamesEverySurface()
        {
            string[] rows = Rows(20, 10, 'A');
            rows[3] = new string('B', 20);
            E2Map m = new E2Map { Surfaces = new MapSurfaces(new[] { "grass", "mud" }, rows) };
            Assert.That(Check(m.Bytes(), M6Pack()), Is.EqualTo(new[] { P("GATE_REF", "surface grass"), P("GATE_REF", "surface mud") }));
        }

        internal static byte[] Version2()
        {
            string text = Encoding.UTF8.GetString(new E2Map().Bytes());
            Assert.That(text, Does.Contain("\n  \"version\": 1,\n"));
            return Encoding.UTF8.GetBytes(text.Replace("\n  \"version\": 1,\n", "\n  \"version\": 2,\n"));
        }

        [Test]
        public void UnreadableDescriptionIsOneProblemWithItsLine() =>
            Assert.That(Check(Version2(), M6Pack()), Is.EqualTo(new[] { P("MAPDESC_FORMAT", line: 3) }));

        // Team 2's base is closed in, plus one unknown unit type and one point inside a wall.
        internal static E2Map MultiProblem() =>
            Enclosure().Wall(10, 4, 1, 2).Point(1, 15500, 2500).Point(2, 10500, 4500)
                .Base(1, 1, 2500, 2500).Base(2, 2, 2500, 7500).Unit(1, 6500, 2500, type: "rwr.nope");

        [Test]
        public void SeveralProblemsComeBackSorted() =>
            Assert.That(Check(MultiProblem().Bytes(), M6Pack()), Is.EqualTo(new[]
            {
                P("GATE_POINT_BLOCKED", "point 2", 10, 4, 10500, 4500),
                P("GATE_REF", "unit 1", 6, 2, 6500, 2500),
                P("GATE_UNREACHABLE", "base 2 point 1", 2, 7, 2500, 7500),
            }));

        // A (z 5, x 9) base 1, B (z 3, x 12) unit 3, C (z 3, x 4) base 2 and D (z 3, x 4) unit 7, each inside a wall cell.
        [Test]
        public void SameCodeSortsByZThenXThenId()
        {
            E2Map m = new E2Map().Wall(9, 5, 1, 1).Wall(12, 3, 1, 1).Wall(4, 3, 1, 1)
                .Base(1, 1, 9500, 5500).Unit(3, 12500, 3500).Base(2, 2, 4500, 3500).Unit(7, 4200, 3800);
            Assert.That(Check(m.Bytes(), M6Pack()), Is.EqualTo(new[]
            {
                P("GATE_SPAWN_FOOTPRINT", "base 2", 4, 3, 4500, 3500),
                P("GATE_SPAWN_FOOTPRINT", "unit 7", 4, 3, 4200, 3800),
                P("GATE_SPAWN_FOOTPRINT", "unit 3", 12, 3, 12500, 3500),
                P("GATE_SPAWN_FOOTPRINT", "base 1", 9, 5, 9500, 5500),
            }));
        }

        // Acceptance 4: Wp22TerrainTests' anchor cases as descriptions. The gate's navigation codes appear exactly when
        // TerrainAnchors.Validate throws on the same map and anchors.
        private static readonly string[] NavCodes = { "GATE_UNREACHABLE", "GATE_POINT_BLOCKED", "GATE_SPAWN_FOOTPRINT" };

        private static void Agrees(byte[] bytes, ContentPack pack, MapProblem[] expected)
        {
            MapProblem[] problems = Check(bytes, pack);
            Assert.That(problems, Is.EqualTo(expected));
            bool gate = problems.Any(p => NavCodes.Contains(p.Code));
            bool runtime = E2RuntimeLockTests.RuntimeAnchors(bytes, pack) != "ok";
            Assert.That(gate, Is.EqualTo(runtime));
        }

        [Test]
        public void AnchorsDemoReachesEveryPoint() => Agrees(TerrainDemo(), TerrainPack(), new MapProblem[0]);

        [Test]
        public void AnchorsMudBand() =>
            Agrees(MudBand(), TerrainPack(MudBlockedPackJson), new[] { P("GATE_UNREACHABLE", "base 2 point 1", 44, 16, 44500, 16500) });

        [Test]
        public void AnchorsSlopeAndWall()
        {
            Agrees(TerrainDemo(), TerrainPack(SteepPackJson), new[]
            {
                P("GATE_POINT_BLOCKED", "point 1", 22, 16, 22500, 16500),
                P("GATE_UNREACHABLE", "base 1 point 2", 3, 16, 3500, 16500),
            });
            Agrees(WallAnchor(), TerrainPack(), new[] { P("GATE_SPAWN_FOOTPRINT", "base 1", 6, 10, 6500, 10500) });
        }

        [Test]
        public void AnchorsPlateau() =>
            Agrees(Plateau(), TerrainPack(), new[]
            {
                P("GATE_UNREACHABLE", "base 1 point 1", 3, 16, 3500, 16500),
                P("GATE_UNREACHABLE", "base 2 point 1", 44, 16, 44500, 16500),
            });
    }
}
