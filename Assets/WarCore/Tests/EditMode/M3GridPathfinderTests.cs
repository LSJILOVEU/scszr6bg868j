using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class M3GridPathfinderTests
    {
        public static IEnumerable<int> MapSeeds()
        {
            for (int i = 0; i < 200; i++) yield return 73000 + i;
        }

        [TestCaseSource(nameof(MapSeeds))]
        public void AllPairs_MatchIndependentDijkstraAndValidateEveryPath(int seed)
        {
            var random = new Random(seed);
            int width = random.Next(2, 17), depth = random.Next(2, 17);
            var open = new bool[width * depth];
            for (int i = 0; i < open.Length; i++) open[i] = random.Next(100) >= 30;
            NavGrid grid = Grid(width, depth, open, -19, -7);
            var search = new GridPathfinder(grid);
            long pairs = 0;
            for (int start = 0; start < open.Length; start++)
            {
                // One independent solve per start supplies all target costs.
                int[] distances = Dijkstra(width, depth, open, start);
                for (int target = 0; target < open.Length; target++)
                {
                    GridPathStatus expected = !open[start] ? GridPathStatus.Rejected
                        : distances[target] == int.MaxValue ? GridPathStatus.Unreachable : GridPathStatus.Found;
                    GridPathStatus actual = search.Find(grid.Center(start, default), grid.Center(target, default));
                    if (actual != expected) Assert.Fail($"seed={seed} {start}->{target}: {actual} != {expected}");
                    if (expected != GridPathStatus.Found)
                    {
                        if (search.Path.Length != 0 || search.TotalCost != 0) Assert.Fail("Failed search retained a result.");
                        continue;
                    }
                    if (search.TotalCost != distances[target])
                        Assert.Fail($"seed={seed} {start}->{target}: cost {search.TotalCost} != {distances[target]}");
                    ValidatePath(search.Path, search.TotalCost, start, target, width, depth, open);
                    pairs++;
                }
            }
            TestContext.Out.WriteLine($"seed={seed}, grid={width}x{depth}, pairs={open.Length * open.Length}, reachable={pairs}");
        }

        [TestCase(0, 19, new[] { 0, 6, 12, 18, 19 })]
        [TestCase(24, 5, new[] { 24, 18, 12, 6, 5 })]
        public void OpenGrid_TieVectors(int start, int target, int[] expected)
        {
            var search = new GridPathfinder(Rows(".....", ".....", ".....", ".....", "....."));
            Assert.That(search.Find(start, target), Is.EqualTo(GridPathStatus.Found));
            Assert.That(search.Path.ToArray(), Is.EqualTo(expected));
            Assert.That(search.TotalCost, Is.EqualTo(5242));
        }

        [Test]
        public void SymmetricWall_TieUsesLowerNodeKey()
        {
            var search = new GridPathfinder(Rows(".....", "..#..", "..#..", "..#..", "....."));
            Assert.That(search.Find(10, 14), Is.EqualTo(GridPathStatus.Found));
            Assert.That(search.Path.ToArray(), Is.EqualTo(new[] { 10, 6, 1, 2, 3, 9, 14 }));
            Assert.That(search.TotalCost, Is.EqualTo(6828));
        }

        [Test]
        public void MultipleObstacles_TieVector()
        {
            var search = new GridPathfinder(Rows(".......", "...#...", ".#.#.#.", "...#...", "......."));
            Assert.That(search.Find(14, 20), Is.EqualTo(GridPathStatus.Found));
            Assert.That(search.Path.ToArray(), Is.EqualTo(new[] { 14, 7, 8, 2, 3, 4, 12, 13, 20 }));
            Assert.That(search.TotalCost, Is.EqualTo(8828));
        }

        [Test]
        public void NegativeOrigin_HalfOpenBoundsAndImmutableDigest()
        {
            byte[] bits = { 255, 255 };
            var grid = new NavGrid(4, 4, SimFix.FromInt(-4), SimFix.FromInt(-4), bits);
            bits[0] = 0;
            Assert.That(grid.IsPassable(0, 0), Is.True);
            Assert.That(grid.TryCell(SimVec3.Metres(-4, 0, -4), out int key), Is.True);
            Assert.That(key, Is.Zero);
            Assert.That(grid.TryCell(new SimVec3(SimFix.FromRaw(-1), default, SimFix.FromRaw(-1)), out key), Is.True);
            Assert.That(key, Is.EqualTo(15));
            Assert.That(grid.TryCell(SimVec3.Metres(-3, 0, -3), out key), Is.True);
            Assert.That(key, Is.EqualTo(5));
            Assert.That(grid.TryCell(default, out _), Is.False);
            Assert.That(grid.TryCell(new SimVec3(SimFix.FromRaw(-4 * 65536 - 1), default, SimFix.FromInt(-3)), out _), Is.False);
            Assert.That(grid.TryCell(new SimVec3(SimFix.FromRaw(long.MinValue), default, default), out _), Is.False);
            Assert.That(grid.TryCell(new SimVec3(SimFix.FromRaw(long.MaxValue), default, default), out _), Is.False);
            using var bytes = new MemoryStream();
            using var writer = new BinaryWriter(bytes);
            writer.Write(4L); writer.Write(4L); writer.Write(-4L * 65536); writer.Write(-4L * 65536);
            writer.Write(new byte[] { 255, 255 });
            ulong digest = 14695981039346656037UL;
            foreach (byte b in bytes.ToArray()) digest = unchecked((digest ^ b) * 1099511628211UL);
            Assert.That(grid.Digest, Is.EqualTo(digest));
            Assert.That(new NavGrid(4, 4, SimFix.FromInt(-4), SimFix.FromInt(-4), bits).Digest, Is.Not.EqualTo(digest));
            Assert.That(new NavGrid(4, 4, default, default, new byte[] { 255, 255 }).Digest, Is.Not.EqualTo(digest));
            Assert.That(new NavGrid(8, 2, SimFix.FromInt(-4), SimFix.FromInt(-4), new byte[] { 255, 255 }).Digest, Is.Not.EqualTo(digest));
        }

        [TestCase(0, 1)] [TestCase(1, 0)] [TestCase(257, 1)] [TestCase(1, 257)]
        public void GridDimensions_AreBounded(int width, int depth) =>
            Assert.Throws<ArgumentOutOfRangeException>(() => new NavGrid(width, depth, default, default, Array.Empty<byte>()));

        [Test]
        public void GridEncoding_RejectsFractionalOriginAndInvalidBitmap()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new NavGrid(1, 1, SimFix.FromRaw(-1), default, new byte[] { 1 }));
            Assert.Throws<ArgumentException>(() => new NavGrid(1, 1, default, default, Array.Empty<byte>()));
            Assert.Throws<ArgumentException>(() => new NavGrid(1, 1, default, default, new byte[] { 255 }));
            Assert.That(new NavGrid(1, 1, default, default, new byte[] { 1 }).Count, Is.EqualTo(1));
            var maximum = new NavGrid(256, 256, default, default, new byte[8192]);
            Assert.That(maximum.Count, Is.EqualTo(65536));
        }

        [Test]
        public void SameCellFailuresAndReuse_ClearBorrowedResult()
        {
            var search = new GridPathfinder(Rows(".#.", "...", ".#."));
            Assert.That(search.Find(3, 3), Is.EqualTo(GridPathStatus.Found));
            Assert.That(search.Path.ToArray(), Is.EqualTo(new[] { 3 }));
            Assert.That(search.TotalCost, Is.Zero);
            foreach (int end in new[] { -1, 9, 1, 7 })
            {
                Assert.That(search.Find(3, end), Is.EqualTo(GridPathStatus.Unreachable));
                Assert.That(search.Path.Length, Is.Zero);
            }
            foreach (int start in new[] { -1, 9, 1, 7 })
                Assert.That(search.Find(start, 3), Is.EqualTo(GridPathStatus.Rejected));
            Assert.That(search.Find(0, 2), Is.EqualTo(GridPathStatus.Found));
            Assert.That(search.Path.ToArray(), Is.EqualTo(new[] { 0, 3, 4, 5, 2 }));
        }

        [Test]
        public void DiagonalNeedsBothOrthogonalCells()
        {
            foreach (NavGrid grid in new[] { Rows(".#", "#."), Rows("..", "#.") })
            {
                var search = new GridPathfinder(grid);
                GridPathStatus result = search.Find(0, 3);
                if (grid.IsPassable(1, 0))
                {
                    Assert.That(result, Is.EqualTo(GridPathStatus.Found));
                    Assert.That(search.Path.ToArray(), Is.EqualTo(new[] { 0, 1, 3 }));
                    Assert.That(search.TotalCost, Is.EqualTo(2000));
                }
                else Assert.That(result, Is.EqualTo(GridPathStatus.Unreachable));
            }
        }

        internal static NavGrid Rows(params string[] rows)
        {
            int width = rows[0].Length;
            var open = new bool[width * rows.Length];
            for (int i = 0; i < open.Length; i++) open[i] = rows[i / width][i % width] != '#';
            return Grid(width, rows.Length, open);
        }

        internal static NavGrid Grid(int width, int depth, bool[] open, int ox = 0, int oz = 0)
        {
            var bits = new byte[(open.Length + 7) / 8];
            for (int i = 0; i < open.Length; i++) if (open[i]) bits[i / 8] |= (byte)(1 << (i % 8));
            return new NavGrid(width, depth, SimFix.FromInt(ox), SimFix.FromInt(oz), bits);
        }

        private static int[] Dijkstra(int width, int depth, bool[] open, int start)
        {
            var distance = new int[open.Length];
            var visited = new bool[open.Length];
            for (int i = 0; i < distance.Length; i++) distance[i] = int.MaxValue;
            if (!open[start]) return distance;
            distance[start] = 0;
            while (true)
            {
                int best = -1;
                for (int i = 0; i < distance.Length; i++)
                    if (!visited[i] && distance[i] != int.MaxValue && (best < 0 || distance[i] < distance[best])) best = i;
                if (best < 0) return distance;
                visited[best] = true;
                int x = best % width, z = best / width;
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dz == 0) continue;
                        int nx = x + dx, nz = z + dz;
                        if (nx < 0 || nx >= width || nz < 0 || nz >= depth || !open[nz * width + nx]) continue;
                        if (dx != 0 && dz != 0 && (!open[z * width + nx] || !open[nz * width + x])) continue;
                        int next = nz * width + nx;
                        int candidate = distance[best] + (dx == 0 || dz == 0 ? 1000 : 1414);
                        if (candidate < distance[next]) distance[next] = candidate;
                    }
            }
        }

        private static void ValidatePath(ReadOnlySpan<int> path, int cost, int start, int target, int width, int depth, bool[] open)
        {
            if (path.Length == 0 || path.Length > open.Length || path[0] != start || path[path.Length - 1] != target)
                Assert.Fail("Invalid endpoints or path length.");
            int sum = 0;
            for (int i = 0; i < path.Length; i++)
            {
                int key = path[i];
                if (key < 0 || key >= open.Length || !open[key]) Assert.Fail("Path entered a wall or left the grid.");
                if (i == 0) continue;
                int previous = path[i - 1];
                int dx = Math.Abs(key % width - previous % width), dz = Math.Abs(key / width - previous / width);
                if (dx > 1 || dz > 1 || dx + dz == 0) Assert.Fail("Nonadjacent path step.");
                if (dx == 1 && dz == 1)
                {
                    if (!open[previous / width * width + key % width] || !open[key / width * width + previous % width])
                        Assert.Fail("Path cuts a blocked corner.");
                    sum += 1414;
                }
                else sum += 1000;
            }
            if (sum != cost) Assert.Fail($"Path cost {sum} differs from reported {cost}.");
        }
    }
}
