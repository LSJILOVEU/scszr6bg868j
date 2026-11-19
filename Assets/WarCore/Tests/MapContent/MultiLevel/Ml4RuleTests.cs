using System.Linq;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Content.MapTools;

namespace WarCore.Tests
{
    // ML-4 acceptance 7, 8 and 9: ground headroom, walkable on a plate and the hard plate rules, each edge from both sides.
    public sealed class Ml4RuleTests
    {
        private const long M = Ml4Maps.M;
        private const long Half = Ml4Maps.Half;

        private static string Code(MapDesc d) => Ml4WcMapTests.Code(Ml4Maps.Bake(d));
        private static string GateCodes(MapDesc d) => string.Join(" | ", Ml4Maps.Gate(d).Select(p => p.Code + " " + p.Id + " " + p.CellX + "," + p.CellZ));

        // 4 x 1 cells whose highest corners are 0, 0.5 m, 1 m and 1 m (the same slope as ML-1's fixtures).
        private static int[][] Slope() => new[]
        {
            new[] { 0, 0, 0, (int)Half, (int)Half },
            new[] { 0, 0, (int)Half, (int)M, (int)M },
        };

        // ---- acceptance 7: rule 1 ----

        [TestCase(0)]
        [TestCase(-65536)]
        public void PlateBottomTwoMetresUpLeavesTheGroundOpen(long floor)
        {
            WcMap open = Ml4Maps.Valid(Ml4Maps.Bake(Ml4Maps.Make(floor, 3, 1, null, new[] { Ml4Maps.Flat(1, 1, 0, 1, 1, floor + 2 * M + Half) })));
            Assert.That(open.IsPassable(1, 0), Is.True);
            WcMap shut = Ml4Maps.Valid(Ml4Maps.Bake(Ml4Maps.Make(floor, 3, 1, null, new[] { Ml4Maps.Flat(1, 1, 0, 1, 1, floor + 2 * M + Half - 1) })));
            Assert.That(shut.IsPassable(1, 0), Is.False);
            Assert.That(shut.IsPassable(0, 0) && shut.IsPassable(2, 0), Is.True);
        }

        // Bottom 2.6 m over cells with highest corners 0, 0.5 and 1 m: only the third is blocked (per-cell gmax).
        [Test]
        public void HeadroomIsJudgedOnEachCellsHighestCorner()
        {
            const long top = 3 * M + 6554;
            Assert.That(top - Half, Is.EqualTo(2 * M + 39322), "bottom about 2.6 m");
            WcMap map = Ml4Maps.Valid(Ml4Maps.Bake(Ml4Maps.Make(0, 4, 1, Slope(), new[] { Ml4Maps.Flat(1, 0, 0, 3, 1, top) })));
            Assert.That(Enumerable.Range(0, 4).Select(x => map.IsPassable(x, 0)), Is.EqualTo(new[] { true, true, false, true }));
        }

        // A plate falling 3 m -> 2 m along x over four cells: cell tops' lowest corners 2.75, 2.5, 2.25, 2.0 m, bottoms
        // 2.25, 2.0, 1.75, 1.5 m; flat ground at 0 is blocked under the last two.
        [Test]
        public void HeadroomUnderASlopedPlateIsPerCell()
        {
            WcMap map = Ml4Maps.Valid(Ml4Maps.Bake(Ml4Maps.Make(0, 6, 1, null, new[] { Ml4Maps.Slope(1, 1, 0, 4, 1, "x", 3 * M, 2 * M) })));
            Assert.That(Enumerable.Range(0, 6).Select(x => map.IsPassable(x, 0)), Is.EqualTo(new[] { true, true, true, false, false, true }));
        }

        // ---- acceptance 8: rule 2 ----

        private static WcMapPlate OnePlate(long floor, params MapWall[] walls) =>
            Ml4Maps.Valid(Ml4Maps.Bake(Ml4Maps.Make(floor, 3, 1, null, new[] { Ml4Maps.Flat(1, 0, 0, 3, 1, floor + 3 * M) }, walls: walls))).Plates[0];

        [TestCase(0)]
        [TestCase(-65536)]
        public void RaisedWallTwoMetresOverThePlateLeavesItWalkable(long floor)
        {
            Assert.That(OnePlate(floor, Ml4Maps.Wall(1, 1, 0, 1, 1, M, floor + 5 * M)).IsWalkable(1, 0), Is.True);
            WcMapPlate low = OnePlate(floor, Ml4Maps.Wall(1, 1, 0, 1, 1, M, floor + 5 * M - 1));
            Assert.That(low.IsWalkable(1, 0), Is.False);
            Assert.That(low.IsWalkable(0, 0) && low.IsWalkable(2, 0), Is.True);
            Assert.That(OnePlate(floor, Ml4Maps.Wall(1, 1, 0, 1, 1, M, floor + 3 * M)).IsWalkable(1, 0), Is.False, "a wall standing on the plate");
        }

        // On a sloped plate the headroom counts from the cell's highest corner: cell 0 of a plate rising 3 -> 4 m over two
        // cells has corners 3 and 3.5 m, so a wall bottom at 5 m (2 m over the lowest) blocks and one at 5.5 m does not.
        [Test]
        public void SlopedPlateHeadroomUsesTheHighestCorner()
        {
            WcMapPlate Under(long baseRaw) => Ml4Maps.Valid(Ml4Maps.Bake(Ml4Maps.Make(0, 3, 1, null, new[] { Ml4Maps.Slope(1, 0, 0, 2, 1, "x", 3 * M, 4 * M) },
                walls: new[] { Ml4Maps.Wall(1, 0, 0, 1, 1, M, baseRaw) }))).Plates[0];
            Assert.That(Under(5 * M).IsWalkable(0, 0), Is.False);
            Assert.That(Under(5 * M + Half - 1).IsWalkable(0, 0), Is.False);
            Assert.That(Under(5 * M + Half).IsWalkable(0, 0), Is.True);
        }

        // A floor wall under the plate (top = plate bottom, thickness 0.5 m) does not block the plate; its bottom is floorRaw.
        [TestCase(0)]
        [TestCase(-65536)]
        public void WallUnderThePlateDoesNotBlockIt(long floor)
        {
            WcMapPlate p = OnePlate(floor, Ml4Maps.Wall(1, 1, 0, 1, 1, 2 * M + Half));
            Assert.That(p.IsWalkable(1, 0), Is.True);
            Assert.That(PlateSpan.Walkable(new PlateGeom(1, 0, 0, 1, 1, PlateAxis.Flat, 3 * M, 3 * M, Half, new[] { true }),
                new[] { new WallGeom(1, 0, 0, 1, 1, 0, 3 * M - Half) }), Is.EqualTo(new[] { true }));
            Assert.That(PlateSpan.Walkable(new PlateGeom(1, 0, 0, 1, 1, PlateAxis.Flat, 3 * M, 3 * M, Half, new[] { true }),
                new[] { new WallGeom(1, 0, 0, 1, 1, 0, 3 * M - M / 4) }), Is.EqualTo(new[] { false }), "the band reaches 0.25 m under the top");
        }

        // ---- acceptance 9: rules 3-7 ----

        // Plate over cell 2 of the slope (far corner 1 m): a top of 1 m is on the ground, 1 raw lower is buried.
        [Test]
        public void BuriedIsCornerWise()
        {
            MapDesc on = Ml4Maps.Make(0, 4, 1, Slope(), new[] { Ml4Maps.Flat(1, 2, 0, 1, 1, M) });
            Ml4Maps.Valid(Ml4Maps.Bake(on));
            MapDesc under = Ml4Maps.Make(0, 4, 1, Slope(), new[] { Ml4Maps.Flat(1, 2, 0, 1, 1, M - 1) });
            Assert.That(Code(under), Is.EqualTo("WCMAP_PLATE_BURIED"));
            Assert.That(GateCodes(under), Is.EqualTo("GATE_PLATE_BURIED plate 1 2,0"));
            Assert.That(GateCodes(on), Is.Empty);
        }

        [Test]
        public void BuriedOnANegativeFloor()
        {
            int[][] ground = Ml4Maps.Ground(3, 1, 0);
            Ml4Maps.Valid(Ml4Maps.Bake(Ml4Maps.Make(-M, 3, 1, ground, new[] { Ml4Maps.Flat(1, 1, 0, 1, 1, 0) })));
            Assert.That(Code(Ml4Maps.Make(-M, 3, 1, ground, new[] { Ml4Maps.Flat(1, 1, 0, 1, 1, -1) })), Is.EqualTo("WCMAP_PLATE_BURIED"));
        }

        [TestCase(0)]
        [TestCase(-65536)]
        public void StackGapIsTwoMetresInclusive(long floor)
        {
            MapDesc fit = Ml4Maps.Make(floor, 4, 1, null, new[] { Ml4Maps.Flat(1, 0, 0, 2, 1, floor + 3 * M), Ml4Maps.Flat(2, 1, 0, 2, 1, floor + 5 * M + Half) });
            Ml4Maps.Valid(Ml4Maps.Bake(fit));
            MapDesc tight = Ml4Maps.Make(floor, 4, 1, null, new[] { Ml4Maps.Flat(1, 0, 0, 2, 1, floor + 3 * M), Ml4Maps.Flat(2, 1, 0, 2, 1, floor + 5 * M + Half - 1) });
            Assert.That(Code(tight), Is.EqualTo("WCMAP_PLATE_STACK"));
            Assert.That(GateCodes(tight), Is.EqualTo("GATE_PLATE_STACK plate 1 plate 2 1,0"));
        }

        // Three plates: only neighbours by topMax are checked, and the middle pair is still caught.
        [Test]
        public void ThreeInAStackCheckNeighbours()
        {
            MapPlate a = Ml4Maps.Flat(3, 0, 0, 1, 1, 3 * M), c = Ml4Maps.Flat(1, 0, 0, 1, 1, 8 * M);
            Ml4Maps.Valid(Ml4Maps.Bake(Ml4Maps.Make(0, 3, 1, null, new[] { a, Ml4Maps.Flat(2, 0, 0, 1, 1, 5 * M + Half), c }, ceilingRaw: 9 * M)));
            MapDesc middle = Ml4Maps.Make(0, 3, 1, null, new[] { a, Ml4Maps.Flat(2, 0, 0, 1, 1, 5 * M + Half - 1), c }, ceilingRaw: 9 * M);
            Assert.That(Code(middle), Is.EqualTo("WCMAP_PLATE_STACK"));
            Assert.That(GateCodes(middle), Is.EqualTo("GATE_PLATE_STACK plate 2 plate 3 0,0"));
            Assert.That(PlateSpan.Stacks(3, 1, Ml4Maps.Bake(middle).Plates.Select(Geom).ToList(), out _, out _).Count, Is.EqualTo(1));
        }

        [Test]
        public void EqualTopsAreAStack()
        {
            MapDesc d = Ml4Maps.Make(0, 4, 1, null, new[] { Ml4Maps.Flat(2, 0, 0, 2, 1, 3 * M), Ml4Maps.Flat(1, 1, 0, 2, 1, 3 * M) });
            Assert.That(Code(d), Is.EqualTo("WCMAP_PLATE_STACK"));
            Assert.That(GateCodes(d), Is.EqualTo("GATE_PLATE_STACK plate 1 plate 2 1,0"));
        }

        private static PlateGeom Geom(WcMapPlate p) => new PlateGeom(p.Id, p.X, p.Z, p.Width, p.Depth, p.Axis, p.FromRaw, p.ToRaw, p.ThickRaw,
            PlateSpan.Bits(p.Solid, p.CellCount));

        // Wall top = plate bottom and wall bottom = plate top are legal; 1 raw into the plate box is not.
        [Test]
        public void WallThroughPlateEdges()
        {
            MapDesc With(MapWall w) => Ml4Maps.Make(0, 3, 1, null, new[] { Ml4Maps.Flat(1, 0, 0, 3, 1, 3 * M) }, walls: new[] { w });
            Ml4Maps.Valid(Ml4Maps.Bake(With(Ml4Maps.Wall(1, 1, 0, 1, 1, 3 * M / 2, M))));
            Ml4Maps.Valid(Ml4Maps.Bake(With(Ml4Maps.Wall(1, 1, 0, 1, 1, M, 3 * M))));
            MapDesc top = With(Ml4Maps.Wall(1, 1, 0, 1, 1, 3 * M / 2, M + 1));
            Assert.That(Code(top), Is.EqualTo("WCMAP_WALL_PLATE"));
            Assert.That(GateCodes(top), Is.EqualTo("GATE_WALL_PLATE wall 1 plate 1 1,0"));
            Assert.That(Code(With(Ml4Maps.Wall(1, 1, 0, 1, 1, M, 3 * M - 1))), Is.EqualTo("WCMAP_WALL_PLATE"));
        }

        [Test]
        public void WallInACutoutIsLegal()
        {
            MapDesc d = Ml4Maps.Make(0, 3, 1, null, new[] { Ml4Maps.Flat(1, 0, 0, 3, 1, 3 * M) }, new[] { Ml4Maps.Cut(1, 1, 1, 0, 1, 1) },
                new[] { Ml4Maps.Wall(1, 1, 0, 1, 1, 5 * M) });
            Ml4Maps.Valid(Ml4Maps.Bake(d));
            Assert.That(GateCodes(d), Is.Empty);
            MapDesc wide = Ml4Maps.Make(0, 3, 1, null, new[] { Ml4Maps.Flat(1, 0, 0, 3, 1, 3 * M) }, new[] { Ml4Maps.Cut(1, 1, 1, 0, 1, 1) },
                new[] { Ml4Maps.Wall(1, 1, 0, 2, 1, 5 * M) });
            Assert.That(Code(wide), Is.EqualTo("WCMAP_WALL_PLATE"));
            Assert.That(GateCodes(wide), Is.EqualTo("GATE_WALL_PLATE wall 1 plate 1 2,0"));
        }

        // A floor wall on a negative floor stands on floorRaw: top floorRaw + 2.5 m meets a plate bottom at floorRaw + 2.5 m.
        [Test]
        public void FloorWallOnANegativeFloor()
        {
            MapDesc With(long height) => Ml4Maps.Make(-M, 3, 1, null, new[] { Ml4Maps.Flat(1, 0, 0, 3, 1, 2 * M) }, walls: new[] { Ml4Maps.Wall(1, 1, 0, 1, 1, height) });
            Ml4Maps.Valid(Ml4Maps.Bake(With(2 * M + Half)));
            Assert.That(Code(With(2 * M + Half + 2)), Is.EqualTo("WCMAP_WALL_PLATE"));
        }

        // .wcmap slope: 2/1 per cell is legal, 1 raw more is not.
        [Test]
        public void PlateSlopeTwoToOne()
        {
            Ml4Maps.Valid(Ml4Maps.Bake(Ml4Maps.Make(4, 1, Ml4Maps.Slope(1, 1, 0, 2, 1, "x", 0, 4 * M))));
            Assert.That(Code(Ml4Maps.Make(4, 1, Ml4Maps.Slope(1, 1, 0, 2, 1, "x", 0, 4 * M + 2))), Is.EqualTo("WCMAP_PLATE_SLOPE"));
            Ml4Maps.Valid(Ml4Maps.Bake(Ml4Maps.Make(1, 4, Ml4Maps.Slope(1, 0, 1, 1, 2, "z", 4 * M, 0))));
            Assert.That(Code(Ml4Maps.Make(1, 4, Ml4Maps.Slope(1, 0, 1, 1, 2, "z", 4 * M + 2, 0))), Is.EqualTo("WCMAP_PLATE_SLOPE"));
        }

        // 64 plates in one cell are legal, 65 are CAPACITY (.wcmap and the gate's bake).
        [Test]
        public void SixtyFourPlatesPerCell()
        {
            MapDesc Tower(int n) => Ml4Maps.Make(0, 9, 8, null,
                Enumerable.Range(0, n).Select(k => Ml4Maps.Flat((ulong)k + 1, 0, 0, 1, 1, Half + k * (2 * M + Half))).ToArray(), ceilingRaw: 200 * M);
            Ml4Maps.Valid(Ml4Maps.Bake(Tower(64)));
            Assert.That(Code(Tower(65)), Is.EqualTo("WCMAP_CAPACITY"));
            Assert.That(GateCodes(Tower(65)), Does.StartWith("MAPDESC_WCMAP WCMAP_CAPACITY"));
        }

        // Solid cells of all plates up to the map's cell count.
        [Test]
        public void SolidCellsUpToTheMapCells()
        {
            Ml4Maps.Valid(Ml4Maps.Bake(Ml4Maps.Make(2, 2, Ml4Maps.Flat(1, 0, 0, 2, 2, 3 * M))));
            Ml4Maps.Valid(Ml4Maps.Bake(Ml4Maps.Make(0, 2, 2, null, new[] { Ml4Maps.Flat(1, 0, 0, 2, 2, 3 * M), Ml4Maps.Flat(2, 0, 0, 1, 1, 6 * M) },
                new[] { Ml4Maps.Cut(1, 1, 1, 1, 1, 1) })));
            MapDesc over = Ml4Maps.Make(2, 2, Ml4Maps.Flat(1, 0, 0, 2, 2, 3 * M), Ml4Maps.Flat(2, 0, 0, 1, 1, 6 * M));
            Assert.That(Code(over), Is.EqualTo("WCMAP_CAPACITY"));
            Assert.That(GateCodes(over), Does.StartWith("MAPDESC_WCMAP WCMAP_CAPACITY"));
        }
    }
}
