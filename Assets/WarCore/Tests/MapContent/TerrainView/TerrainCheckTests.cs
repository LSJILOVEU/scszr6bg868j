using NUnit.Framework;
using WarCore.Content;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests.TerrainView
{
    // L8a (3)-(5): drawn walls, collision, navigation and point heights agree cell by cell; each broken view is caught.
    public sealed class TerrainCheckTests
    {
        private static ViewMap View(WcMap map) => ViewMap.Parse(L8aFixtures.Bytes(map));

        private static void Expect(TerrainCheckResult r, bool shape, int walls, int nav, int heights)
        {
            Assert.That(r.ShapeMismatch, Is.EqualTo(shape), "shape\n" + r);
            Assert.That(r.WallCells, Is.EqualTo(walls), "walls\n" + r);
            Assert.That(r.NavCells, Is.EqualTo(nav), "nav\n" + r);
            Assert.That(r.HeightPoints, Is.EqualTo(heights), "heights\n" + r);
            Assert.That(r.IsClean, Is.EqualTo(!shape && walls == 0 && nav == 0 && heights == 0));
        }

        // Independent per-cell classes of a session's map (wall / slope / surface / open) and its walkable count.
        private static (int Wall, int Slope, int Surface, int Open, int Walkable) Classify(SimSession s, WcMap map, int num, int den)
        {
            int wall = 0, slope = 0, surface = 0, open = 0, walkable = 0, cells = 0;
            HeightField field = map.HasHeightField ? L8aFixtures.Field(map) : null;
            TerrainTables t = s.Terrain;
            for (int z = 0; z < (int)map.Depth; z++)
                for (int x = 0; x < (int)map.Width; x++)
                {
                    cells++;
                    if (s.BlockedCell(x, z)) wall++;
                    else if (field != null && !field.CellWithinSlope(x, z, num, den)) slope++;
                    else if (map.HasSurfaceTable && map.SurfaceCells[z * (int)map.Width + x] == 1) surface++;
                    else open++;
                    if (t.Nav.IsPassable(x, z)) walkable++;
                }
            Assert.That(cells, Is.EqualTo(48));
            return (wall, slope, surface, open, walkable);
        }

        // (3) F: wall 2, slope 12 (columns x = 3, 4), mud 1, open 33; Sim walks exactly the 33 open cells.
        [Test]
        public void MapF_IsClean_AndEveryClassIsPresent()
        {
            WcMap map = L8aFixtures.F();
            SimSession s = L8aFixtures.Session(map, L8aFixtures.Pack());
            TerrainCheckResult clean = TerrainCheck.Run(s.Map, s);
            Expect(clean, false, 0, 0, 0);
            Assert.That(clean.ToString(), Is.EqualTo("shape same\nwalls 0\nnav 0\nheights 0\n"));
            Assert.That(s.Map, Is.Not.Null);
            Assert.That(s.TerrainRules, Is.Not.Null);
            Assert.That(Classify(s, map, 3, 4), Is.EqualTo((2, 12, 1, 33, 33)));
        }

        // (3) F': no height field, only the surface table: still a terrain world; wall 2, mud 1, open 45.
        [Test]
        public void MapFWithoutHeights_IsClean()
        {
            WcMap map = L8aFixtures.F(heights: false);
            SimSession s = L8aFixtures.Session(map, L8aFixtures.Pack());
            Assert.That(s.Terrain, Is.Not.Null);
            Assert.That(s.Terrain.Ground, Is.Null);
            Expect(TerrainCheck.Run(s.Map, s), false, 0, 0, 0);
            Assert.That(Classify(s, map, 3, 4), Is.EqualTo((2, 0, 1, 45, 45)));
        }

        // (3) F'': no surface table, plain rwr pack (no terrain section): slope falls back to 2/1, so the 1.5 ridge is
        // walkable; wall 2, open 46.
        [Test]
        public void MapFWithoutSurfaces_RwrPack_IsClean()
        {
            WcMap map = L8aFixtures.F(surfaces: false);
            SimSession s = L8aFixtures.Session(map, L8aFixtures.RwrOnly());
            Assert.That(s.TerrainRules, Is.Null);
            Expect(TerrainCheck.Run(s.Map, s), false, 0, 0, 0);
            Assert.That(Classify(s, map, 2, 1), Is.EqualTo((2, 0, 0, 46, 46)));
        }

        private static SimSession SessionF() => L8aFixtures.Session(L8aFixtures.F(), L8aFixtures.Pack());

        // (4)(i) The wall drawn one column right, centre x = 2.5 (bits follow): old 2 + new 2 cells disagree with collision and navigation.
        [Test]
        public void WallMoved_ReportsFourWallAndFourNavCells()
        {
            TerrainCheckResult r = TerrainCheck.Run(View(L8aFixtures.F(wallX: 2)), SessionF());
            Expect(r, false, 4, 4, 0);
            // Scan order is z-major: (1,1) (2,1) (1,2) (2,2).
            Assert.That(r.ToString(), Is.EqualTo("shape same\nwalls 4 1,1 2,1 1,2 2,2\nnav 4 1,1 2,1 1,2 2,2\nheights 0\n"));
        }

        // (4)(ii) The ridge missing from the view: 12 cells walkable in the view, not in Sim; the 7 points at i = 4 differ.
        [Test]
        public void RidgeMissing_ReportsTwelveNavCellsAndSevenPoints() =>
            Expect(TerrainCheck.Run(View(L8aFixtures.F(ridge: false)), SessionF()), false, 0, 12, 7);

        // (4)(iii) Mud drawn as ground at (6,5).
        [Test]
        public void MudDrawnAsGround_ReportsOneNavCell() =>
            Expect(TerrainCheck.Run(View(L8aFixtures.F(mud: false)), SessionF()), false, 0, 1, 0);

        // (4)(iv) Every view point 1 raw higher: slopes unchanged, all (8 + 1) * (6 + 1) = 63 points differ.
        [Test]
        public void AllPointsOneRawHigher_ReportsSixtyThreePoints()
        {
            TerrainCheckResult r = TerrainCheck.Run(View(L8aFixtures.F(lift: 1)), SessionF());
            Expect(r, false, 0, 0, 63);
            // Only the first 8 points of the scan (row j = 0, i = 0..7) are listed.
            Assert.That(r.ToString(), Is.EqualTo("shape same\nwalls 0\nnav 0\nheights 63 0,0 1,0 2,0 3,0 4,0 5,0 6,0 7,0\n"));
        }

        // (4)(v) A 7-wide view: shape mismatch, no cell scan.
        [Test]
        public void NarrowerView_ReportsShape() =>
            Expect(TerrainCheck.Run(View(L8aFixtures.F(width: 7)), SessionF()), true, 0, 0, 0);

        // (4)(vi) The view's origin at x = 1 (Max.X = 9): shape mismatch.
        [Test]
        public void ShiftedView_ReportsShape()
        {
            TerrainCheckResult r = TerrainCheck.Run(View(L8aFixtures.F(minX: 1)), SessionF());
            Expect(r, true, 0, 0, 0);
            Assert.That(r.ToString(), Is.EqualTo("shape differs\nwalls 0\nnav 0\nheights 0\n"));
        }

        // (5) Flat worlds: only shape and walls are checked, and they agree.
        [Test]
        public void FlatM6_IsClean()
        {
            SimSession s = SimSession.Create(DemoRecipes.M6(), DemoRecipes.M6MapBytes());
            Assert.That(s.Terrain, Is.Null);
            Expect(TerrainCheck.Run(s.Map, s), false, 0, 0, 0);
        }

        [Test]
        public void FlatM7_IsClean()
        {
            SimSession s = SimSession.Create(DemoRecipes.M7(), DemoRecipes.M7MapBytes());
            Assert.That(s.Terrain, Is.Null);
            Expect(TerrainCheck.Run(s.Map, s), false, 0, 0, 0);
        }
    }
}
