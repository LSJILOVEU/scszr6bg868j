using System;
using NUnit.Framework;
using WarCore.Sim;
using static WarCore.Tests.Ml6Nav;

namespace WarCore.Tests
{
    // ML-6 acceptance 5: with no plates the layered search is GridPathfinder bit for bit on the same TerrainTables:
    // status, path, total and Expanded, for Find and FindWithin.
    public sealed class Ml6EquivalenceTests
    {
        [Test]
        public void ZeroPlatesIsGridPathfinder()
        {
            var rnd = new Random(6201);
            int pairs = 0, found = 0;
            for (int m = 0; m < 500; m++)
            {
                int w = rnd.Next(1, 33), d = rnd.Next(1, 33);
                var cost = new byte[w * d];
                int blockedOneIn = rnd.Next(2, 8);
                bool weighted = rnd.Next(3) != 0;
                for (int k = 0; k < cost.Length; k++)
                    cost[k] = rnd.Next(blockedOneIn) == 0 ? (byte)0 : weighted ? (byte)rnd.Next(1, 256) : (byte)1;
                TerrainTables ground = Ground(w, d, cost);
                var grid = new GridPathfinder(ground);
                var layered = new LayeredPathfinder(Nav(ground));
                for (int p = 0; p < 6; p++)
                {
                    int a = rnd.Next(-1, w * d + 1), b = rnd.Next(-1, w * d + 1);
                    if (Same(grid, layered, a, b)) found++;
                    pairs++;
                }
            }
            TestContext.Out.WriteLine("ML-6 zero plates: pairs " + pairs + ", found " + found);
            Assert.That(found, Is.GreaterThanOrEqualTo(1000));
        }

        // One 512 x 512 ground with scattered blocked cells and costs, corner to corner and a few more pairs.
        [Test]
        public void ZeroPlatesOnTheLargestMap()
        {
            const int side = 512;
            var rnd = new Random(6202);
            var cost = new byte[side * side];
            for (int k = 0; k < cost.Length; k++) cost[k] = rnd.Next(5) == 0 ? (byte)0 : (byte)rnd.Next(1, 4);
            cost[0] = 1; cost[cost.Length - 1] = 1;
            TerrainTables ground = Ground(side, side, cost);
            var grid = new GridPathfinder(ground);
            var layered = new LayeredPathfinder(Nav(ground));
            Assert.That(Same(grid, layered, 0, cost.Length - 1), Is.True, "corner to corner");
            for (int p = 0; p < 4; p++) Same(grid, layered, rnd.Next(cost.Length), rnd.Next(cost.Length));
        }

        private static bool Same(GridPathfinder grid, LayeredPathfinder layered, int a, int b)
        {
            GridPathStatus want = grid.Find(a, b);
            Assert.That(layered.Find(a, b), Is.EqualTo(want), a + " -> " + b);
            Assert.That(layered.Path.ToArray(), Is.EqualTo(grid.Path.ToArray()), "path");
            Assert.That((layered.TotalCostWide, layered.Expanded), Is.EqualTo((grid.TotalCostWide, grid.Expanded)), "total and expanded");
            if (want != GridPathStatus.Found) return false;
            long cost = grid.TotalCostWide;
            foreach (long limit in new[] { cost, cost - 1, cost / 2 })
            {
                GridPathStatus within = grid.FindWithin(a, b, limit);
                Assert.That(layered.FindWithin(a, b, limit), Is.EqualTo(within), "within " + limit);
                Assert.That(layered.Path.ToArray(), Is.EqualTo(grid.Path.ToArray()));
                Assert.That((layered.TotalCostWide, layered.Expanded), Is.EqualTo((grid.TotalCostWide, grid.Expanded)));
            }
            return true;
        }
    }
}
