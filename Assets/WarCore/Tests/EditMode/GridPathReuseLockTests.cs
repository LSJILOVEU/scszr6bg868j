using System;
using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    // Locks every result of a reused GridPathfinder (status, wide cost, path keys) across a fixed query stream,
    // recorded on the search that cleared the whole cost table before each Find.
    public sealed class GridPathReuseLockTests
    {
        private const ulong RecordedDigest = 0xE54B8FEDACA04688UL;
        private const int RecordedFound = 104, RecordedUnreachable = 66, RecordedRejected = 70, RecordedExhausted = 23;

        private static readonly (int Seed, int Width, int Depth, int WallPercent, int MaxCost)[] Maps =
        {
            (101, 16, 16, 30, 0), (102, 64, 48, 25, 0), (103, 256, 256, 22, 0),
            (104, 97, 131, 35, 255), (105, 256, 256, 15, 8), (106, 1, 200, 10, 3),
        };
        private const int QueriesPerMap = 40;

        [Test]
        public void ReusedSearches_MatchRecordedDigestAndStatusCounts()
        {
            ulong digest = 14695981039346656037UL;
            int found = 0, unreachable = 0, rejected = 0, exhausted = 0;
            foreach (var spec in Maps)
            {
                NavGrid grid = Build(spec, out TerrainTables tables);
                GridPathfinder search = tables == null ? new GridPathfinder(grid) : new GridPathfinder(tables);
                foreach ((int start, int target) in Queries(spec.Seed, grid.Count))
                {
                    GridPathStatus status = search.Find(start, target);
                    if (status == GridPathStatus.Found) found++;
                    else if (status == GridPathStatus.Unreachable)
                    {
                        unreachable++;
                        // Passable in-range target: the search exhausted the open set instead of rejecting the target early.
                        if (target < grid.Count && grid.IsPassable(target % grid.Width, target / grid.Width)) exhausted++;
                    }
                    else rejected++;
                    digest = Mix(digest, search, status);
                }
            }
            TestContext.Out.WriteLine($"reuse lock: digest=0x{digest:X16} found={found} unreachable={unreachable} rejected={rejected} exhausted={exhausted}");
            Assert.That(found, Is.EqualTo(RecordedFound));
            Assert.That(unreachable, Is.EqualTo(RecordedUnreachable));
            Assert.That(rejected, Is.EqualTo(RecordedRejected));
            Assert.That(exhausted, Is.EqualTo(RecordedExhausted));
            Assert.That(digest, Is.EqualTo(RecordedDigest));
        }

        [Test]
        public void ReusedSearch_EqualsFreshSearchForEveryQuery()
        {
            foreach (var spec in Maps)
            {
                NavGrid grid = Build(spec, out TerrainTables tables);
                GridPathfinder reused = tables == null ? new GridPathfinder(grid) : new GridPathfinder(tables);
                foreach ((int start, int target) in Queries(spec.Seed, grid.Count))
                {
                    GridPathfinder fresh = tables == null ? new GridPathfinder(grid) : new GridPathfinder(tables);
                    GridPathStatus a = reused.Find(start, target), b = fresh.Find(start, target);
                    string where = $"seed {spec.Seed} {start}->{target}";
                    Assert.That(a, Is.EqualTo(b), where);
                    Assert.That(reused.TotalCostWide, Is.EqualTo(fresh.TotalCostWide), where);
                    Assert.That(reused.Path.ToArray(), Is.EqualTo(fresh.Path.ToArray()), where);
                }
            }
        }

        internal static NavGrid Build((int Seed, int Width, int Depth, int WallPercent, int MaxCost) spec, out TerrainTables tables)
        {
            var random = new Random(spec.Seed);
            int count = spec.Width * spec.Depth;
            var bits = new byte[(count + 7) / 8];
            var cost = new byte[count];
            var speed = new ushort[count];
            for (int key = 0; key < count; key++)
            {
                if (random.Next(100) < spec.WallPercent) continue;
                bits[key >> 3] |= (byte)(1 << (key & 7));
                cost[key] = (byte)(spec.MaxCost == 0 ? 1 : 1 + random.Next(spec.MaxCost));
                speed[key] = 1000;
            }
            var grid = new NavGrid(spec.Width, spec.Depth, default, default, bits);
            tables = spec.MaxCost == 0 ? null
                : new TerrainTables(null, grid, cost, speed, Array.Empty<string>(), Array.Empty<byte>(), Array.Empty<byte>());
            return grid;
        }

        // Random pairs over every key (blocked starts give Rejected), plus out-of-range keys and an immediate repeat.
        internal static (int, int)[] Queries(int seed, int count)
        {
            var random = new Random(seed * 7919);
            var queries = new (int, int)[QueriesPerMap];
            for (int i = 0; i < queries.Length; i++) queries[i] = (random.Next(count), random.Next(count));
            queries[5] = queries[4];
            queries[9] = (-1, queries[9].Item2);
            queries[13] = (queries[13].Item1, count);
            return queries;
        }

        internal static ulong Mix(ulong digest, GridPathfinder search, GridPathStatus status)
        {
            digest = Add(digest, (long)status);
            digest = Add(digest, search.TotalCostWide);
            digest = Add(digest, search.Path.Length);
            foreach (int key in search.Path) digest = Add(digest, key);
            return digest;
        }

        private static ulong Add(ulong digest, long value)
        {
            for (int i = 0; i < 8; i++) digest = unchecked((digest ^ (byte)(value >> (8 * i))) * 1099511628211UL);
            return digest;
        }
    }
}
