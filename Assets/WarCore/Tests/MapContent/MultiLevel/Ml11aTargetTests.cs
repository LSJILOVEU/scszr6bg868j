using NUnit.Framework;
using WarCore.Battle;
using WarCore.Content.MapTools;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.Ml11aWorld;
using static WarCore.Tests.RespawnWorld;

namespace WarCore.Tests
{
    // ML-11a acceptance 16 (a centre on a cell nobody walks on), 17 (a base on such a cell), 11 (falling back to a point or to
    // the base) and 8d for a unit that falls back. A unit falls back when the test's engage part says so, which the scheduler
    // asks only of an armed unit that sees an enemy.
    public sealed class Ml11aTargetTests
    {
        private static ControlPointDef Ground(int id, long xMm, long zMm, SimFix radius, int owner = 0) =>
            new ControlPointDef(id, Flat(xMm, zMm), radius, owner);
        private static readonly SimVec3 West = Flat(30500, 30500), East = Flat(45500, 30500);

        // 16a. The ground under the stair (cells x 36..39, z 31..32) is blocked. A centre in cell (37, 31) is walked to at the
        // nearest walkable cell whose middle is in the circle: (37, 30), 1 m north, seven cells from (30, 30).
        [Test]
        public void ACentreOnABlockedCellIsWalkedToAtTheNearestCell()
        {
            M6World w = World(new[] { Walker(1, 30500, 30500) }, new[] { Ground(1, 37500, 31500, SimFix.FromInt(2)) });
            Assert.That(Cost(w, 0, West, 0), Is.EqualTo(7000L));
            Assert.That(Cost(w, 0, East, 0), Is.EqualTo(8000L));
            Assert.That(Pick(w, 1), Is.EqualTo(0));
        }

        // A centre on the line between cells (37, 31) and (38, 31): the middles of (37, 30) and (38, 30) are equally near; the
        // lower cell is taken, seven cells from (30, 30) and eight from (45, 30).
        [Test]
        public void EquallyNearCellsGoToTheLowerCell()
        {
            M6World w = World(new[] { Walker(1, 30500, 30500) }, new[] { Ground(1, 38000, 31500, SimFix.FromInt(2)) });
            Assert.That(Cost(w, 0, West, 0), Is.EqualTo(7000L));
            Assert.That(Cost(w, 0, East, 0), Is.EqualTo(8000L));
        }

        // 16b, 16c. The middle of (37, 30) is exactly 1 m from the centre: in a circle of 1 m, out of one of 0.9 m, and then
        // no cell is left and the point is reached from nowhere: no dispatch, no front line.
        [Test]
        public void TheCellsMiddleMustBeInTheCircle()
        {
            M6World on = World(new[] { Walker(1, 30500, 30500) }, new[] { Ground(1, 37500, 31500, SimFix.FromInt(1)) });
            Assert.That(Cost(on, 0, West, 0), Is.EqualTo(7000L));
            var points = new[] { Ground(1, 37500, 31500, SimFix.Ratio(9, 10)), PointDown(2, 20500, 30500, 1, 1) };
            M6World off = World(new[] { Walker(1, 30500, 30500) }, points);
            Assert.That(Cost(off, 0, West, 0), Is.EqualTo(-1L));
            Assert.That(Pick(off, 1), Is.EqualTo(-1));
            Assert.That(Field(off).Frontline(1), Is.EqualTo(-1));
        }

        // A centre on a walkable cell is walked to at that cell, however near another one is.
        [Test]
        public void AWalkableCentreCellIsTheTarget()
        {
            M6World w = World(new[] { Walker(1, 30500, 30500) }, new[] { Ground(1, 37950, 30950, SimFix.FromInt(2)) });
            Assert.That(Cost(w, 0, West, 0), Is.EqualTo(7000L));
            Assert.That(Cost(w, 0, Flat(38500, 30500), 0), Is.EqualTo(1000L));
        }

        // 16a upstairs. A wall stands on the floor's cell (43, 33). The cells (43, 32) and (42, 33) are equally near; the lower
        // one is (43, 32). From the floor's (40, 31): a diagonal to (41, 32) (the cutout bars the other way) and two steps east.
        [Test]
        public void ACentreUnderAWallUpstairsIsWalkedToOnItsLayer()
        {
            var point = new ControlPointDef(1, Up(43500, 33500), SimFix.Ratio(3, 2), 0, false, default, 0, 1);
            M6World w = World(new[] { Walker(1, 30500, 31500) }, new[] { point }, capture: 30, map: Walled());
            Assert.That(Cost(w, 0, Up(40500, 31500), 1), Is.EqualTo(3414L));
            Assert.That(Cost(w, 0, Up(43500, 33500), 1), Is.EqualTo(-1L), "nobody stands under the wall");
            // From the ground (30, 31): five steps, a seam, three stair cells, a seam, then the 3414 above.
            Assert.That(Cost(w, 0, Flat(30500, 31500), 0), Is.EqualTo(13414L));
            Assert.That(Pick(w, 1), Is.EqualTo(0));
            bool failed = false;
            for (int t = 0; t < 400 && w.Capture.Points[0].Owner == 0; t++) { w.Step(); failed |= Failed(w, 1); }
            Assert.That(w.Capture.Points[0].Owner, Is.EqualTo(1), "the soldier walks up and takes it");
            Assert.That(failed, Is.False);
        }

        // 17a. Team 1's base stands on the blocked cell (37, 31): no point is walked to from there, so a spread ranks from each
        // unit, like a team without a base. From ground (30, 30): the west point at (25, 30) costs 5000, the east at (45, 30) 15000.
        [Test]
        public void ASpreadFromABaseThatReachesNothingRanksFromTheUnit()
        {
            var units = new System.Collections.Generic.List<UnitSpawn>();
            for (ulong key = 1; key <= 6; key++) units.Add(Walker(key, 30500, 30500));
            M6World w = World(units, new[] { PointDown(1, 45500, 30500), PointDown(2, 25500, 30500) }, bases: Bases(new SpawnAnchor(1, Flat(37500, 31500))));
            Install(w, "dispatch", new NearestDispatch(0, 2));
            int ones = 0;
            for (ulong key = 1; key <= 6; key++)
            {
                ones += Rank(w, key, 2);
                Assert.That(Pick(w, key), Is.EqualTo(Rank(w, key, 2) == 0 ? 1 : 0), "unit " + key);
            }
            Assert.That(ones, Is.InRange(1, 5), "the fixture has both ranks");
        }

        private static M6World Fleeing(UnitSpawn[] units, ControlPointDef[] points, SpawnAnchor[] bases = null, bool stair = true, byte[] map = null)
        {
            M6World w = World(units, points, stair: stair, capture: 100000, bases: bases, map: map);
            Install(w, "engage", new Flee());
            Install(w, "dispatch", new Sent());
            return w;
        }
        private static readonly ControlPointDef Far = PointDown(9, 70500, 5500);

        // 11. A unit that falls back goes to the owned point it walks to soonest: the ground point at 10000, not the point right
        // above it at 13414; and never to a point it cannot walk to.
        [TestCase(true)]
        [TestCase(false)]
        public void AUnitFallsBackToTheOwnedPointItWalksToSoonest(bool stair)
        {
            var points = new[] { PointUp(1, 41500, 32500, 1, 1), PointDown(2, 51500, 32500, 1, 1) };
            M6World w = Fleeing(new[] { Gunner(1, 41500, 32500), Still(10, 45500, 32500) }, points, stair: stair);
            w.Step(12);
            Assert.That(Ordered(w, 1, out SimVec3 target, out int layer), Is.True);
            Assert.That(layer, Is.EqualTo(0));
            Assert.That(Slots(w).TryFindAll(0, target, 1, out _), Is.True, "a slot of the ground point");
            for (int t = 0; t < 200; t++) { w.Step(); Assert.That(Failed(w, 1), Is.False, "tick " + t); }
        }

        // 11. The cost is read from the unit's layer. A unit upstairs at the floor's (43, 30), an owned point on the ground right
        // under it and one on the floor at (40, 33). From the floor: two steps, a diagonal and two steps to the point upstairs
        // (the cutout bars the straight diagonals); to the ground point 3414 to the stair's top, a seam, three stair cells, a
        // seam, one step north and eight east. The unit falls back to the point upstairs.
        [Test]
        public void AUnitUpstairsFallsBackByTheCostFromItsFloor()
        {
            var points = new[] { PointUp(1, 40500, 33500, 1, 1), PointDown(2, 43500, 30500, 1, 1) };
            M6World w = Fleeing(new[] { Gunner(1, 43500, 30500, 1), Still(10, 43500, 33500, 1) }, points);
            Assert.That((Cost(w, 0, Up(43500, 30500), 1), Cost(w, 1, Up(43500, 30500), 1)), Is.EqualTo((5414L, 17414L)));
            Assert.That((Cost(w, 0, Flat(43500, 30500), 0), Cost(w, 1, Flat(43500, 30500), 0)), Is.EqualTo((15414L, 0L)));
            w.Step(12);
            Assert.That(Ordered(w, 1, out SimVec3 target, out int layer), Is.True);
            Assert.That(layer, Is.EqualTo(1));
            Assert.That(Slots(w).TryFindAll(1, target, 0, out _), Is.True, "a slot of the point upstairs");
        }

        // 1. Without plates the walk home is not checked, as before: the base is 0.1 m from a wall, where a soldier's footprint
        // does not fit, and the unit is still ordered there.
        [Test]
        public void WithoutPlatesTheWalkHomeIsNotChecked()
        {
            byte[] map = Ml7Worlds.Bytes(Ml4Maps.Make(0, 80, 40, null, new MapPlate[0], null, new[] { Ml4Maps.Wall(1, 30, 20, 1, 1, 2 * M) }));
            M6World w = Fleeing(new[] { Gunner(1, 41500, 20500), Still(10, 45500, 20500) }, new[] { Far }, Bases(new SpawnAnchor(1, Flat(31100, 20500))), map: map);
            Assert.That(Part<IUnitLayers>(w, "layers"), Is.Null);
            Assert.That(Moves(w).CanStandAt(Flat(31100, 20500), 0), Is.False);
            w.Step(12);
            Assert.That(Ordered(w, 1, out SimVec3 target, out int layer), Is.True);
            Assert.That((target, layer), Is.EqualTo((Flat(31100, 20500), 0)));
        }

        // 11. Without an owned point the unit falls back to its base. Upstairs the order carries the base's layer; a unit under
        // the base has not arrived, a unit at it has.
        [Test]
        public void AUnitFallsBackToItsBaseOnTheBasesLayer()
        {
            SpawnAnchor[] bases = Bases(new SpawnAnchor(1, Up(41500, 32500), 1));
            M6World under = Fleeing(new[] { Gunner(1, 41500, 32500), Still(10, 45500, 32500) }, new[] { Far }, bases);
            under.Step(12);
            Assert.That(Ordered(under, 1, out SimVec3 target, out int layer), Is.True, "under the base is not at the base");
            Assert.That((target, layer), Is.EqualTo((Up(41500, 32500), 1)));

            M6World at = Fleeing(new[] { Gunner(1, 41500, 32500, 1), Still(10, 43500, 33500, 1) }, new[] { Far }, bases);
            for (int t = 0; t < 30; t++) { at.Step(); Assert.That(Ordered(at, 1), Is.False, "at the base, tick " + t); }

            // Walking on the ground to the place under the base is not being on the way to the base.
            M6World walking = Fleeing(new[] { Gunner(1, 44500, 32500), Still(10, 46500, 32500) }, new[] { Far }, bases);
            Moves(walking).Order(Id(walking, 1), Flat(41500, 32500), 0);
            walking.Step(12);
            Assert.That(Ordered(walking, 1, out target, out layer), Is.True);
            Assert.That((target, layer), Is.EqualTo((Up(41500, 32500), 1)));
        }

        // A base at the floor's edge, where a soldier's footprint does not fit: the unit stands (an order there would be moved
        // to the cell's middle and never read as the base).
        [Test]
        public void AUnitDoesNotFallBackToABaseItCannotStandOn()
        {
            SpawnAnchor[] bases = Bases(new SpawnAnchor(1, Up(41500, 30100), 1));
            M6World w = Fleeing(new[] { Gunner(1, 41500, 32500, 1), Still(10, 43500, 33500, 1) }, new[] { Far }, bases);
            Assert.That(Moves(w).CanStandAt(Up(41500, 30100), 1), Is.False);
            Assert.That(Moves(w).CanStandAt(Up(41500, 30500), 1), Is.True);
            for (int t = 0; t < 60; t++) { w.Step(); Assert.That(Ordered(w, 1), Is.False, "tick " + t); }
        }

        // The base is the team's first anchor: on the ground here; the second, on the floor without a stair, plays no part.
        [Test]
        public void TheBaseIsTheTeamsFirstAnchor()
        {
            SpawnAnchor[] bases = Bases(new SpawnAnchor(1, Flat(30500, 20500)), new SpawnAnchor(1, Up(41500, 32500), 1));
            M6World w = Fleeing(new[] { Gunner(1, 41500, 20500), Still(10, 45500, 20500) }, new[] { Far }, bases, stair: false);
            w.Step(12);
            Assert.That(Ordered(w, 1, out SimVec3 target, out int layer), Is.True);
            Assert.That((target, layer), Is.EqualTo((Flat(30500, 20500), 0)));
        }

        // 17b, 8d. A base nobody walks to (on the blocked cell), and a base on the ground for a unit on a floor without a stair:
        // the unit stands; no order, so no failed walk. The unit on the ground beside it does fall back.
        [Test]
        public void AUnitDoesNotFallBackToABaseItCannotWalkTo()
        {
            M6World blocked = Fleeing(new[] { Gunner(1, 30500, 30500), Still(10, 30500, 25500) }, new[] { Far }, Bases(new SpawnAnchor(1, Flat(37500, 31500))));
            for (int t = 0; t < 60; t++) { blocked.Step(); Assert.That(Ordered(blocked, 1), Is.False, "tick " + t); }

            var units = new[] { Gunner(1, 41500, 32500, 1), Still(10, 43500, 33500, 1), Gunner(2, 41500, 20500), Still(11, 45500, 20500) };
            M6World cut = Fleeing(units, new[] { Far }, Bases(new SpawnAnchor(1, Flat(30500, 20500))), stair: false);
            bool second = false;
            for (int t = 0; t < 300; t++)
            {
                cut.Step();
                Assert.That(Ordered(cut, 1) || Failed(cut, 1), Is.False, "tick " + t);
                second |= Ordered(cut, 2);
            }
            Assert.That(second, Is.True, "the unit on the ground walks back");
            Assert.That((Layer(cut, 1), At(cut, 1)), Is.EqualTo((1, Up(41500, 32500))));
        }
    }
}
