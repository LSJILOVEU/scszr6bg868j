using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Content.MapTools;
using WarCore.Content.Rwr;
using WarCore.Presentation;

namespace WarCore.Tests
{
    // ML-4 acceptance 10 (gate), the gate report (deliverable 6) and the runtime refusal (acceptance 11, last line).
    public sealed class Ml4GateTests
    {
        private const long M = Ml4Maps.M;
        private const long Half = Ml4Maps.Half;

        private static IEnumerable<string> Lines(IReadOnlyList<MapProblem> problems) =>
            problems.Select(p => p.Code + " " + p.Id + (p.HasCell ? " " + p.CellX + "," + p.CellZ : ""));

        [Test]
        public void DemoIsTheBuilderAndPassesTheGate()
        {
            byte[] stored = File.ReadAllBytes(Ml4Maps.DemoPath);
            Assert.That(stored, Is.EqualTo(Ml4Maps.Bytes(Ml4Maps.Demo())));
            Assert.That(MapDescJson.Write(MapDescJson.Read(stored)), Is.EqualTo(stored));
            Assert.That(MapDescJson.Read(stored).Version, Is.EqualTo(2));
            Assert.That(MapGate.Check(stored, RwrPack.Load("rwr-m6")), Is.Empty);
            WcMap map = WcMapReader.Read(MapBake.Run(stored).WcMapBytes);
            Assert.That(map.Plates.Select(p => p.Id), Is.EqualTo(new ulong[] { 1, 2, 3 }));
        }

        // The house as built: stair cells blocked on the ground, open on the stair; the first floor open except under the
        // partition, open under the lintel; the cutout over the stair; the roof all walkable.
        [Test]
        public void DemoHouseIsWhatTheDescriptionSays()
        {
            WcMap map = WcMapReader.Read(MapBake.Run(File.ReadAllBytes(Ml4Maps.DemoPath)).WcMapBytes);
            WcMapPlate stair = map.Plates[0], floor = map.Plates[1], roof = map.Plates[2];
            Assert.That(Enumerable.Range(0, 5).Select(i => stair.TopRaw(i, 0)), Is.EqualTo(new[] { 0, 3 * M / 4, 3 * M / 2, 9 * M / 4, 3 * M }));
            Assert.That(Enumerable.Range(9, 4).Select(x => map.IsPassable(x, 9)), Is.EqualTo(new[] { false, false, false, false }), "ground under the stair");
            Assert.That(Enumerable.Range(0, 4).All(i => stair.IsWalkable(i, 0)), Is.True);
            Assert.That(Enumerable.Range(0, 4).Select(i => floor.IsSolid(i, 3)), Is.EqualTo(new[] { false, false, false, false }), "cutout over the stair");
            Assert.That(floor.IsSolid(4, 3) && floor.IsWalkable(4, 3), Is.True, "landing at the top of the stair");
            Assert.That(new[] { floor.IsWalkable(2, 0), floor.IsWalkable(2, 1), floor.IsWalkable(2, 2) }, Is.EqualTo(new[] { false, true, false }), "partition and doorway");
            Assert.That(Enumerable.Range(0, 24).All(i => roof.IsWalkable(i % 6, i / 6)), Is.True);
            Assert.That(new[] { map.IsPassable(8, 7), map.IsPassable(8, 8), map.IsPassable(8, 6), map.IsPassable(10, 7) }, Is.EqualTo(new[] { true, true, false, true }),
                "the west doorway and the room behind it, under the first floor");
        }

        [Test]
        public void EachCounterExampleReportsOneProblem()
        {
            Assert.That(Lines(Ml4Maps.Gate(Ml4Maps.Buried())), Is.EqualTo(new[] { "GATE_PLATE_BURIED plate 1 1,1" }));
            Assert.That(Lines(Ml4Maps.Gate(Ml4Maps.Stacked())), Is.EqualTo(new[] { "GATE_PLATE_STACK plate 1 plate 2 2,1" }));
            Assert.That(Lines(Ml4Maps.Gate(Ml4Maps.WallThrough())), Is.EqualTo(new[] { "GATE_WALL_PLATE wall 1 plate 1 2,2" }));
            Assert.That(Lines(Ml4Maps.Gate(Ml4Maps.Steep())), Is.EqualTo(new[] { "GATE_PLATE_SLOPE plate 1 1,1" }));
        }

        // A pack walkable slope of 3/4: a rise of exactly 3/4 per cell passes, 1 raw more does not.
        [Test]
        public void PlateSlopeFollowsThePack()
        {
            MapDesc Rising(long to) => Pack(Ml4Maps.Make(4, 1, Ml4Maps.Slope(1, 1, 0, 2, 1, "x", 0, to)), "rwr-terrain-demo");
            ContentPack pack = RwrPack.Load("rwr-terrain-demo");
            Assert.That((pack.Terrain.MaxWalkableNum, pack.Terrain.MaxWalkableDen), Is.EqualTo((3, 4)));
            Assert.That(Ml4Maps.Gate(Rising(2 * 49152), pack), Is.Empty);
            Assert.That(Lines(Ml4Maps.Gate(Rising(2 * 49152 + 2), pack)), Is.EqualTo(new[] { "GATE_PLATE_SLOPE plate 1 1,0" }));
        }

        // A pack at 2/1 is capped at 5/3: 109226 raw per cell passes, 109227 does not (no integer rise is exactly 5/3).
        [Test]
        public void PlateSlopeIsCappedAtFiveThirds()
        {
            Assert.That(RwrPack.Load("rwr-m6").Terrain, Is.Null, "no slope rule: 2/1");
            Assert.That(Ml4Maps.Gate(Ml4Maps.Make(3, 1, Ml4Maps.Slope(1, 1, 0, 1, 1, "x", 0, 109226))), Is.Empty);
            Assert.That(Lines(Ml4Maps.Gate(Ml4Maps.Make(3, 1, Ml4Maps.Slope(1, 1, 0, 1, 1, "x", 0, 109227)))), Is.EqualTo(new[] { "GATE_PLATE_SLOPE plate 1 1,0" }));
            Assert.That(Lines(Ml4Maps.Gate(Ml4Maps.Make(1, 3, Ml4Maps.Slope(1, 0, 1, 1, 1, "z", 109227, 0)))), Is.EqualTo(new[] { "GATE_PLATE_SLOPE plate 1 0,1" }));
        }

        private static MapDesc Pack(MapDesc d, string pack) => new MapDesc(d.Id, d.Content, pack, d.Size, d.Origin, d.FloorRaw, d.CeilingRaw,
            d.Layers.ToArray(), d.Heights?.Select(r => r.ToArray()).ToArray(), d.Surfaces, d.Plates.ToArray(), d.Cutouts.ToArray(), d.Walls.ToArray(),
            d.Points.ToArray(), d.Bases.ToArray(), d.Units.ToArray(), d.Run, d.Owners.ToArray(), null, null, d.Stamps.ToArray(), d.Removed.ToArray(), d.Next);

        // Three plates in a stack with two conflicting pairs: two problems; the three are left out, so a wall through them is
        // not reported, while a wall through a fourth, good plate still is.
        [Test]
        public void StackPairsAreReportedOnceAndTheRestBakes()
        {
            MapDesc d = Ml4Maps.Make(0, 6, 2, null,
                new[] { Ml4Maps.Flat(1, 0, 0, 2, 1, 3 * M), Ml4Maps.Flat(2, 0, 0, 2, 1, 4 * M), Ml4Maps.Flat(3, 0, 0, 2, 1, 5 * M), Ml4Maps.Flat(4, 3, 0, 2, 1, 3 * M) },
                walls: new[] { Ml4Maps.Wall(1, 3, 0, 1, 1, 5 * M), Ml4Maps.Wall(2, 1, 0, 1, 1, 6 * M) });
            Assert.That(Lines(Ml4Maps.Gate(d)), Is.EqualTo(new[]
            {
                "GATE_PLATE_STACK plate 1 plate 2 0,0", "GATE_PLATE_STACK plate 2 plate 3 0,0", "GATE_WALL_PLATE wall 1 plate 4 3,0",
            }));
        }

        // A plate left out takes its cutouts with it: no crash, no separate report.
        [Test]
        public void DroppedPlateTakesItsCutouts()
        {
            MapDesc d = Ml4Maps.Make(0, 6, 4, Ml4Maps.Ground(6, 4, M), new[] { Ml4Maps.Flat(1, 1, 1, 3, 2, Half), Ml4Maps.Flat(2, 4, 0, 2, 2, 4 * M) },
                new[] { Ml4Maps.Cut(1, 1, 2, 1, 1, 1), Ml4Maps.Cut(2, 2, 4, 0, 1, 1) });
            Assert.That(Lines(Ml4Maps.Gate(d)), Is.EqualTo(new[] { "GATE_PLATE_BURIED plate 1 1,1" }));
        }

        [Test]
        public void OneWallThroughTwoPlatesIsTwoProblems()
        {
            MapDesc d = Ml4Maps.Make(0, 6, 2, null, new[] { Ml4Maps.Flat(1, 0, 0, 2, 1, 3 * M), Ml4Maps.Flat(2, 3, 0, 2, 1, 3 * M) },
                walls: new[] { Ml4Maps.Wall(1, 1, 0, 3, 1, 4 * M) });
            Assert.That(Lines(Ml4Maps.Gate(d)), Is.EqualTo(new[] { "GATE_WALL_PLATE wall 1 plate 1 1,0", "GATE_WALL_PLATE wall 1 plate 2 3,0" }));
        }

        // Soldiers stay on the ground in ML-4; one under a low plate cannot stand there.
        [Test]
        public void SoldierUnderALowPlate()
        {
            var units = new[] { new MapUnit(1, "rwr.soldier-1", 1, 1, new MapMm(1500, 1500), false), new MapUnit(2, "rwr.soldier-1", 1, 1, new MapMm(5500, 1500), false) };
            MapDesc d = Ml4Maps.Make(0, 6, 4, null, new[] { Ml4Maps.Flat(1, 1, 1, 3, 2, M) }, units: units);
            Assert.That(Lines(Ml4Maps.Gate(d)), Is.EqualTo(new[] { "GATE_SPAWN_FOOTPRINT unit 1 1,1" }));
        }

        // A .wcmap with plates is refused by the runtime until ML-6 to ML-9 read them.
        [Test]
        public void RuntimeRefusesPlates()
        {
            byte[] bytes = MapBake.Run(File.ReadAllBytes(Ml4Maps.DemoPath)).WcMapBytes;
            var recipe = new WorldRecipe(41, MoverKind.InterimCrowd, new[] { new UnitSpawn(1, 0, 1, 1500, 1500, 6000, 1) }, Array.Empty<BridgeCommand>());
            Assert.Throws<NotSupportedException>(() => SimSession.Create(recipe, bytes));
            byte[] flat = MapBake.Run(Ml4Maps.Bytes(Ml4Maps.Make(0, 4, 4, null, new MapPlate[0]))).WcMapBytes;
            Assert.DoesNotThrow(() => SimSession.Create(recipe, flat));
        }

        // ---- the gate report (deliverable 6) ----

        // The demo and the four counter-examples through the gate: its problems, and per plate its number, solid cells,
        // walkable cells and the ground cells whose headroom it takes. Written to WARCORE_ML4_GATE_REPORT when set.
        [Test]
        public void GateReport()
        {
            var text = new StringBuilder();
            foreach ((string name, MapDesc d) in new[] { ("plate-demo", Ml4Maps.Demo()), ("buried", Ml4Maps.Buried()), ("stacked", Ml4Maps.Stacked()),
                ("wall-through", Ml4Maps.WallThrough()), ("steep", Ml4Maps.Steep()) })
            {
                IReadOnlyList<MapProblem> problems = Ml4Maps.Gate(d);
                text.Append("map ").Append(name).Append(": ").Append(problems.Count).Append(" problem(s)\n");
                foreach (string line in Lines(problems)) text.Append("  ").Append(line).Append('\n');
                List<PlateGeom> geoms = MapBake.PlateGeoms(d);
                List<WallGeom> walls = MapBake.WallGeoms(d);
                for (int n = 0; n < geoms.Count; n++)
                {
                    PlateGeom g = geoms[n];
                    int solid = g.Solid.Count(b => b), walkable = PlateSpan.Walkable(g, walls).Count(b => b);
                    int headroom = PlateSpan.GroundBlocks(d.Size.X, d.Size.Z, MapBake.LocalPoint(d), new[] { g }).Count(b => b);
                    text.Append("  plate number ").Append(n + 1).Append(" (id ").Append(g.Id).Append("): solid ").Append(solid)
                        .Append(", walkable ").Append(walkable).Append(", ground cells without headroom ").Append(headroom).Append('\n');
                }
            }
            string report = text.ToString();
            TestContext.Out.Write(report);
            Assert.That(report, Does.Contain("map plate-demo: 0 problem(s)"));
            Assert.That(report, Does.Contain("plate number 2 (id 2): solid 20, walkable 18, ground cells without headroom 0"));
            string path = Environment.GetEnvironmentVariable("WARCORE_ML4_GATE_REPORT");
            if (!string.IsNullOrEmpty(path)) File.WriteAllText(path, report);
        }
    }
}
