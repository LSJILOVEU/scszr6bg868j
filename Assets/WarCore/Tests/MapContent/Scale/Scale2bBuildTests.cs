using System;
using System.Linq;
using NUnit.Framework;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Content.MapTools;
using WarCore.Presentation;
using static WarCore.Tests.Scale2bFixtures;

namespace WarCore.Tests
{
    // SCALE-2b acceptance SC9-SC11 and SC17: windows, storeys and stairs. What the section accepts must build: windows through
    // the prefabs (with the odd-height padding triggered), storeys through MapBuilding, stairs through MapBuildings.
    public sealed class Scale2bBuildTests
    {
        // SC9: sill within [2r, min(E, M) - r - 1]; top within [max(E, M) + r, storey - plate].
        [Test]
        public void SC9_WindowBounds()
        {
            Accepts("rwr-stance", ("windowSillMm", 500));
            RefusesOnly("windowSillMm", "rwr-stance", ("windowSillMm", 499));
            Accepts("rwr-stance", ("windowSillMm", 1249));
            RefusesOnly("windowSillMm", "rwr-stance", ("windowSillMm", 1250));
            Accepts("rwr-stance", ("windowTopMm", 1750));
            RefusesOnly("windowTopMm", "rwr-stance", ("windowTopMm", 1749));
            Accepts("rwr-stance", ("storeyMm", 3000), ("plateMm", 500), ("windowTopMm", 2500));
            RefusesOnly("windowTopMm", "rwr-stance", ("storeyMm", 3000), ("plateMm", 500), ("windowTopMm", 2501));
        }

        private static readonly int[] Sills = { 500, 501, 777, 900, 901, 1001, 1100, 1247, 1248, 1249, 1250, 1251, 1260 };
        private static readonly int[] Tops = { 1740, 1749, 1750, 1751, 1999, 2000, 2001, 2250, 2499, 2500 };

        // A terrain map whose window board (2 raw) and sill bottom (floor + 1 raw) differ by an odd amount, so every sill pads.
        private static MapDesc OddTerrain()
        {
            var heights = new int[5][];
            for (int j = 0; j < heights.Length; j++) heights[j] = new int[13];
            heights[1][5] = 2;
            return Desc("rwr-stance", 12, 4, heights);
        }

        // SC9 grid: every sill and top the relations accept builds as a ground window on a flat map and on the odd terrain
        // map, and as a window on a floor plate one storey down.
        [Test]
        public void SC9_EveryAcceptedWindowBuilds()
        {
            ContentPack pack = Shipped("rwr-stance");
            CombatBody body = pack.Body.Value;
            StanceDef[] stances = pack.BuildCombat().Stances.ToArray();
            MapDesc flat = Desc("rwr-stance", 12, 4, null), terrain = OddTerrain();
            int accepted = 0, refused = 0;
            foreach (int sill in Sills)
                foreach (int top in Tops)
                {
                    var s = new ScaleRules(3000, 500, 1000, 1400, 2500, sill, top, 1000, 175, 280, 3250, 2000, 3000);
                    if (ScaleRules.Problems(s, body, stances, 3, 4, null).Count > 0) { refused++; continue; }
                    accepted++;
                    long topRaw = s.StoreyRaw, floorTop = s.StoreyRaw;
                    string at = "sill " + sill + " top " + top;
                    Assert.DoesNotThrow(() => MapPrefabs.Window(flat, "w", new MapXZ(0, 1), true, 10, 4, 2, topRaw, "walls", "wall", body,
                        s.WindowSillRaw, s.WindowTopRaw), "flat " + at);
                    Assert.DoesNotThrow(() => MapPrefabs.Window(terrain, "w", new MapXZ(0, 1), true, 10, 4, 2, topRaw, "walls", "wall", body,
                        s.WindowSillRaw, s.WindowTopRaw), "terrain " + at);
                    Assert.DoesNotThrow(() => MapPrefabs.WindowOn(flat, "w", new MapXZ(0, 1), true, 10, 4, 2, floorTop, floorTop + s.StoreyRaw,
                        "walls", "wall", body, s.WindowSillRaw, s.WindowTopRaw), "plate " + at);
                }
            Assert.That(accepted, Is.GreaterThan(50));
            Assert.That(refused, Is.GreaterThan(10));
        }

        // The odd terrain map does pad: a sill at exactly min(E, M) - r is refused by the prefab there, not on the flat map.
        [Test]
        public void SC9_TheTerrainMapPadsTheSill()
        {
            CombatBody body = Shipped("rwr-stance").Body.Value;
            long sill = body.Eye.Raw - R;
            Assert.DoesNotThrow(() => MapPrefabs.Window(Desc("rwr-stance", 12, 4, null), "w", new MapXZ(0, 1), true, 10, 4, 2, 3 * M, "walls", "wall", body, sill));
            Assert.Throws<ArgumentException>(() => MapPrefabs.Window(OddTerrain(), "w", new MapXZ(0, 1), true, 10, 4, 2, 3 * M, "walls", "wall", body, sill));
        }

        // SC10: plate at least 0.5 m; storey minus plate at least the 2 m headroom; a padded storey is taken by MapBuilding.
        [Test]
        public void SC10_Storeys()
        {
            Accepts("rwr-stance", ("plateMm", 500));
            RefusesOnly("plateMm", "rwr-stance", ("plateMm", 499));
            Accepts("rwr-stance", ("storeyMm", 2500), ("plateMm", 500), ("windowTopMm", 1750));
            RefusesOnly("storeyMm", "rwr-stance", ("storeyMm", 2499), ("plateMm", 500), ("windowTopMm", 1750));
            ScaleRules s = Rules("rwr-stance", ("storeyMm", 3001));
            Assert.That(s.StoreyRaw, Is.EqualTo(196674));
            Assert.DoesNotThrow(() => new MapBuilding(new MapXZ(2, 2), new MapXZ(12, 8), 3, s.StoreyRaw, s.PlateRaw, 's', 2, 1, true, new MapXZ(4, 4), 6));
        }

        // SC11: the stair step obeys the layer's slope and 5/3.
        [Test]
        public void SC11_StairSlope()
        {
            Accepts("rwr-body", ("stairRiseMm", 300), ("stairRunMm", 180));
            RefusesOnly("stairRiseMm/stairRunMm", "rwr-body", ("stairRiseMm", 301), ("stairRunMm", 180));
            Accepts("rwr-stance", ("stairRiseMm", 210), ("stairRunMm", 280));
            RefusesOnly("stairRiseMm/stairRunMm", "rwr-stance", ("stairRiseMm", 211), ("stairRunMm", 280));
        }

        private static readonly MapBuilding Template = new MapBuilding(new MapXZ(2, 2), new MapXZ(12, 8), 1, 196608, 32768, 's', 2, 1, true, new MapXZ(4, 4), 1);

        private static MapBuilding With(ScaleRules s, int floors, int stair) =>
            new MapBuilding(Template.Cell, Template.Size, floors, s.StoreyRaw, s.PlateRaw, 's', 2, 1, true, Template.StairCell, stair);

        // SC11: StairCells on rwr-scale: two floors 5 (4 is steeper than the step), three floors 6 (5 stands too close over
        // the stair below), and the cases with no stair.
        [Test]
        public void SC11_StairCells()
        {
            ScaleRules s = Shipped("rwr-scale").SectionOrNull<ScaleRules>();
            Assert.That(Ed6aFixtures.NoThrow(() => s.StairCells(2)), Is.EqualTo(5));
            long riseOfFour = (s.StoreyRaw + 3) / 4;
            Assert.That(riseOfFour * s.StairRunMm, Is.GreaterThan((long)s.StairRiseMm * M), "4 cells are steeper than the standard step");
            Assert.That(Ed6aFixtures.NoThrow(() => s.StairCells(3)), Is.EqualTo(6));
            MapDesc flat = Desc("rwr-scale", 30, 20, null);
            Assert.DoesNotThrow(() => MapBuildings.Structure(flat, "b", With(s, 3, 6), 3, 4));
            Assert.That(Assert.Throws<ArgumentException>(() => MapBuildings.Structure(flat, "b", With(s, 3, 5), 3, 4)).Message,
                Does.StartWith("the stairs stand over each other"));

            Assert.Throws<ArgumentException>(() => Rules("rwr-stance", ("storeyMm", 2500), ("plateMm", 500), ("windowTopMm", 1750)).StairCells(3));
            Assert.Throws<ArgumentException>(() => Rules("rwr-stance", ("stairRiseMm", 1), ("stairRunMm", 100000)).StairCells(2));
            Assert.Throws<ArgumentException>(() => Rules("rwr-stance", ("stairRiseMm", 1), ("stairRunMm", 1000)).StairCells(2));
            ScaleRules steepest = Rules("rwr-body", ("stairRiseMm", 100000), ("stairRunMm", 100000));
            Assert.That(Ed6aFixtures.NoThrow(() => steepest.StairCells(2)), Is.EqualTo(3), "rise * 65536 needs a long");
            ScaleRules longest = Rules("rwr-stance", ("storeyMm", 3002), ("stairRiseMm", 60), ("stairRunMm", 10109));
            Assert.That(Ed6aFixtures.NoThrow(() => longest.StairCells(2)), Is.EqualTo(ScaleRules.MaxStairCells));
            Assert.That(ScaleRules.MaxStairCells, Is.EqualTo(508));
            Assert.Throws<ArgumentException>(() => Rules("rwr-stance", ("storeyMm", 3000), ("stairRiseMm", 60), ("stairRunMm", 10135)).StairCells(2));
        }

        [Test]
        public void SC11_StairCellsFloorRange()
        {
            ScaleRules s = ScaleRules.Default;
            Assert.That(Ed6aFixtures.NoThrow(() => s.StairCells(1)), Is.EqualTo(1));
            Assert.That(Ed6aFixtures.NoThrow(() => s.StairCells(MapBuilding.MaxFloors)), Is.EqualTo(6));
            Assert.Throws<ArgumentException>(() => s.StairCells(0));
            Assert.Throws<ArgumentException>(() => s.StairCells(MapBuilding.MaxFloors + 1));
        }

        // SC17: StandardBuilding on rwr-scale builds with 1 to 4 floors: storey, plate, a one-cell door and the stair cells.
        [TestCase(1, 1)]
        [TestCase(2, 5)]
        [TestCase(3, 6)]
        [TestCase(4, 6)]
        public void SC17_StandardBuildings(int floors, int stair)
        {
            MapDocument doc = Tall("rwr-scale");
            MapBuildingInfo b = doc.StandardBuilding(2, 2, 12, 8, floors, "s", 2, true, 4, 4);
            Assert.That(b.Floors, Is.EqualTo(floors));
            Assert.That(b.StoreyRaw, Is.EqualTo(196608));
            Assert.That(b.ThickRaw, Is.EqualTo(32768));
            Assert.That(b.DoorWidth, Is.EqualTo(1));
            Assert.That(b.StairLength, Is.EqualTo(stair));
            Assert.That((b.CellX, b.CellZ, b.SizeX, b.SizeZ, b.DoorSide, b.DoorAt, b.StairAlongX, b.StairX, b.StairZ),
                Is.EqualTo((2, 2, 12, 8, "s", 2, true, 4, 4)));
            MapBuildResult built = null;
            Assert.DoesNotThrow(() => built = doc.WithBuilding(null, b));
            Assert.That(built.Document.Buildings(built.BuildJson).Single().StairLength, Is.EqualTo(stair));
        }
    }
}
