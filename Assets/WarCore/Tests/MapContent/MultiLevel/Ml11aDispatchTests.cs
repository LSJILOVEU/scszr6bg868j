using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.Ml11aWorld;
using static WarCore.Tests.RespawnWorld;

namespace WarCore.Tests
{
    // ML-11a acceptance 7 (points by walking cost), 8a-8c and 8e (points that cannot be walked to) and 11 (front line): the
    // default dispatch's exact answers on the house. Costs are hand-walked: 1000 a straight step or seam, 1414 a diagonal.
    public sealed class Ml11aDispatchTests
    {
        private static readonly SpawnAnchor[] NoBaseOfOurs = { new SpawnAnchor(2, P(78, 20)) };

        // 7a-7c. The soldier stands on the ground right under the point upstairs. Up the stair it is 13414 away: a diagonal to
        // (40, 33), five steps west along z = 33, a step to the stair's foot at (35, 32), a seam, three stair cells, a seam and
        // one floor cell. A ground point ten cells east costs 10000 and wins; fourteen cells east costs 14000 and loses.
        [TestCase(51500, 32500, 10000, 1)]
        [TestCase(55500, 32500, 14000, 0)]
        public void TheNearerPointIsTheOneWalkedToSooner(long x, long z, long cost, int pick)
        {
            M6World w = World(new[] { Walker(1, 41500, 32500) }, new[] { PointUp(1, 41500, 32500), PointDown(2, x, z) });
            Assert.That(Cost(w, 0, Flat(41500, 32500), 0), Is.EqualTo(13414L));
            Assert.That(Cost(w, 1, Flat(41500, 32500), 0), Is.EqualTo(cost));
            Assert.That(Pick(w, 1), Is.EqualTo(pick));
        }

        // 7c. Thirteen cells east and one north is twelve straight steps and a diagonal: 13414, a tie, to the lower index.
        [TestCase(true)]
        [TestCase(false)]
        public void EqualCostsGoToTheLowerIndex(bool upFirst)
        {
            ControlPointDef up = PointUp(1, 41500, 32500), down = PointDown(2, 54500, 33500);
            M6World w = World(new[] { Walker(1, 41500, 32500) }, upFirst ? new[] { up, down } : new[] { down, up });
            Assert.That(Cost(w, 0, Flat(41500, 32500), 0), Is.EqualTo(13414L));
            Assert.That(Cost(w, 1, Flat(41500, 32500), 0), Is.EqualTo(13414L));
            Assert.That(Pick(w, 1), Is.EqualTo(0));
        }

        // The same place one layer up is on the point: cost 0.
        [Test]
        public void TheCostIsReadOnTheUnitsLayer()
        {
            M6World w = World(new[] { Walker(1, 41500, 32500, 1) }, new[] { PointDown(2, 51500, 32500), PointUp(1, 41500, 32500) });
            Assert.That(Cost(w, 1, Up(41500, 32500), 1), Is.EqualTo(0L));
            Assert.That(Cost(w, 0, Up(41500, 32500), 1), Is.EqualTo(13414L + 10000));
            Assert.That(Pick(w, 1), Is.EqualTo(1));
        }

        // 7d. Spread over two points, ranked from an origin upstairs at cell (41, 32) of the floor. West point at ground (33, 31):
        // a diagonal to (40, 31), a seam, three stair cells, a seam, two steps = 8414. East point at ground (48, 32): a step to
        // (40, 32), a seam, three stair cells, a seam to (35, 32), a step to (35, 33), twelve east along z = 33, a diagonal = 20414.
        // From the ground under the origin it is the other way round: east 7000, west 9242 (by z = 33). So an origin read on the
        // wrong layer swaps the ranks.
        private static readonly ControlPointDef West = PointDown(2, 33500, 31500), East = PointDown(3, 48500, 32500);
        private static IEnumerable<UnitSpawn> Six()
        {
            for (ulong key = 1; key <= 6; key++) yield return Walker(key, 20500 + (long)key * 1000, 20500);
        }
        private static void Spread(M6World w) => Install(w, "dispatch", new NearestDispatch(0, 2));
        private static void BothRanks(M6World w, ulong first, ulong last)
        {
            int ones = 0, count = 0;
            for (ulong key = first; key <= last; key++) { ones += Rank(w, key, 2); count++; }
            Assert.That(ones, Is.InRange(1, count - 1), "the fixture has both ranks");
        }

        [Test]
        public void ASpreadFromTheFrontLineReadsItsLayer()
        {
            M6World w = World(Six(), new[] { PointUp(1, 41500, 32500, 1, 1), West, East });
            Spread(w);
            Assert.That(Field(w).Frontline(1), Is.EqualTo(0));
            Assert.That((Cost(w, 1, Up(41500, 32500), 1), Cost(w, 2, Up(41500, 32500), 1)), Is.EqualTo((8414L, 20414L)));
            Assert.That((Cost(w, 1, Flat(41500, 32500), 0), Cost(w, 2, Flat(41500, 32500), 0)), Is.EqualTo((9242L, 7000L)));
            BothRanks(w, 1, 6);
            for (ulong key = 1; key <= 6; key++) Assert.That(Pick(w, key), Is.EqualTo(Rank(w, key, 2) == 0 ? 1 : 2), "unit " + key);
        }

        [Test]
        public void ASpreadFromTheBaseReadsItsLayer()
        {
            M6World w = World(Six(), new[] { West, East }, bases: Bases(new SpawnAnchor(1, Up(41500, 32500), 1)));
            Spread(w);
            Assert.That(Field(w).Frontline(1), Is.EqualTo(-1));
            BothRanks(w, 1, 6);
            for (ulong key = 1; key <= 6; key++) Assert.That(Pick(w, key), Is.EqualTo(Rank(w, key, 2)), "unit " + key);
        }

        // Without a base of its own the team ranks from each unit: upstairs units and units under them answer the other way round.
        [Test]
        public void ASpreadFromTheUnitReadsItsLayer()
        {
            var units = new List<UnitSpawn>();
            for (ulong key = 1; key <= 4; key++) units.Add(Walker(key, 41500, 32500, 1));
            for (ulong key = 5; key <= 8; key++) units.Add(Walker(key, 41500, 32500));
            M6World w = World(units, new[] { West, East }, bases: NoBaseOfOurs);
            Spread(w);
            BothRanks(w, 1, 4); BothRanks(w, 5, 8);
            for (ulong key = 1; key <= 4; key++) Assert.That(Pick(w, key), Is.EqualTo(Rank(w, key, 2)), "upstairs unit " + key);
            for (ulong key = 5; key <= 8; key++) Assert.That(Pick(w, key), Is.EqualTo(1 - Rank(w, key, 2)), "ground unit " + key);
        }

        // 7e. Every unit holds: the front-line point when the unit can walk to it.
        [Test]
        public void AHolderGoesToTheFrontLine()
        {
            M6World w = World(Six(), new[] { West, PointUp(1, 41500, 32500, 1, 1), East });
            Install(w, "dispatch", new NearestDispatch(1000, 1));
            Assert.That(Field(w).Frontline(1), Is.EqualTo(1));
            for (ulong key = 1; key <= 6; key++) Assert.That(Pick(w, key), Is.EqualTo(1), "unit " + key);
        }

        // 8a, 8b. Without the stair nothing walks up: the point upstairs is passed over, and alone it leaves nothing to go for.
        [Test]
        public void APointNobodyWalksToIsPassedOver()
        {
            M6World w = World(new[] { Walker(1, 41500, 32500) }, new[] { PointUp(1, 41500, 32500), PointDown(2, 51500, 32500) }, stair: false);
            Assert.That(Cost(w, 0, Flat(41500, 32500), 0), Is.EqualTo(-1L));
            Assert.That(Pick(w, 1), Is.EqualTo(1));
            M6World alone = World(new[] { Walker(1, 41500, 32500) }, new[] { PointUp(1, 41500, 32500) }, stair: false);
            Assert.That(Pick(alone, 1), Is.EqualTo(-1));
        }

        // A place that is no walkable cell of its layer reaches nothing: the cutout, off the plate, off the map, the air layer.
        [Test]
        public void APlaceThatIsNoCellReachesNothing()
        {
            M6World w = World(new[] { Walker(1, 41500, 32500) }, new[] { PointUp(1, 41500, 32500), PointDown(2, 51500, 32500) });
            Assert.That(Cost(w, 0, Up(42500, 31500), 1), Is.EqualTo(-1L), "the cutout");
            Assert.That(Cost(w, 0, Up(45500, 31500), 1), Is.EqualTo(-1L), "off the plate");
            Assert.That(Cost(w, 0, Flat(-500, 31500), 0), Is.EqualTo(-1L), "off the map");
            Assert.That(Cost(w, 1, Flat(37500, 31500), 0), Is.EqualTo(-1L), "the blocked ground under the stair");
            Assert.That(Cost(w, 0, Up(41500, 32500), 65535), Is.EqualTo(-1L), "the air layer");
            Assert.That(Cost(w, 0, Up(41500, 31500), 1), Is.EqualTo(1000L));
        }

        // 8c. Spread over three with only two points to walk to: the rank is taken among two, and the point nobody walks to,
        // though it lies nearest on the plane, is in no rank.
        [Test]
        public void ASpreadCountsOnlyPointsItCanWalkTo()
        {
            var units = new List<UnitSpawn>();
            for (ulong key = 1; key <= 12; key++) units.Add(Walker(key, 20500 + (long)key * 1000, 20500));
            M6World w = World(units, new[] { PointUp(1, 41500, 32500), West, East }, stair: false, bases: Bases(new SpawnAnchor(1, P(41, 30))));
            Install(w, "dispatch", new NearestDispatch(0, 3));
            BothRanks(w, 1, 12);
            bool third = false;
            // From the base at ground (41, 30): east 7 cells with two diagonals = 7828, west 8 cells with one = 8414.
            for (ulong key = 1; key <= 12; key++)
            {
                third |= Rank(w, key, 3) == 2 && Rank(w, key, 2) == 1;
                Assert.That(Pick(w, key), Is.EqualTo(Rank(w, key, 2) == 0 ? 2 : 1), "unit " + key);
            }
            Assert.That(third, Is.True, "a unit whose rank among three differs");
            Assert.That((Cost(w, 2, Flat(41500, 30500), 0), Cost(w, 1, Flat(41500, 30500), 0)), Is.EqualTo((7828L, 8414L)));
        }

        // 8e. The front-line point is upstairs on a floor without a stair, with a neutral point beside it; a holder on the ground
        // cannot walk there and attacks the nearest point it can walk to. A holder upstairs goes to the front line.
        [Test]
        public void AHolderThatCannotWalkToTheFrontLineAttacks()
        {
            var points = new[] { PointUp(1, 40500, 32500, 1, 1), PointUp(2, 43500, 32500), East };
            M6World w = World(new[] { Walker(1, 30500, 20500), Walker(2, 43500, 30500, 1) }, points, stair: false);
            Install(w, "dispatch", new NearestDispatch(1000, 1));
            Assert.That(Field(w).Frontline(1), Is.EqualTo(0));
            Assert.That(Pick(w, 1), Is.EqualTo(2));
            Assert.That(Pick(w, 2), Is.EqualTo(0));
        }

        // 11. The front line is the owned point nearest to a point not owned by walking cost: upstairs is 13414 from the neutral
        // point under it, the owned ground point 10000. A pair with no way between does not count at all.
        [TestCase(true, 1)]
        [TestCase(false, 1)]
        public void TheFrontLineIsFoundByWalkingCost(bool stair, int front)
        {
            var points = new[] { PointUp(1, 41500, 32500, 1, 1), PointDown(2, 51500, 32500, 1, 1), PointDown(3, 41500, 32500) };
            M6World w = World(new[] { Walker(1, 30500, 20500) }, points, stair: stair);
            Assert.That(Cost(w, 2, Up(41500, 32500), 1), Is.EqualTo(stair ? 13414L : -1L));
            Assert.That(Field(w).Frontline(1), Is.EqualTo(front));
        }

        [Test]
        public void ATeamWhosePointsReachNothingHasNoFrontLine()
        {
            M6World w = World(new[] { Walker(1, 30500, 20500) }, new[] { PointUp(1, 41500, 32500, 1, 1), PointDown(3, 41500, 32500) }, stair: false);
            Assert.That(Field(w).Frontline(1), Is.EqualTo(-1));
        }
    }
}
