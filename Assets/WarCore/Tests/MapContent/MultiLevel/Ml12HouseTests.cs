using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Content.MapTools;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.Ml12Fixtures;
using static WarCore.Tests.Ml6Nav;

namespace WarCore.Tests
{
    // ML-12a: the building stamp rebuilds the demo house, a three-floor tower is walkable to the top, and the gate accepts both.
    public sealed class Ml12HouseTests
    {
        private static MapBuildResult House() => Doc(DemoGround()).WithBuilding(null, DemoHouse());

        private static MapBuildResult TowerDoc(int floors) => Doc(Blank(24, 16)).WithBuilding(null, Tower(floors));

        private static WcMap Bake(MapDocument doc) => WcMapReader.Read(MapBake.Run(doc.Save()).WcMapBytes);

        private static string Geometry(WcMapPlate p) => p.X + "," + p.Z + " " + p.Width + "x" + p.Depth + " " + p.Axis + " " + p.FromRaw + ">" + p.ToRaw
            + " t" + p.ThickRaw + " s" + p.Surface + " solid " + string.Join(".", p.Solid) + " walk " + string.Join(".", p.Walkable);

        // Plates and cutouts: the baked plates of the building equal the demo's, field by field (ids and wall splits may differ).
        [Test]
        public void TheBuildingRebuildsTheDemoHouse()
        {
            MapBuildResult r = House();
            Assert.That(r.Instance, Is.EqualTo("building-1"));
            WcMap built = Bake(r.Document), demo = WcMapReader.Read(PlateDemoMap.Bytes());
            List<string> mine = built.Plates.Select(Geometry).OrderBy(s => s, StringComparer.Ordinal).ToList();
            List<string> theirs = demo.Plates.Select(Geometry).OrderBy(s => s, StringComparer.Ordinal).ToList();
            Assert.That(mine.Except(theirs).ToList(), Is.Empty, "built only");
            Assert.That(theirs.Except(mine).ToList(), Is.Empty, "demo only");
            Assert.That(mine, Is.EqualTo(theirs));
            MapDesc d = Desc(r.Document);
            Assert.That(d.Cutouts.Select(c => c.Cell.X + "," + c.Cell.Z + " " + c.Size.X + "x" + c.Size.Z).ToArray(), Is.EqualTo(new[] { "10,9 4x1" }));
            Assert.That(d.Cutouts[0].Plate, Is.EqualTo(BySrc(d.Plates, p => p.Src, "building-1", MapBuildings.Src(1, 0, MapBuildings.PartPlate)).Id));
            // The ground floor's walls stand 3 m high from the floor like the demo's (the door's lintel from 2 m); the demo has
            // no first-floor walls.
            Assert.That(d.Walls.Where(w => w.BaseRaw == null).Select(w => w.HeightRaw).Distinct().ToArray(), Is.EqualTo(new[] { 3 * M }));
            Assert.That(d.Walls.Where(w => w.BaseRaw != null).Select(w => w.BaseRaw.Value + "+" + w.HeightRaw).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToArray(),
                Is.EqualTo(new[] { 2 * M + "+" + M, 3 * M + "+" + 3 * M }));
            // The walkable ground cells: the same as the demo's.
            LayeredNav a = WcMapLayeredNav.Build(built, null, built.ContentId), b = WcMapLayeredNav.Build(demo, null, demo.ContentId);
            Assert.That((a.GroundCount, a.LayerCount, a.Count), Is.EqualTo((b.GroundCount, b.LayerCount, b.Count)));
            for (int n = 0; n < a.GroundCount; n++) Assert.That(a.CostAt(n), Is.EqualTo(b.CostAt(n)), "ground " + n);
        }

        // The tower's nav layer of a plate: layer k is the plate with the k-th smallest id.
        private static int Layer(MapDesc d, ulong src)
        {
            ulong id = BySrc(d.Plates, p => p.Src, "building-1", src).Id;
            return 1 + d.Plates.Select(p => p.Id).OrderBy(x => x).ToList().IndexOf(id);
        }

        // Tower(3): from the ground cell outside the door (5, 1) to the first floor's (10, 8) and the second floor's (10, 8).
        //   Ground (5, 1) -> (3, 8): through the door (5, 2), (5, 3), 2 diagonals, 3 straight steps: 2000 + 2 x 1414 + 3000 = 7828.
        //   Seam onto stair 0's foot (4, 8): 1000; up its cells 5..9: 5000; seam to the first floor's (10, 8): 1000. Total 14828.
        //   First floor (10, 8) -> (3, 8) round the hole over x 4..9 on row 8 (no corner cutting past the hole): (10, 9) 1000,
        //   7 straight steps along row 9 to (3, 9) 7000, (3, 8) 1000: 9000; seam, stair 1, seam: 7000.
        //   Total to the second floor: 14828 + 9000 + 7000 = 30828.
        [Test]
        public void ATowerOfThreeFloorsIsWalkableToTheTop()
        {
            MapBuildResult r = TowerDoc(3);
            MapDesc d = Desc(r.Document);
            WcMap map = Bake(r.Document);
            LayeredNav nav = WcMapLayeredNav.Build(map, null, map.ContentId);
            Assert.That(nav.LayerCount, Is.EqualTo(5));
            int start = Node(nav, 0, 5, 1);
            int first = Node(nav, Layer(d, MapBuildings.Src(1, 0, MapBuildings.PartPlate)), 10, 8);
            int second = Node(nav, Layer(d, MapBuildings.Src(2, 0, MapBuildings.PartPlate)), 10, 8);
            Found(new LayeredPathfinder(nav), start, first, 14828);
            Found(new LayeredPathfinder(nav), start, second, 30828);
            // The cross-check of ML-11a's reverse search agrees.
            Assert.That(new LayeredReach(nav, second).CostFrom(start), Is.EqualTo(30828));

            ulong stair1 = BySrc(d.Plates, p => p.Src, "building-1", MapBuildings.Src(1, 0, MapBuildings.PartStair)).Id;
            MapDocument cut = r.Document.WithoutEntry("plate", stair1);
            MapDesc cd = Desc(cut);
            WcMap cutMap = Bake(cut);
            LayeredNav cutNav = WcMapLayeredNav.Build(cutMap, null, cutMap.ContentId);
            int s2 = Node(cutNav, 0, 5, 1);
            int f1 = Node(cutNav, Layer(cd, MapBuildings.Src(1, 0, MapBuildings.PartPlate)), 10, 8);
            int f2 = Node(cutNav, Layer(cd, MapBuildings.Src(2, 0, MapBuildings.PartPlate)), 10, 8);
            Found(new LayeredPathfinder(cutNav), s2, f1, 14828);
            Assert.That(new LayeredPathfinder(cutNav).Find(s2, f2), Is.EqualTo(GridPathStatus.Unreachable));
        }

        [Test]
        public void TheGateAcceptsTheBuildings()
        {
            Assert.That(Gate(House().Document).Select(p => p.Code).ToArray(), Is.Empty);
            Assert.That(Gate(TowerDoc(3).Document).Select(p => p.Code).ToArray(), Is.Empty);
            Assert.That(Gate(TowerDoc(1).Document).Select(p => p.Code).ToArray(), Is.Empty);
            MapDocument two = Doc(Blank(40, 16)).WithBuilding(null, Tower(2)).Document;
            Assert.That(Gate(two).Select(p => p.Code).ToArray(), Is.Empty);
        }

        // A 3 m stair over one cell is too steep; a 2.4 m storey leaves less than 2 m under the plate above. Both stop with
        // nothing changed.
        [Test]
        public void TooSteepOrTooLowStops()
        {
            MapDocument blank = Doc(Blank(24, 16));
            byte[] before = blank.Save();
            var steep = new MapBuildingInfo(2, 2, 12, 10, 2, 3 * M, M / 2, "s", 3, 2, true, 4, 8, 1);
            Assert.That(Assert.Throws<ArgumentException>(() => blank.WithBuilding(null, steep)).Message, Does.Contain("too steep"));
            var low = new MapBuildingInfo(2, 2, 12, 10, 2, 157286, M / 2, "s", 3, 2, true, 4, 8, 4);
            Assert.Throws<ArgumentException>(() => blank.WithBuilding(null, low));
            Assert.That(blank.Save(), Is.EqualTo(before));
            // Out of the map, and walls past the ceiling.
            Assert.Throws<ArgumentException>(() => blank.WithBuilding(null, new MapBuildingInfo(14, 2, 12, 10, 2, 3 * M, M / 2, "s", 3, 2, true, 4, 8, 4)));
            Assert.Throws<ArgumentException>(() => Doc(Blank(24, 16, ceiling: 8 * M)).WithBuilding(null, Tower(3)));
            // Three floors stack stair over stair: 4 cells (0.75 m a cell) leave 1.75 m, 5 cells (0.6 m) 1.9 m; 6 cells is the
            // shortest. Two floors have one stair and take 4 cells.
            foreach (int length in new[] { 4, 5 })
            {
                var stacked = new MapBuildingInfo(2, 2, 12, 10, 3, 3 * M, M / 2, "s", 3, 2, true, 4, 8, length);
                Assert.That(Assert.Throws<ArgumentException>(() => blank.WithBuilding(null, stacked)).Message, Does.Contain("less than 2 m"), "length " + length);
            }
            MapBuildResult two = blank.WithBuilding(null, new MapBuildingInfo(2, 2, 12, 10, 2, 3 * M, M / 2, "s", 3, 2, true, 4, 8, 4));
            Assert.Throws<ArgumentException>(() => two.Document.WithFloors(two.BuildJson, "building-1", 3));
            Assert.That(blank.Save(), Is.EqualTo(before));
        }

        // The ground under the building may differ by 0.3 m (LayeredNav.StepRaw); the floors stand on its highest point.
        [Test]
        public void TheBuildingStandsOnItsHighestGround()
        {
            MapDocument flat = Doc(Blank(24, 16)).WithHeightsEnabled(0);
            MapDocument step = flat.WithHeight(5, 5, (int)LayeredNav.StepRaw);
            MapDesc d = Desc(step.WithBuilding(null, Tower(2)).Document);
            MapPlate p1 = BySrc(d.Plates, p => p.Src, "building-1", 1001);
            Assert.That(p1.YRaw, Is.EqualTo(LayeredNav.StepRaw + 3 * M));
            MapWall n0 = BySrc(d.Walls, w => w.Src, "building-1", 11);
            Assert.That(n0.HeightRaw, Is.EqualTo(LayeredNav.StepRaw + 3 * M));
            // A point just outside the footprint does not count; one more raw inside it does.
            Assert.DoesNotThrow(() => flat.WithHeight(15, 5, (int)(4 * M)).WithBuilding(null, Tower(2)));
            Assert.That(Assert.Throws<ArgumentException>(() => flat.WithHeight(14, 12, (int)LayeredNav.StepRaw + 1).WithBuilding(null, Tower(2))).Message,
                Does.Contain("not flat"));
        }

        // Each bad parameter stops; the edges that just fit do not.
        [Test]
        public void BadParametersStop()
        {
            MapDocument blank = Doc(Blank(24, 16));
            MapBuildingInfo B(int sizeX = 12, int floors = 2, long storey = 3 * M, long thick = M / 2, string side = "s", int doorAt = 3, int doorWidth = 2,
                bool alongX = true, int stairX = 4, int stairZ = 8, int length = 6) =>
                new MapBuildingInfo(2, 2, sizeX, 10, floors, storey, thick, side, doorAt, doorWidth, alongX, stairX, stairZ, length);
            var bad = new Dictionary<string, MapBuildingInfo>
            {
                { "size 3", B(sizeX: 3, stairX: 3, length: 1, doorAt: 1, doorWidth: 1) }, { "floors 0", B(floors: 0) }, { "floors 33", B(floors: 33) },
                { "odd storey", B(storey: 3 * M + 1) }, { "storey 2.4 m", B(storey: 157286) }, { "thin", B(thick: M / 2 - 1) },
                { "headroom 1.9 m", B(storey: 170394, thick: 45876) }, { "side q", B(side: "q") }, { "side sw", B(side: "sw") },
                { "door at 0", B(doorAt: 0) }, { "door width 0", B(doorWidth: 0) }, { "door to the corner", B(doorAt: 10, doorWidth: 2) },
                { "stair 0", B(length: 0) }, { "stair foot on the wall", B(stairX: 3) }, { "stair top on the wall", B(stairX: 7) },
                { "stair along z off the end", B(alongX: false, stairX: 5, stairZ: 5, length: 6) },
                { "stair along z foot on the wall", B(alongX: false, stairX: 5, stairZ: 3, length: 4) },
            };
            foreach (KeyValuePair<string, MapBuildingInfo> c in bad)
                Assert.Throws<ArgumentException>(() => blank.WithBuilding(null, c.Value), c.Key);
            foreach (MapBuildingInfo ok in new[] { B(doorAt: 9, doorWidth: 2), B(doorAt: 1, doorWidth: 1), B(stairX: 6), B(alongX: false, stairX: 5, stairZ: 4, length: 6),
                B(storey: 170394, thick: 39322), B(floors: 1, length: 1) })
                Assert.DoesNotThrow(() => blank.WithBuilding(null, ok), ok.DoorAt + " " + ok.StairX + " " + ok.StairLength + " " + ok.StoreyRaw);
        }

        // Without the cutout over the stair the plate above stacks on it: the gate says so.
        [Test]
        public void WithoutTheCutoutTheGateReportsTheStack()
        {
            MapDocument doc = TowerDoc(2).Document;
            ulong cutout = Desc(doc).Cutouts.Single().Id;
            IReadOnlyList<MapProblem> problems = Gate(doc.WithoutEntry("cutout", cutout));
            Assert.That(problems.Select(p => p.Code).Distinct().ToArray(), Is.EqualTo(new[] { "GATE_PLATE_STACK" }));
        }
    }
}
