using System;
using System.Linq;
using NUnit.Framework;
using WarCore.Content.MapTools;
using WarCore.Sim;
using static WarCore.Tests.Trv1aMaps;

namespace WarCore.Tests
{
    // TRV-1a: the ramp prefab (T1-01 to T1-07, T1-18 to T1-20, T1-22). Expected values are worked by hand in each comment.
    public sealed class Trv1aRampTests
    {
        private static MapDesc Flat16() => Flat(16, 8);

        private static void Shape(MapPlate p, int x, int z, int w, int d, string axis, long from, long to, long thick)
        {
            Assert.That((p.Cell.X, p.Cell.Z, p.Size.X, p.Size.Z), Is.EqualTo((x, z, w, d)), "cell and size");
            Assert.That(p.YRaw, Is.Null);
            Assert.That((p.Slope.Axis, p.Slope.FromRaw, p.Slope.ToRaw), Is.EqualTo((axis, from, to)), "slope");
            Assert.That(p.ThickRaw, Is.EqualTo(thick), "thickness");
        }

        private static ArgumentException Refused(TestDelegate call, string words)
        {
            ArgumentException e = Assert.Throws<ArgumentException>(call);
            StringAssert.Contains(words, e.Message);
            return e;
        }

        // T1-01: 0.9 m over flat ground at 3/4: one cell rises 58982 > 49152; two cells rise ceil(58982 / 2) = 29491 each.
        [Test]
        public void ADoorwayNinetyCentimetresUpTakesTwoCells()
        {
            MapStampPlan plan = Ramp(Flat16(), 6, 2, 2, MapSide.PlusX, 58982);
            Assert.That(plan.Instance, Is.EqualTo("ramp"));
            Assert.That(plan.Plates.Select(e => e.Src), Is.EqualTo(new ulong[] { 1 }));
            Assert.That(plan.Walls.Count + plan.Points.Count + plan.Bases.Count + plan.Units.Count + plan.Cutouts.Count + plan.Heights.Count + plan.Surfaces.Count, Is.Zero);
            Shape(Plate(plan), 6, 2, 2, 2, "x", 58982, 0, 58982);
            Assert.That(Points(Plate(plan)), Is.EqualTo(new long[] { 58982, 29491, 0 }));
        }

        // T1-02, T1-03: the slope bound is inclusive: 4 x 49152 = 3 x 65536.
        [TestCase(49152, 1)]
        [TestCase(49153, 2)]
        public void TheSlopeBoundIsInclusive(long door, int cells) =>
            Assert.That(Plate(Ramp(Flat16(), 6, 2, 2, MapSide.PlusX, door)).Size.X, Is.EqualTo(cells));

        // T1-04, T1-05: the far edge's points (7, 2) = 0 and (7, 3) = 19660 or 19661; drop 30000 - 19660 fits one cell at 3/4.
        [Test]
        public void TheFootTakesTheHigherPointWithinAStep()
        {
            MapDesc d = Ground(16, 8, (i, j) => i == 7 && j == 3 ? Step : 0, 10 * M);
            Shape(Plate(Ok(() => Ramp(d, 6, 2, 1, MapSide.PlusX, 30000))), 6, 2, 1, 1, "x", 30000, Step, 32768);
            MapDesc uneven = Ground(16, 8, (i, j) => i == 7 && j == 3 ? Step + 1 : 0, 10 * M);
            Refused(() => Ramp(uneven, 6, 2, 1, MapSide.PlusX, 30000), "differs too much");
        }

        // T1-06: only the doorway edge can be above the plate (each line nearer than the foot failed the slope test, so the
        // ground there is below the plate's line); (6, 3) at the door top is legal, 1 raw above is buried.
        [Test]
        public void TheRampIsNeverBuried()
        {
            Assert.DoesNotThrow(() => Ramp(Ground(16, 8, (i, j) => i == 6 && j == 3 ? 58982 : 0, 10 * M), 6, 2, 2, MapSide.PlusX, 58982));
            Refused(() => Ramp(Ground(16, 8, (i, j) => i == 6 && j == 3 ? 58983 : 0, 10 * M), 6, 2, 2, MapSide.PlusX, 58982), "below the ground");
        }

        // T1-07: leaving the map at any length tried, a foot on the map's edge (no ground cell outside it to step from), and
        // the stack clearance to another plate (bottom of the upper plate at least 2 m above the ramp's highest corner in a
        // shared cell: 58982 + 131072 + 32768 = 222822 for a 0.5 m plate).
        [Test]
        public void TheRampStaysOnTheMapAndClearOfPlates()
        {
            Refused(() => Ramp(Flat16(), 15, 2, 2, MapSide.PlusX, 58982), "map edge");   // one cell already ends on x = 16
            Refused(() => Ramp(Flat16(), 16, 2, 2, MapSide.PlusX, 58982), "leaves the map");
            Refused(() => Ramp(Flat16(), 6, 7, 2, MapSide.PlusX, 58982), "leaves the map");
            Refused(() => Ramp(Flat16(), 0, 2, 2, MapSide.MinusX, 58982), "map edge");   // one cell already ends on x = 0
            Refused(() => Ramp(Flat16(), 6, -1, 2, MapSide.PlusX, 58982), "leaves the map");
            Refused(() => Ramp(Flat16(), 14, 2, 2, MapSide.PlusX, 58982), "map edge");   // foot on x = 16
            Refused(() => Ramp(Flat16(), 1, 2, 2, MapSide.MinusX, 58982), "map edge");   // foot on x = 0
            Refused(() => Ramp(Flat16(), 6, 6, 2, MapSide.PlusZ, 58982), "map edge");    // foot on z = 8
            Assert.That(Plate(Ok(() => Ramp(Flat16(), 13, 2, 2, MapSide.PlusX, 58982))).Cell.X, Is.EqualTo(13), "foot on x = 15, one cell in");
            Assert.That(Plate(Ok(() => Ramp(Flat16(), 2, 2, 2, MapSide.MinusX, 58982))).Cell.X, Is.EqualTo(1), "foot on x = 1");
            Assert.That(Plate(Ok(() => Ramp(Flat16(), 6, 5, 2, MapSide.PlusZ, 58982))).Cell.Z, Is.EqualTo(5), "foot on z = 7");
            Refused(() => Ramp(Flat(16, 8, Ml4Maps.Flat(1, 6, 2, 2, 2, 222821)), 6, 2, 2, MapSide.PlusX, 58982), "plate 1");
            Assert.DoesNotThrow(() => Ramp(Flat(16, 8, Ml4Maps.Flat(1, 6, 2, 2, 2, 222822)), 6, 2, 2, MapSide.PlusX, 58982));
        }

        [Test]
        public void EachRefusalSaysWhy()
        {
            Assert.That(Assert.Throws<ArgumentException>(() => Ramp(Flat16(), 6, 2, 0, MapSide.PlusX, 58982)).ParamName, Is.EqualTo("width"));
            Refused(() => Ramp(Flat16(), 6, 2, 2, MapSide.PlusX, 0), "no drop");
            Assert.DoesNotThrow(() => Ramp(Flat16(), 6, 2, 2, MapSide.PlusX, 1));
            Refused(() => Ramp(Flat16(), 6, 2, 2, MapSide.PlusX, 10 * M + 1), "ceiling");
            Assert.That(Plate(Ok(() => Ramp(Flat(32, 8), 6, 2, 2, MapSide.PlusX, 10 * M))).Size.X, Is.EqualTo(14), "a door at the ceiling: ceil(655360 / 49152) = 14 cells");
            Assert.Throws<ArgumentOutOfRangeException>(() => Ramp(Flat16(), 6, 2, 2, (MapSide)4, 58982));
            // The description's height limit is 2^29 - 1 = 536870911: a doorway 1 raw above it is refused even under a
            // higher ceiling; ground at -536870911 one cell out makes a 2-cell ramp (foot at 536000000) thicker than it.
            MapDesc high = Ml4Maps.Make(536870911, 16, 8, null, new MapPlate[0], ceilingRaw: 536936447);
            Assert.Throws<ArgumentOutOfRangeException>(() => Ramp(high, 6, 2, 2, MapSide.PlusX, 536870912));
            MapDesc pit = Ground(16, 8, (i, j) => i == 7 ? -536870911 : 536000000, 537000000);
            Refused(() => Ramp(pit, 6, 2, 2, MapSide.PlusX, 536058982), "thicker");
            Assert.Throws<ArgumentOutOfRangeException>(() => Ramp(Flat16(), 6, 2, 2, MapSide.PlusX, 58982, 3, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => Ramp(Flat16(), 6, 2, 2, MapSide.PlusX, 58982, 3, 4097));
            Assert.Throws<ArgumentOutOfRangeException>(() => Ramp(Flat16(), 6, 2, 2, MapSide.PlusX, 58982, -1, 4));
            Assert.Throws<ArgumentOutOfRangeException>(() => Ramp(Flat16(), 6, 2, 2, MapSide.PlusX, 58982, 9, 4));
            Assert.Throws<ArgumentNullException>(() => MapPrefabs.Ramp(null, "ramp", new MapXZ(6, 2), 2, MapSide.PlusX, 58982, 3, 4));
        }

        // A ditch: the line one cell out is 0 (too steep), the line two cells out is back at the doorway's height.
        [Test]
        public void GroundThatRisesBackToTheDoorwayIsRefused() =>
            Refused(() => Ramp(Ground(16, 8, (i, j) => i == 8 ? 58982 : 0, 10 * M), 6, 2, 2, MapSide.PlusX, 58982), "rises back");

        // The point table always counts from the plate's minimum edge: towards -x and -z the low end is "from".
        [Test]
        public void EverySideBuildsItsOwnPointTable()
        {
            Shape(Plate(Ramp(Flat16(), 6, 2, 2, MapSide.PlusX, 58982)), 6, 2, 2, 2, "x", 58982, 0, 58982);
            Shape(Plate(Ramp(Flat16(), 9, 2, 2, MapSide.MinusX, 58982)), 8, 2, 2, 2, "x", 0, 58982, 58982);
            Shape(Plate(Ramp(Flat16(), 6, 2, 2, MapSide.PlusZ, 58982)), 6, 2, 2, 2, "z", 58982, 0, 58982);
            Shape(Plate(Ramp(Flat16(), 6, 5, 2, MapSide.MinusZ, 58982)), 6, 4, 2, 2, "z", 0, 58982, 58982);
            Shape(Plate(Ramp(Flat16(), 6, 2, 1, MapSide.PlusZ, 58982)), 6, 2, 1, 2, "z", 58982, 0, 58982);
            Shape(Plate(Ramp(Flat16(), 9, 2, 1, MapSide.MinusX, 58982)), 8, 2, 2, 1, "x", 0, 58982, 58982);
            Assert.That(Points(Plate(Ramp(Flat16(), 9, 2, 2, MapSide.MinusX, 58982))), Is.EqualTo(new long[] { 0, 29491, 58982 }));
        }

        // Stamping the same instance and part again replaces its plate, so the clearance check leaves that plate out; another
        // instance, or another part of the same one, on the same cells is too close to it.
        [Test]
        public void TheSameStampSourceMayBeStampedAgain()
        {
            MapDesc once = Stamp(Flat16(), Ramp(Flat16(), 6, 2, 1, MapSide.PlusX, 58982));
            MapDesc twice = Stamp(once, Ok(() => Ramp(once, 6, 2, 1, MapSide.PlusX, 58982)));
            Assert.That(twice.Plates.Count, Is.EqualTo(1));
            Assert.That(Ml4Maps.Gate(twice), Is.Empty);
            ulong id = once.Plates.Single().Id;
            Refused(() => MapPrefabs.Ramp(once, "other", new MapXZ(6, 2), 1, MapSide.PlusX, 58982, 3, 4), "plate " + id);
            Refused(() => MapPrefabs.Ramp(once, "ramp", new MapXZ(6, 2), 1, MapSide.PlusX, 58982, 3, 4, 2), "plate " + id);
        }

        [Test]
        public void ThePartNumberIsTheSource() =>
            Assert.That(MapPrefabs.Ramp(Flat16(), "r", new MapXZ(6, 2), 2, MapSide.PlusX, 58982, 3, 4, 57).Plates.Single().Src, Is.EqualTo(57ul));

        // T1-18: ground falls 6554 a cell going out, 58982 below the doorway at its edge (h = 6554 (22 - x), door 104864 + 58982).
        // One cell: drop 65536 > 49152. Two: drop 72090, ceil(72090 / 2) = 36045 <= 49152. Thickness 163846 - 91756.
        // At 70 % (45875 a cell) the drop after L cells is 58982 + 45875 L; ceil(drop / L) <= 49152 needs L >= 18 > 16.
        [Test]
        public void DownhillTheFootIsMeasuredAgainAtEveryLength()
        {
            MapDesc d = Ground(32, 8, (i, j) => 6554L * Math.Max(0, 22 - i), 10 * M);
            Shape(Plate(Ramp(d, 6, 2, 2, MapSide.PlusX, 163846)), 6, 2, 2, 2, "x", 163846, 91756, 72090);
            // At 1/16 a cell rises at most 4096: 65536 needs exactly 16 cells (15 would rise 4370 somewhere).
            Assert.That(Plate(Ok(() => Ramp(Flat(32, 8), 6, 2, 2, MapSide.PlusX, 65536, 1, 16))).Size.X, Is.EqualTo(16));
            Refused(() => Ramp(Flat(32, 8), 6, 2, 2, MapSide.PlusX, 65537, 1, 16), "more than 16 cells");   // needs 17
            Assert.That(MapPrefabs.MaxRampCells, Is.EqualTo(16));
            MapDesc steep = Ground(32, 4, (i, j) => 45875L * Math.Max(0, 23 - i), 2000000);
            Refused(() => Ramp(steep, 6, 1, 2, MapSide.PlusX, 45875L * 17 + 58982), "more than 16 cells");
        }

        // T1-19: a pack steeper than 5/3 is held to 5/3: 111411 > 109226.67 needs two cells; 109226 fits one (3 x 109226 <= 5 x 65536).
        [TestCase(111411, 2)]
        [TestCase(109226, 1)]
        public void ASteepPackIsHeldToFiveThirds(long door, int cells) =>
            Assert.That(Plate(Ramp(Flat16(), 6, 2, 2, MapSide.PlusX, door, 2, 1)).Size.X, Is.EqualTo(cells));

        // T1-22 (and the review's case): the steepest cell rises ceil(drop / L). 327680 over 3 is 109227 in some cell
        // (3 x 109227 > 5 x 65536), so 4; 218453 over 2 is 109227 in one cell, so 3. The average would allow 3 and 2.
        [TestCase(327680, 4)]
        [TestCase(218453, 3)]
        public void EveryCellIsWithinTheSlope(long door, int cells)
        {
            MapStampPlan plan = Ramp(Flat16(), 6, 2, 2, MapSide.PlusX, door, 2, 1);
            Assert.That(Plate(plan).Size.X, Is.EqualTo(cells));
            long[] points = Points(Plate(plan));
            Assert.That(Enumerable.Range(0, cells).All(k => 3 * (points[k] - points[k + 1]) <= 5 * M), "every cell within 5/3");
            Assert.That(Ml4Maps.Gate(Stamp(Flat16(), plan)), Is.Empty, "the gate takes the ramp");
        }

        // T1-20: the underside is never above the lowest ground point under the ramp, and the plate is at least 0.5 m thick.
        [Test]
        public void TheRampIsSolidDownToTheGround()
        {
            Assert.That(Plate(Ramp(Flat16(), 6, 2, 2, MapSide.PlusX, 19661)).ThickRaw, Is.EqualTo(32768), "0.3 m drop: the minimum");
            Assert.That(Plate(Ramp(Flat16(), 6, 2, 2, MapSide.PlusX, 58982)).ThickRaw, Is.EqualTo(58982), "0.9 m drop: down to the ground");
            // The foot line (x = 8) is raised to 19660, the line between stays at 0: one cell is too steep (58982), two fit
            // (ceil(39322 / 2) = 19661); the underside goes down to 0 under the middle, not to the foot.
            MapDesc dip = Ground(16, 8, (i, j) => i == 8 ? Step : 0, 10 * M);
            Shape(Plate(Ramp(dip, 6, 2, 2, MapSide.PlusX, 58982)), 6, 2, 2, 2, "x", 58982, Step, 58982);
        }
    }
}
