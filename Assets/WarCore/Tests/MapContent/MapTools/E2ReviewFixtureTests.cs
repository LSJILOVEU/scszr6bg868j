using System.Linq;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Content.MapTools;
using static WarCore.Tests.E2Fixtures;
using static WarCore.Tests.E2GateTests;

namespace WarCore.Tests
{
    // Fixtures added after the first review: writer errors, point spawns, the unknown-type unit, layer reporting, a map origin
    // away from zero and ordinal id order.
    public sealed class E2ReviewFixtureTests
    {
        private static MapProblem[] Check(byte[] bytes, ContentPack pack) => MapGate.Check(bytes, pack).ToArray();

        private static string[] Rows(int width, int depth, char c) => Enumerable.Repeat(new string(c, width), depth).ToArray();

        // Two layers on one bit pass the reader but not the writer; the writer's own code is kept.
        [Test]
        public void WriterErrorKeepsItsCode()
        {
            E2Map m = new E2Map { ExtraLayers = new[] { new MapLayer("other", 1) } }.Wall(5, 5, 1, 1);
            Assert.That(Check(m.Bytes(), M6Pack()), Is.EqualTo(new[] { P("MAPDESC_WCMAP", "WCMAP_LAYERS") }));
        }

        // The point itself stands and stays in step 3; only its spawn inside the wall is reported.
        [Test]
        public void PointSpawnInAWall()
        {
            E2Map m = new E2Map().Wall(10, 4, 1, 2).Point(1, 5500, 4500, new MapMm(10500, 4500)).Base(1, 1, 2500, 2500).Base(2, 2, 17500, 7500);
            Assert.That(Check(m.Bytes(), M6Pack()), Is.EqualTo(new[] { P("GATE_SPAWN_FOOTPRINT", "spawn 1", 10, 4, 10500, 4500) }));
        }

        [Test]
        public void UnknownTypeOnAWallIsBothProblems()
        {
            E2Map m = new E2Map().Wall(5, 5, 1, 1).Unit(1, 5500, 5500, type: "rwr.nope");
            Assert.That(Check(m.Bytes(), M6Pack()), Is.EqualTo(new[]
            {
                P("GATE_REF", "unit 1", 5, 5, 5500, 5500),
                P("GATE_SPAWN_FOOTPRINT", "unit 1", 5, 5, 5500, 5500),
            }));
        }

        [Test]
        public void LayerIsReportedOncePerLayerUsedByAWall()
        {
            E2Map twoWalls = new E2Map { LayerBit = 0 }.Wall(5, 5, 1, 1).Wall(8, 5, 1, 1);
            Assert.That(Check(twoWalls.Bytes(), M6Pack()), Is.EqualTo(new[] { P("GATE_LAYER", "layer walls") }));
            E2Map unused = new E2Map { ExtraLayers = new[] { new MapLayer("spare", 0) } }.Wall(5, 5, 1, 1);
            Assert.That(Check(unused.Bytes(), M6Pack()), Is.Empty);
        }

        [Test]
        public void BrokenSurfaceTableStillReportsTheLayer()
        {
            E2Map m = new E2Map { Pack = "rwr-terrain-demo", LayerBit = 0, Surfaces = new MapSurfaces(new[] { "lava" }, Rows(20, 10, 'A')) }.Wall(5, 5, 1, 1);
            Assert.That(Check(m.Bytes(), TerrainPack()), Is.EqualTo(new[] { P("GATE_LAYER", "layer walls"), P("GATE_REF", "surface lava") }));
        }

        // Origin (-5, 3): cells count from the origin, millimetres stay world coordinates; with and without a grid.
        [Test]
        public void CellsAreRelativeToTheMapOrigin()
        {
            E2Map nav = new E2Map { Origin = new MapXZ(-5, 3) }.Wall(2, 1, 1, 1).Unit(1, -2500, 4500);
            Assert.That(Check(nav.Bytes(), M6Pack()), Is.EqualTo(new[] { P("GATE_SPAWN_FOOTPRINT", "unit 1", 2, 1, -2500, 4500) }));
            E2Map noNav = new E2Map { Origin = new MapXZ(-5, 3), Pack = "rwr-terrain-demo", Surfaces = new MapSurfaces(new[] { "lava" }, Rows(20, 10, 'A')) }
                .Unit(1, 10500, 12500, type: "rwr.nope");
            Assert.That(Check(noNav.Bytes(), TerrainPack()), Is.EqualTo(new[]
            {
                P("GATE_REF", "surface lava"),
                P("GATE_REF", "unit 1", 15, 9, 10500, 12500),
            }));
        }

        // Same code and cell: ids compare ordinally, so "point 10" sorts before "point 2".
        [Test]
        public void IdsCompareOrdinally()
        {
            E2Map m = new E2Map().Wall(1, 6, 3, 1).Wall(1, 8, 3, 1).Wall(1, 7, 1, 1).Wall(3, 7, 1, 1)
                .Point(2, 10500, 2500).Point(10, 15500, 7500).Base(1, 1, 2500, 7500).Base(2, 2, 17500, 2500);
            Assert.That(Check(m.Bytes(), M6Pack()), Is.EqualTo(new[]
            {
                P("GATE_UNREACHABLE", "base 1 point 10", 2, 7, 2500, 7500),
                P("GATE_UNREACHABLE", "base 1 point 2", 2, 7, 2500, 7500),
            }));
        }
    }
}
