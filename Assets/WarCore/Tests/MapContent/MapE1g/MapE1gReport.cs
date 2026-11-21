using NUnit.Framework;

namespace WarCore.Tests
{
    // MAP-E1g: prints the numbers the run tests lock. Not part of any suite.
    public sealed class MapE1gReport
    {
        [Test, Explicit("prints the baseline numbers"), Category("Manual")]
        public void Print()
        {
            foreach (int team in new[] { 1, 2 })
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                MapE1gRun.Result r = MapE1gRun.Go(MapE1gWorld.World(MapE1gWorld.Alone(team)), 3000, team);
                TestContext.WriteLine("E7 team " + team + " ticks=" + r.Ticks + " taken=" + string.Join(",", System.Linq.Enumerable.Select(r.Taken, p => p.Key + "@" + p.Value))
                    + " failures=" + r.Failures.Count + " off=" + r.OffCell.Count + " deployed=" + r.Deployed + " hash=" + r.Hash.ToString("X16") + " ms=" + sw.ElapsedMilliseconds);
                if (r.Failures.Count > 0) TestContext.WriteLine("  first failure " + r.Failures[0]);
                if (r.OffCell.Count > 0) TestContext.WriteLine("  first off " + r.OffCell[0]);
            }
            var s2 = System.Diagnostics.Stopwatch.StartNew();
            MapE1gRun.Result b = MapE1gRun.Go(MapE1gWorld.World(), 9100, 0, MapE1gWorld.World());
            TestContext.WriteLine("E8 ticks=" + b.Ticks + " winner=" + b.Winner + " hash=" + b.Hash.ToString("X16") + " diff=" + b.FirstHashDiff + " failures=" + b.Failures.Count
                + " off=" + b.OffCell.Count + " deployed=" + b.Deployed + " dead=" + b.Dead + " taken=" + string.Join(",", System.Linq.Enumerable.Select(b.Taken, p => p.Key + "@" + p.Value)) + " ms=" + s2.ElapsedMilliseconds);
            if (b.Failures.Count > 0) TestContext.WriteLine("  first failure " + b.Failures[0]);
            if (b.OffCell.Count > 0) TestContext.WriteLine("  first off " + b.OffCell[0]);
        }
    }
}
