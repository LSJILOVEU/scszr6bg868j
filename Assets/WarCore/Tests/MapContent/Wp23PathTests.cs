using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class Wp23PathTests
    {
        private static TerrainTables Tables(int seed, int width, int depth, int wallPercent, int maxCost, bool unit = false, int minCost = 1)
        {
            var random = new Random(seed);
            var bits = new byte[(width * depth + 7) / 8];
            var cost = new byte[width * depth];
            var speed = new ushort[width * depth];
            for (int k = 0; k < width * depth; k++)
            {
                if (random.Next(100) < wallPercent) continue;
                bits[k >> 3] |= (byte)(1 << (k & 7));
                cost[k] = (byte)(unit ? 1 : minCost + random.Next(maxCost));
                speed[k] = 1000;
            }
            var nav = new NavGrid(width, depth, default, default, bits);
            return new TerrainTables(null, nav, cost, speed, Array.Empty<string>(), Array.Empty<byte>(), Array.Empty<byte>());
        }

        private static List<int> Walk(GridPathfinder finder) { var list = new List<int>(); foreach (int k in finder.Path) list.Add(k); return list; }

        [Test]
        public void PlainAndUnitCostSearches_EqualTheFrozenOldAStar_PathForPath()
        {
            int compared = 0;
            for (int seed = 0; seed < 220; seed++)
            {
                int width = 8 + seed % 9, depth = 8 + seed / 3 % 9;
                TerrainTables t = Tables(seed, width, depth, 15 + seed % 30, 1, unit: true);
                var frozen = new FrozenPathfinder(t.Nav);
                var plain = new GridPathfinder(t.Nav);
                var weighted = new GridPathfinder(t);
                var random = new Random(seed * 7 + 1);
                for (int n = 0; n < 6; n++)
                {
                    int a = random.Next(t.Nav.Count), b = random.Next(t.Nav.Count);
                    GridPathStatus expected = frozen.Find(a, b);
                    Assert.That(plain.Find(a, b), Is.EqualTo(expected), $"seed {seed} plain status");
                    Assert.That(Walk(plain), Is.EqualTo(frozen.Path), $"seed {seed} {a}->{b} plain path");
                    Assert.That(plain.TotalCost, Is.EqualTo(frozen.TotalCost));
                    Assert.That(weighted.Find(a, b), Is.EqualTo(expected), $"seed {seed} weighted status");
                    Assert.That(Walk(weighted), Is.EqualTo(frozen.Path), $"seed {seed} {a}->{b} unit-cost path");
                    Assert.That(weighted.TotalCostWide, Is.EqualTo((long)frozen.TotalCost));
                    compared++;
                }
            }
            Assert.That(compared, Is.GreaterThanOrEqualTo(200));
        }

        [Test]
        public void WeightedSearch_FindsTheDijkstraOptimum_AndEveryEdgeIsLegal()
        {
            int checkedPaths = 0;
            for (int seed = 0; seed < 220; seed++)
            {
                int width = 7 + seed % 8, depth = 7 + seed / 4 % 8;
                TerrainTables t = Tables(1000 + seed, width, depth, 10 + seed % 35, 1 + seed % 255);
                var finder = new GridPathfinder(t);
                var random = new Random(seed);
                for (int n = 0; n < 4; n++)
                {
                    int a = random.Next(t.Nav.Count), b = random.Next(t.Nav.Count);
                    bool startOk = t.Nav.IsPassable(a % width, a / width), targetOk = t.Nav.IsPassable(b % width, b / width);
                    GridPathStatus status = finder.Find(a, b);
                    if (!startOk) { Assert.That(status, Is.EqualTo(GridPathStatus.Rejected)); continue; }
                    if (!targetOk) { Assert.That(status, Is.EqualTo(GridPathStatus.Unreachable)); continue; }
                    long best = DijkstraOracle.Cost(t, a, b);
                    if (best < 0) { Assert.That(status, Is.EqualTo(GridPathStatus.Unreachable)); continue; }
                    Assert.That(status, Is.EqualTo(GridPathStatus.Found));
                    Assert.That(finder.TotalCostWide, Is.EqualTo(best), $"seed {seed} {a}->{b}");
                    var path = Walk(finder);
                    long sum = 0;
                    for (int i = 1; i < path.Count; i++)
                    {
                        int x0 = path[i - 1] % width, z0 = path[i - 1] / width, x1 = path[i] % width, z1 = path[i] / width;
                        int dx = x1 - x0, dz = z1 - z0;
                        Assert.That(Math.Abs(dx) <= 1 && Math.Abs(dz) <= 1 && (dx != 0 || dz != 0));
                        Assert.That(t.Nav.IsPassable(x1, z1), "enters a blocked cell");
                        bool diagonal = dx != 0 && dz != 0;
                        if (diagonal) Assert.That(t.Nav.IsPassable(x1, z0) && t.Nav.IsPassable(x0, z1), "cuts a corner");
                        sum += (diagonal ? 1414L : 1000L) * t.CostAt(path[i]);
                    }
                    Assert.That(sum, Is.EqualTo(best));
                    checkedPaths++;
                }
            }
            Assert.That(checkedPaths, Is.GreaterThanOrEqualTo(200));
        }

        [Test]
        public void CostsBeyondIntRange_DoNotOverflow_AndTotalCostStaysIntForPlainSearches()
        {
            // A serpentine corridor over the whole 256 x 256 grid: ~33 000 cells at 255 x 1000 each is far above 2^31.
            const int n = 256;
            var bits = new byte[(n * n + 7) / 8];
            var cost = new byte[n * n];
            var speed = new ushort[n * n];
            for (int z = 0; z < n; z++)
                for (int x = 0; x < n; x++)
                {
                    bool openRow = z % 2 == 0;
                    bool connector = z % 4 == 1 && x == n - 1 || z % 4 == 3 && x == 0;
                    if (!openRow && !connector) continue;
                    int k = z * n + x;
                    bits[k >> 3] |= (byte)(1 << (k & 7)); cost[k] = 255; speed[k] = 1000;
                }
            var nav = new NavGrid(n, n, default, default, bits);
            var t = new TerrainTables(null, nav, cost, speed, Array.Empty<string>(), Array.Empty<byte>(), Array.Empty<byte>());
            var finder = new GridPathfinder(t);
            Assert.That(finder.Find(0, (n - 1) * n), Is.EqualTo(GridPathStatus.Found));
            Assert.That(finder.TotalCostWide, Is.GreaterThan((long)int.MaxValue));
            Assert.That(finder.TotalCostWide, Is.EqualTo(1000L * 255 * (finder.Path.Length - 1)), "no diagonals exist in a one-cell corridor");
            Assert.Throws<SimOverflowException>(() => { int _ = finder.TotalCost; });
            var plain = new GridPathfinder(nav);
            Assert.That(plain.Find(0, (n - 1) * n), Is.EqualTo(GridPathStatus.Found));
            Assert.That(plain.TotalCost, Is.EqualTo(1000 * (plain.Path.Length - 1)));
        }

        [Test]
        public void CheapRoadDetours_AndExpensiveMudIsAvoided()
        {
            // 9 x 3 open field. Row 2 costs 1, row 0 costs 1, row 1 costs 9: from (0,0) to (8,0) straight is cheapest;
            // from (0,1) the two cheap rows beat staying in the expensive middle row.
            const int w = 9, d = 3;
            var bits = new byte[(w * d + 7) / 8]; var cost = new byte[w * d]; var speed = new ushort[w * d];
            for (int k = 0; k < w * d; k++) { bits[k >> 3] |= (byte)(1 << (k & 7)); cost[k] = (byte)(k / w == 1 ? 9 : 1); speed[k] = 1000; }
            var t = new TerrainTables(null, new NavGrid(w, d, default, default, bits), cost, speed, Array.Empty<string>(), Array.Empty<byte>(), Array.Empty<byte>());
            var finder = new GridPathfinder(t);
            Assert.That(finder.Find(0 * w + 0, 0 * w + 8), Is.EqualTo(GridPathStatus.Found));
            Assert.That(Walk(finder), Is.EqualTo(new List<int> { 0, 1, 2, 3, 4, 5, 6, 7, 8 }));
            Assert.That(finder.Find(1 * w + 0, 1 * w + 8), Is.EqualTo(GridPathStatus.Found));
            foreach (int k in Walk(finder).GetRange(1, 7)) Assert.That(k / w, Is.Not.EqualTo(1), "the expensive middle row is avoided");
            Assert.That(finder.TotalCostWide, Is.LessThan(8L * 1000 * 9));
        }

        // The tie-breaks are part of the contract: with every cell dearer than 1 the heuristic scales by the cheapest cell, and the
        // routes picked among equal-cost ones are exactly these (a checksum of 92 routes on cost-3..4 fields).
        [Test]
        public void RoutesOnDearFields_AreFrozen()
        {
            long checksum = 17;
            int routes = 0;
            for (int seed = 0; seed < 40; seed++)
            {
                TerrainTables t = Tables(seed, 12 + seed % 7, 10 + seed % 5, 12, 2, minCost: 3);
                var finder = new GridPathfinder(t);
                var random = new Random(seed + 1000);
                for (int q = 0; q < 3; q++)
                {
                    int a = random.Next(t.Nav.Count), b = random.Next(t.Nav.Count);
                    if (finder.Find(a, b) != GridPathStatus.Found) continue;
                    routes++;
                    foreach (int k in finder.Path) checksum = checksum * 1000003 + k;
                    checksum = checksum * 31 + finder.TotalCostWide;
                }
            }
            Console.WriteLine($"WP23 dear-field routes {routes} checksum {checksum}");
            Assert.That(routes, Is.EqualTo(ExpectedRoutes));
            Assert.That(checksum, Is.EqualTo(ExpectedChecksum));
        }

        private const int ExpectedRoutes = 92;
        private const long ExpectedChecksum = 1804451548754723646;
    }
}
