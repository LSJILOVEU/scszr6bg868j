using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Content.MapTools;
using WarCore.Content.Rwr;
using WarCore.Sim;

namespace WarCore.Tests
{
    // MAP-E0: the assault demo map on uneven ground. Level 0 is the map as it is (locked by Ml11aAssaultTests; here only the
    // reference), level 1 adds a height table that is all 0, level 2 adds waves and lifts the house (MapE0Level).
    public sealed class MapE0AssaultTests
    {
        private const long M = SimFix.Scale;

        private static readonly (int, int, int, int, int, int)[] FourSeams =
        {
            (0, 16, 11, 1, 17, 11), (0, 16, 12, 1, 17, 12), (1, 20, 11, 2, 21, 11), (1, 20, 12, 2, 21, 12),
        };

        private static string Report(string name, MapE0Level.SquadRun r) =>
            "MAPE0 " + name + " taken=" + r.Taken + " east=" + r.East + " firstUp=" + string.Join(",", r.FirstUp.Select(p => p.Key + ":" + p.Value)) +
            " startY=" + string.Join(",", r.StartY.Select(p => p.Key + ":" + p.Value));

        private static string Report(string name, MapE0Level.BattleRun r) =>
            "MAPE0 " + name + " ticks=" + r.Ticks + " winner=" + r.Winner + " hash=" + r.Hash.ToString("x16") + " attackersUpstairs=" + r.Upstairs.Count;

        // ---- Z1: the fixture is what the card says ----

        private static long Sum(IReadOnlyList<IReadOnlyList<int>> heights) => heights.Sum(row => row.Sum(h => (long)h));

        [Test]
        public void TheWavesAreTheCardsGround()
        {
            MapE0Level level = MapE0Level.Of(2);
            IReadOnlyList<IReadOnlyList<int>> h = level.Desc.Heights;
            Assert.That((h.Count, h[0].Count), Is.EqualTo((21, 41)), "21 rows (z) of 41 points (x)");
            Assert.That(h.Sum(row => row.Count), Is.EqualTo(861));
            Assert.That(h.Min(row => row.Min()), Is.EqualTo(0));
            Assert.That(h.Max(row => row.Max()), Is.EqualTo(24192));
            Assert.That(Sum(h), Is.EqualTo(10410560));
            int step = 0;
            for (int z = 0; z < h.Count; z++)
                for (int x = 0; x < h[z].Count; x++)
                {
                    if (x > 0) step = Math.Max(step, Math.Abs(h[z][x] - h[z][x - 1]));
                    if (z > 0) step = Math.Max(step, Math.Abs(h[z][x] - h[z - 1][x]));
                }
            Assert.That(step, Is.EqualTo(7792), "largest height difference between neighbouring points");
            // Heights[z][x]
            Assert.That(new[] { h[0][0], h[0][40], h[11][17], h[12][17], h[13][17], h[11][16], h[7][25] },
                Is.EqualTo(new[] { 0, 16000, 14992, 8848, 12944, 12544, 18192 }));
        }

        [Test]
        public void TheHouseIsLiftedByTheRise()
        {
            MapE0Level level = MapE0Level.Of(2);
            Assert.That(level.Rise, Is.EqualTo(18592));
            MapDesc d = level.Desc, original = PlateAssaultMap.Desc();
            const long Top = 3 * M + 18592;   // 215200: the first floor, the stair's high end, the ground storey's walls
            Assert.That(Top, Is.EqualTo(215200));
            Assert.That(d.Walls.Count, Is.EqualTo(original.Walls.Count));
            int ground = 0, upper = 0;
            for (int i = 0; i < d.Walls.Count; i++)
            {
                MapWall w = d.Walls[i], o = original.Walls[i];
                Assert.That((w.Id, w.Kind, w.Layer, w.Cell.X, w.Cell.Z, w.Size.X, w.Size.Z), Is.EqualTo((o.Id, o.Kind, o.Layer, o.Cell.X, o.Cell.Z, o.Size.X, o.Size.Z)), "wall " + w.Id + " cells");
                if (w.BaseRaw.HasValue)
                {
                    upper++;
                    Assert.That((w.BaseRaw.Value, w.HeightRaw), Is.EqualTo((Top, 3 * M)), "upper wall " + w.Id + " bottom and height");
                    Assert.That(w.BaseRaw.Value + w.HeightRaw, Is.EqualTo(411808), "upper wall " + w.Id + " top");
                }
                else
                {
                    ground++;
                    Assert.That(w.HeightRaw, Is.EqualTo(Top), "ground wall " + w.Id + " top");
                }
            }
            Assert.That((ground, upper), Is.EqualTo((7, 8)));
            Assert.That(d.CeilingRaw, Is.EqualTo(8 * M));
            Assert.That(411808, Is.LessThan(d.CeilingRaw));
            MapPlate stair = d.Plates[0], floor = d.Plates[1];
            Assert.That((stair.Id, stair.YRaw, stair.Slope.Axis, stair.Slope.FromRaw, stair.Slope.ToRaw, stair.ThickRaw), Is.EqualTo((1UL, (long?)null, "x", 18592L, Top, M / 2)));
            Assert.That((floor.Id, floor.YRaw, floor.Slope, floor.ThickRaw), Is.EqualTo((2UL, (long?)Top, (MapSlope)null, M / 2)));
            Assert.That((stair.Cell.X, stair.Cell.Z, stair.Size.X, stair.Size.Z, floor.Cell.X, floor.Cell.Z, floor.Size.X, floor.Size.Z),
                Is.EqualTo((17, 11, 4, 2, 15, 6, 10, 8)));
            // Nothing else moved.
            Assert.That(d.Cutouts.Count, Is.EqualTo(1));
            Assert.That((d.Points.Count, d.Bases.Count, d.Units.Count), Is.EqualTo((original.Points.Count, original.Bases.Count, original.Units.Count)));
            Assert.That(d.Units.Select(u => (u.Key, u.Team, u.Squad, u.At.X, u.At.Z, u.Plate)), Is.EqualTo(original.Units.Select(u => (u.Key, u.Team, u.Squad, u.At.X, u.At.Z, u.Plate))));
            Assert.That(d.Run.Seed, Is.EqualTo(41UL));
            // The feet of the stair stand on the ground below its low end by less than the seam's 0.3 m (card: 55 to 149 mm).
            long[] foot = { d.Heights[11][16], d.Heights[11][17], d.Heights[12][17], d.Heights[13][17] };
            Assert.That(stair.Slope.FromRaw - d.Heights[11][17], Is.EqualTo(3600));
            Assert.That(stair.Slope.FromRaw - d.Heights[12][17], Is.EqualTo(9744));
            Assert.That(stair.Slope.FromRaw - d.Heights[13][17], Is.EqualTo(5648));
            Assert.That(foot.Max(), Is.LessThan(stair.Slope.FromRaw));
        }

        [Test]
        public void TheFlatTableDiffersFromTheMapByTheTableOnly()
        {
            MapE0Level level = MapE0Level.Of(1);
            MapDesc d = level.Desc, original = PlateAssaultMap.Desc();
            Assert.That(level.Rise, Is.EqualTo(0));
            Assert.That(original.Heights, Is.Null);
            Assert.That((d.Heights.Count, d.Heights[0].Count), Is.EqualTo((21, 41)));
            Assert.That(Sum(d.Heights), Is.EqualTo(0), "every point of the table is 0");
            Assert.That(d.Heights.All(row => row.All(v => v == 0)), Is.True);
            Assert.That(d.Walls.Select(w => (w.Id, w.Cell.X, w.Cell.Z, w.Size.X, w.Size.Z, w.HeightRaw, w.BaseRaw)),
                Is.EqualTo(original.Walls.Select(w => (w.Id, w.Cell.X, w.Cell.Z, w.Size.X, w.Size.Z, w.HeightRaw, w.BaseRaw))));
            Assert.That(d.Plates.Select(p => (p.Id, p.Cell.X, p.Cell.Z, p.Size.X, p.Size.Z, p.YRaw, p.Slope?.FromRaw, p.Slope?.ToRaw, p.ThickRaw)),
                Is.EqualTo(original.Plates.Select(p => (p.Id, p.Cell.X, p.Cell.Z, p.Size.X, p.Size.Z, p.YRaw, p.Slope?.FromRaw, p.Slope?.ToRaw, p.ThickRaw))));
            Assert.That(d.Cutouts.Count, Is.EqualTo(original.Cutouts.Count));
            Assert.That((d.Points.Count, d.Bases.Count, d.Units.Count), Is.EqualTo((original.Points.Count, original.Bases.Count, original.Units.Count)));
        }

        // ---- Z2, Z3: the gate and the seams ----

        [TestCase(1)]
        [TestCase(2)]
        public void TheGateAcceptsTheLiftedMap(int level) =>
            Assert.That(MapGate.Check(MapE0Level.Of(level).DescBytes, RwrPack.Load("rwr-m6")), Is.Empty);

        [TestCase(1)]
        [TestCase(2)]
        public void TheSeamsAreTheFourOfTheOriginal(int level) =>
            Assert.That(MapE0Level.Of(level).Seams(), Is.EquivalentTo(FourSeams));

        // ---- Z4: team 1 alone walks up the stair ----

        // Level 1 equals level 0 of this run (the literal of today's main goes into result.md only).
        [Test]
        public void TheFlatTableDoesNotChangeTheSquadsWalk()
        {
            MapE0Level.SquadRun zero = MapE0Level.Of(0).RunSquad(), one = MapE0Level.Of(1).RunSquad();
            TestContext.WriteLine(Report("squad level0", zero));
            TestContext.WriteLine(Report("squad level1", one));
            Assert.That(zero.Taken, Is.GreaterThan(0), "the reference level takes the point upstairs");
            Assert.That(one.FirstFailure, Is.Null);
            Assert.That(one.FirstUp.Count, Is.EqualTo(8), "every soldier has been on the first floor");
            Assert.That((one.Taken, one.East), Is.EqualTo((zero.Taken, zero.East)));
            Assert.That(one.StartY.Values.All(y => y == 0), Is.True, "the flat table is at 0");
        }

        [Test]
        public void TheSquadTakesThePointUpstairsOnTheWaves()
        {
            MapE0Level.SquadRun run = MapE0Level.Of(2).RunSquad();
            TestContext.WriteLine(Report("squad level2", run));
            Assert.That(run.FirstFailure, Is.Null, "no walk fails");
            Assert.That(run.FirstUp.Count, Is.EqualTo(8), "every soldier has been on the first floor");
            Assert.That(run.Taken, Is.GreaterThan(0), "the point upstairs is taken within 900 ticks");
            Assert.That(run.East, Is.GreaterThan(0), "the east point is taken within 900 ticks");
            // The height table is live: the soldiers start on ground above 0 and stand on the first floor at 3 m over the rise.
            Assert.That(run.StartY.Values.All(y => y > 0), Is.True, "every soldier starts on ground above 0");
            Assert.That(run.UpY.Values.All(y => y == 3 * M + 18592), Is.True, "the first floor's surface is 215200");
        }

        // ---- Z5: the whole battle ----

        [TestCase(1)]
        [TestCase(2)]
        public void TheBattleKeepsEveryoneOnWalkableCells(int level)
        {
            MapE0Level.BattleRun run = MapE0Level.Of(level).RunBattle();
            TestContext.WriteLine(Report("battle level" + level, run));
            Assert.That(run.FirstHashDiff, Is.Null, "the same seed gives the same hashes");
            Assert.That(run.FirstFailure, Is.Null, "no walk fails");
            Assert.That(run.FirstOffCell, Is.Null, "every living soldier stands on a walkable cell of its layer");
            Assert.That(run.Winner, Is.Not.EqualTo(0), "the battle ends");
            Assert.That(run.Upstairs, Is.Not.Empty, "an attacker reaches the first floor");
        }

        // The reference the result page quotes: the same battle on the map as it is.
        [Test]
        public void TheReferenceBattleOnTheOriginalMapIsReported()
        {
            MapE0Level.BattleRun run = MapE0Level.Of(0).RunBattle();
            TestContext.WriteLine(Report("battle level0", run));
            Assert.That(run.Winner, Is.Not.EqualTo(0));
        }
    }
}
