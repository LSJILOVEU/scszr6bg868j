using System.Linq;
using NUnit.Framework;

namespace WarCore.Tests
{
    // MAP-E1g E7 (one team alone) and E8 (the whole match, seed 41). The tick numbers, the winner and the hashes are behaviour
    // baselines recorded here for the first time, on the default soldiers and this temporary map: a package that changes the
    // play may record them again, listing old and new. Nothing here is a determinism lock except "the same world twice".
    public sealed class MapE1gRunTests
    {
        // ---- E7 ----

        // team, ticks, point id -> tick it passed to the team. Point ids: 1 A, 2 B, 3 C, 4 D, 5 E; A and E are held from the start.
        private static readonly (int Team, string Taken, ulong Hash)[] Alone =
        {
            (1, "2@632,3@1192,4@1711", 0x105E8D1F0A5D13E5UL),
            (2, "2@1685,3@1130,4@593", 0x74871F4379F0270DUL),
        };

        [TestCase(1)]
        [TestCase(2)]
        public void ATeamAloneWalksEverywhereItIsSentAndTakesTheMiddlePoints(int team)
        {
            MapE1gRun.Result r = MapE1gRun.Go(MapE1gWorld.World(MapE1gWorld.Alone(team)), 3000, team);
            Assert.That(r.Ticks, Is.EqualTo(3000), "a match that never ends");
            Assert.That(r.Failures, Is.Empty, "no failed walk");
            Assert.That(r.OffCell, Is.Empty, "always on a walkable cell");
            Assert.That(r.Taken.Count, Is.GreaterThanOrEqualTo(1), "takes at least one neutral point");
            // Baseline.
            Assert.That(string.Join(",", r.Taken.Select(p => p.Key + "@" + p.Value)), Is.EqualTo(Alone[team - 1].Taken));
            Assert.That(r.Hash, Is.EqualTo(Alone[team - 1].Hash), r.Hash.ToString("X16"));
            TestContext.WriteLine("MAPE1G E7 team " + team + " taken " + string.Join(",", r.Taken.Select(p => "point " + p.Key + " at tick " + p.Value)) + " deployed " + r.Deployed);
        }

        // ---- E8 ----

        private const int EndTick = 3117, Winner = 2, Deployed = 40, Dead = 100, FailedWalkTicks = 102;
        private const ulong EndHash = 0xDDE33527D87D9E73UL;

        [Test]
        public void TheWholeMatchRepeatsAndEndsWhereItEndedTheFirstTime()
        {
            MapE1gRun.Result r = MapE1gRun.Go(MapE1gWorld.World(), 9100, 0, MapE1gWorld.World());
            Assert.That(r.FirstHashDiff, Is.Null, "the same world twice, tick by tick");
            Assert.That(r.OffCell, Is.Empty, "always on a walkable cell");
            Assert.That(r.Winner, Is.Not.EqualTo(0), "decided before the time limit");
            // Baseline (first recording).
            Assert.That((r.Ticks, r.Winner, r.Deployed, r.Dead), Is.EqualTo((EndTick, Winner, Deployed, Dead)));
            Assert.That(r.Hash, Is.EqualTo(EndHash), r.Hash.ToString("X16"));
            Assert.That(string.Join(",", r.Taken.Select(p => p.Key + "@" + p.Value)), Is.EqualTo("2@632,3@2223,4@593"));
            // Not zero: the card wants no failed walk, the match has some. Recorded as the result, not asserted away.
            Assert.That(r.Failures.Count, Is.EqualTo(FailedWalkTicks));
            Assert.That(r.Failures.Select(f => f.Split(' ')[1]).Distinct().OrderBy(k => int.Parse(k)), Is.EqualTo(new[] { "5", "15", "68", "95" }));
            TestContext.WriteLine("MAPE1G E8 end tick " + r.Ticks + " winner " + r.Winner + " hash " + r.Hash.ToString("X16") + " failed-walk ticks " + r.Failures.Count);
        }
    }
}
