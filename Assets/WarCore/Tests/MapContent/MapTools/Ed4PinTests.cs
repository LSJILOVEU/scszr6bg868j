using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Content.MapTools;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // ED-4 acceptance 5 and 6: problem pins take their places from the gate's own answer (never worked out again), problems
    // without a place only go into the list, and the run set-up starts the same world as the replay's own recipe.
    public sealed class Ed4PinTests
    {
        private Ed0Dirs dirs;

        [SetUp] public void Make() => dirs = new Ed0Dirs();
        [TearDown] public void Clean() => dirs.Dispose();

        // Origin (-10, 5): world x in [-10, 10), z in [5, 15). One wall at relative cell (5, 3) = world cell [-5, -4) x [8, 9); a
        // control point inside it at an off-centre place; one base (team 1 only); a soldier of an unknown type.
        private static byte[] BrokenStructure()
        {
            var map = new E2Map { Origin = new MapXZ(-10, 5) };
            map.Wall(5, 3, 1, 1);
            map.Point(1, -4300, 8700);
            map.Base(1, 1, -8500, 6500);
            map.Unit(1, -2300, 10700, type: "rwr.nosuch");
            return map.Bytes();
        }

        // The same origin; heights with one point 1 m + 1 raw above a 2 m wall's clearance at its first corner (relative (5, 3)).
        private static byte[] WallInTerrain()
        {
            var map = new E2Map { Origin = new MapXZ(-10, 5) };
            map.Heights = new int[map.Depth + 1][];
            for (int z = 0; z < map.Heights.Length; z++) map.Heights[z] = new int[map.Width + 1];
            map.Heights[3][5] = 65537;
            map.Wall(5, 3, 1, 1);
            return map.Bytes();
        }

        private static MapPreviewData Build(byte[] bytes) => MapPreview.Build(bytes, E2Fixtures.M6Pack(), MapPalette.Default, Ed4Fixtures.Messages());

        [Test]
        public void EveryPlacedProblemOfABrokenMapBecomesOnePinAtThePlaceTheGateGave()
        {
            byte[] bytes = BrokenStructure();
            IReadOnlyList<MapProblem> gate = MapGate.Check(bytes, E2Fixtures.M6Pack());
            MapPreviewData d = Build(bytes);
            Assert.That(gate.Select(p => p.Code), Is.EqualTo(new[] { "GATE_MATCH", "GATE_POINT_BLOCKED", "GATE_REF" }));
            var placed = gate.Where(p => p.HasMm || p.HasCell).ToList();
            Assert.That(d.Pins.Count, Is.EqualTo(placed.Count).And.EqualTo(2));
            for (int i = 0; i < placed.Count; i++)
            {
                Assert.That((d.Pins[i].Code, d.Pins[i].Id), Is.EqualTo((placed[i].Code, placed[i].Id)));
                Assert.That((d.Pins[i].XMm, d.Pins[i].ZMm), Is.EqualTo((placed[i].XMm, placed[i].ZMm)));
            }
            // Written down: the control point's own millimetres and the soldier's, not the centre of their cells.
            Assert.That((d.Pins[0].XMm, d.Pins[0].ZMm), Is.EqualTo((-4300L, 8700L)));
            Assert.That((d.Pins[1].XMm, d.Pins[1].ZMm), Is.EqualTo((-2300L, 10700L)));
        }

        [Test]
        public void AProblemWithoutAPlaceIsListedAndNotPinned()
        {
            MapPreviewData d = Build(BrokenStructure());
            Assert.That(d.Problems.Select(p => p.Code), Is.EqualTo(new[] { "GATE_MATCH", "GATE_POINT_BLOCKED", "GATE_REF" }));
            Assert.That(d.Pins.Select(p => p.Code), Is.EqualTo(new[] { "GATE_POINT_BLOCKED", "GATE_REF" }));
            Assert.That(d.Problems[0].Id, Is.EqualTo("team 2"));
            Assert.That(d.Pins.Select(p => p.ProblemIndex), Is.EqualTo(new[] { 1, 2 }), "a pin names its problem by index");
            Assert.That(ContentJson.Parse(MapPreview.ToJson(d), "x").Find("pins").Items.Select(i => i.Find("problem").Text), Is.EqualTo(new[] { "1", "2" }));
        }

        [Test]
        public void ACellOnlyProblemIsPinnedAtTheCentreOfItsCellCountedFromTheOrigin()
        {
            byte[] bytes = WallInTerrain();
            MapProblem problem = MapGate.Check(bytes, E2Fixtures.M6Pack()).Single();
            Assert.That((problem.Code, problem.HasCell, problem.HasMm, problem.CellX, problem.CellZ), Is.EqualTo(("GATE_WALL_TERRAIN", true, false, 5, 3)));
            MapPreviewData data = Build(bytes);
            MapPreviewPin pin = data.Pins.Single();
            Assert.That((data.OriginX, data.OriginZ, data.Width, data.Depth), Is.EqualTo((-10, 5, 20, 10)));
            Assert.That((pin.Code, pin.Id), Is.EqualTo((problem.Code, problem.Id)));
            // (origin + cell) * 1000 + 500 = (-10 + 5) * 1000 + 500 and (5 + 3) * 1000 + 500.
            Assert.That((pin.XMm, pin.ZMm), Is.EqualTo((-4500L, 8500L)));
        }

        [Test]
        public void APinCarriesTheTextOfItsProblemAndTheListCarriesTheSameText()
        {
            byte[] bytes = BrokenStructure();
            MapMessages table = Ed4Fixtures.Messages();
            MapPreviewData d = Build(bytes);
            IReadOnlyList<MapProblem> gate = MapGate.Check(bytes, E2Fixtures.M6Pack());
            for (int i = 0; i < gate.Count; i++)
            {
                Assert.That(d.Problems[i].Text, Is.EqualTo(table.Describe(gate[i])));
                Assert.That(d.Problems[i].Text, Is.Not.EqualTo(gate[i].Code));
            }
            foreach (MapPreviewPin pin in d.Pins)
                Assert.That(pin.Text, Is.EqualTo(d.Problems.First(p => p.Code == pin.Code).Text));
        }

        [Test]
        public void AWriterProblemIsListedWithTheWriterCodeAndNoPin()
        {
            var map = new E2Map();
            map.Heights = new int[map.Depth + 1][];
            for (int z = 0; z < map.Heights.Length; z++) map.Heights[z] = new int[map.Width + 1];
            map.Heights[4][6] = 3 * 65536;
            MapPreviewData d = Build(map.Bytes());
            MapPreviewProblem problem = d.Problems.Single();
            Assert.That((problem.Code, problem.Id), Is.EqualTo(("MAPDESC_WCMAP", "WCMAP_HEIGHT_SLOPE")));
            Assert.That(d.Pins, Is.Empty);
            Assert.That(d.Width, Is.EqualTo(map.Width), "a writer problem still draws the map");
            Assert.That(d.Shade.Count, Is.EqualTo(map.Width * map.Depth));
        }

        [Test]
        public void ADescriptionThatCannotBeReadHasOneProblemNoPinsAndNoGrid()
        {
            byte[] bytes = Encoding.UTF8.GetBytes("{ \"format\": \"nonsense\" }");
            MapPreviewData d = Build(bytes);
            Assert.That(d.Problems.Count, Is.EqualTo(1));
            Assert.That(d.Problems[0].Code, Does.StartWith("MAPDESC_"));
            Assert.That(d.Pins, Is.Empty);
            Assert.That((d.Width, d.Depth), Is.EqualTo((0, 0)));
            Assert.That(d.Shade, Is.Empty);
            Assert.That(d.Steep, Is.Empty);
            Assert.That(d.Ground, Is.Empty);
            Assert.That(d.Walls.Count + d.Plates.Count + d.Points.Count + d.Bases.Count + d.Units.Count + d.Notes.Count, Is.EqualTo(0));
            Assert.That(MapPreview.ToJson(d), Does.Contain("\"problems\""));
        }

        [Test]
        public void ANonJsonFileAlsoGivesOneProblem()
        {
            MapPreviewData d = Build(Encoding.UTF8.GetBytes("not json at all"));
            Assert.That(d.Problems.Count, Is.EqualTo(1));
            Assert.That(d.Pins, Is.Empty);
        }

        [Test]
        public void APackThatIsNotTheDescriptionsPackIsRefusedAsByTheGate()
        {
            byte[] bytes = Ed4Fixtures.MapBytes("terrain-demo");
            Assert.Throws<System.ArgumentException>(() => MapPreview.Build(bytes, E2Fixtures.M6Pack(), MapPalette.Default, Ed4Fixtures.Messages()));
        }

        [Test]
        public void ARunSetupStartsTheWorldTheReplayRecipeStarts()
        {
            byte[] bytes = Ed4Fixtures.MapBytes("lanes-playtest");
            MapRunSetup setup = MapRunSetup.From(bytes);
            MapBakeResult baked = MapBake.Run(bytes);
            Assert.That(setup.Bake.PackId, Is.EqualTo(baked.PackId));
            Assert.That(setup.Bake.WcMapBytes, Is.EqualTo(baked.WcMapBytes));
            SimSession mine = SimSession.Create(setup.Recipe, setup.Bake.WcMapBytes);
            SimSession theirs = SimSession.Create(MapRecipe.From(baked, WarCore.Content.Rwr.RwrPack.Load(baked.PackId)), baked.WcMapBytes);
            for (int i = 0; i < 600; i++) { mine.StepOnce(); theirs.StepOnce(); }
            Assert.That(mine.Tick, Is.EqualTo(600));
            Assert.That(mine.StateHash, Is.EqualTo(theirs.StateHash));
        }

        [Test]
        public void ARunSetupTakesThePackFromTheCatalogSoAnAncientMapRuns()
        {
            byte[] bytes = Ed0ProjectTests.AncientMap();
            MapRunSetup setup = MapRunSetup.From(bytes);
            Assert.That(setup.Bake.PackId, Is.EqualTo("ancient"));
            SimSession mine = SimSession.Create(setup.Recipe, setup.Bake.WcMapBytes), theirs = MapDocument.Open(bytes).Start();
            for (int i = 0; i < 300; i++) { mine.StepOnce(); theirs.StepOnce(); }
            Assert.That(mine.StateHash, Is.EqualTo(theirs.StateHash));
        }

        [Test]
        public void ARunSetupRefusesWhatTheBakeRefuses()
        {
            Assert.Throws<System.IO.InvalidDataException>(() => MapRunSetup.From(Encoding.UTF8.GetBytes("{}")));
        }
    }
}
