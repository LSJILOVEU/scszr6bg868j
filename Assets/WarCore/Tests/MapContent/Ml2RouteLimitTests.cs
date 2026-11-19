using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Content;
using WarCore.Content.MapTools;
using WarCore.Content.Rwr;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    // ML-2: the route search with a cost limit (GridPathfinder.FindWithin) and the read-only AI port IMoveOrders.TryRouteCost.
    public sealed class Ml2RouteLimitTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        // One query: FindWithin(no limit) equals Find; a found route is found again at limit = cost and not at cost - 1;
        // a rejected start stays rejected at every limit; an unreachable target stays unreachable at every limit.
        private static GridPathStatus Check(GridPathfinder search, int start, int target, string where)
        {
            GridPathStatus status = search.Find(start, target);
            long cost = search.TotalCostWide;
            int[] path = search.Path.ToArray();
            AssertSame(search, search.FindWithin(start, target, long.MaxValue), status, cost, path, where + " no limit");
            if (status == GridPathStatus.Found)
            {
                AssertSame(search, search.FindWithin(start, target, cost), status, cost, path, where + " limit=cost");
                AssertSame(search, search.FindWithin(start, target, cost - 1), GridPathStatus.Unreachable, 0, Array.Empty<int>(), where + " limit=cost-1");
            }
            else
                foreach (long limit in new[] { -1L, 0L, 1000L, long.MaxValue })
                    AssertSame(search, search.FindWithin(start, target, limit), status, 0, Array.Empty<int>(), where + " limit=" + limit);
            return status;
        }

        private static void AssertSame(GridPathfinder search, GridPathStatus actual, GridPathStatus status, long cost, int[] path, string where)
        {
            if (actual != status) Assert.Fail(where + ": status " + actual + " != " + status);
            if (search.TotalCostWide != cost) Assert.Fail(where + ": cost " + search.TotalCostWide + " != " + cost);
            if (!search.Path.SameAs(path)) Assert.Fail(where + ": path differs");
        }

        // ---- acceptance 1 and 2: equivalence and the limit edge ----

        [Test]
        public void ReuseLockQueries_EqualFindAndHoldTheLimitEdge()
        {
            int found = 0, unreachable = 0, rejected = 0;
            foreach (var spec in new[] { (101, 16, 16, 30, 0), (102, 64, 48, 25, 0), (103, 256, 256, 22, 0),
                (104, 97, 131, 35, 255), (105, 256, 256, 15, 8), (106, 1, 200, 10, 3) })
            {
                NavGrid grid = GridPathReuseLockTests.Build(spec, out TerrainTables tables);
                GridPathfinder search = tables == null ? new GridPathfinder(grid) : new GridPathfinder(tables);
                foreach ((int start, int target) in GridPathReuseLockTests.Queries(spec.Item1, grid.Count))
                    Count(Check(search, start, target, "seed " + spec.Item1 + " " + start + "->" + target), ref found, ref unreachable, ref rejected);
            }
            // The reuse lock's recorded counts (GridPathReuseLockTests.RecordedFound/Unreachable/Rejected).
            Assert.That((found, unreachable, rejected), Is.EqualTo((104, 66, 70)));
        }

        public static IEnumerable<int> M3Seeds()
        {
            for (int i = 0; i < 200; i++) yield return 73000 + i;
        }

        // The random maps of M3GridPathfinderTests, every pair.
        [TestCaseSource(nameof(M3Seeds))]
        public void M3RandomMaps_EveryPairEqualsFindAndHoldsTheLimitEdge(int seed)
        {
            var random = new Random(seed);
            int width = random.Next(2, 17), depth = random.Next(2, 17);
            var open = new bool[width * depth];
            for (int i = 0; i < open.Length; i++) open[i] = random.Next(100) >= 30;
            var search = new GridPathfinder(M3GridPathfinderTests.Grid(width, depth, open, -19, -7));
            int found = 0, unreachable = 0, rejected = 0;
            for (int start = 0; start < open.Length; start++)
                for (int target = 0; target < open.Length; target++)
                    Count(Check(search, start, target, "seed " + seed + " " + start + "->" + target), ref found, ref unreachable, ref rejected);
            Assert.That(found + unreachable + rejected, Is.EqualTo(open.Length * open.Length));
        }

        // The fixed maps of M3GridPathfinderTests with their queries, plus out-of-range keys.
        [Test]
        public void M3FixedMaps_EqualFindAndHoldTheLimitEdge()
        {
            var cases = new (NavGrid grid, int start, int target, GridPathStatus status)[]
            {
                (M3GridPathfinderTests.Rows(".....", ".....", ".....", ".....", "....."), 0, 19, GridPathStatus.Found),
                (M3GridPathfinderTests.Rows(".....", ".....", ".....", ".....", "....."), 24, 5, GridPathStatus.Found),
                (M3GridPathfinderTests.Rows(".....", "..#..", "..#..", "..#..", "....."), 10, 14, GridPathStatus.Found),
                (M3GridPathfinderTests.Rows(".......", "...#...", ".#.#.#.", "...#...", "......."), 14, 20, GridPathStatus.Found),
                (M3GridPathfinderTests.Rows(".#.", "...", ".#."), 3, 3, GridPathStatus.Found),
                (M3GridPathfinderTests.Rows(".#.", "...", ".#."), 0, 2, GridPathStatus.Found),
                (M3GridPathfinderTests.Rows(".#.", "...", ".#."), 3, 1, GridPathStatus.Unreachable),
                (M3GridPathfinderTests.Rows(".#.", "...", ".#."), 3, -1, GridPathStatus.Unreachable),
                (M3GridPathfinderTests.Rows(".#.", "...", ".#."), 3, 9, GridPathStatus.Unreachable),
                (M3GridPathfinderTests.Rows(".#.", "...", ".#."), 1, 3, GridPathStatus.Rejected),
                (M3GridPathfinderTests.Rows(".#.", "...", ".#."), -1, 3, GridPathStatus.Rejected),
                (M3GridPathfinderTests.Rows(".#.", "...", ".#."), 9, 3, GridPathStatus.Rejected),
                (M3GridPathfinderTests.Rows(".#", "#."), 0, 3, GridPathStatus.Unreachable),
                (M3GridPathfinderTests.Rows("..", "#."), 0, 3, GridPathStatus.Found),
            };
            foreach (var c in cases)
                Assert.That(Check(new GridPathfinder(c.grid), c.start, c.target, c.start + "->" + c.target), Is.EqualTo(c.status), c.start + "->" + c.target);
        }

        // A blocked start is Rejected before the limit is looked at, even at limit 0; a walled-in target is Unreachable at no limit.
        [Test]
        public void BlockedStartIsRejectedAtLimitZero_BlockedTargetIsUnreachableAtNoLimit()
        {
            var search = new GridPathfinder(M3GridPathfinderTests.Rows("...", ".#.", "..."));
            Assert.That(search.FindWithin(4, 0, 0), Is.EqualTo(GridPathStatus.Rejected));
            Assert.That(search.FindWithin(4, 4, 0), Is.EqualTo(GridPathStatus.Rejected));
            Assert.That(search.FindWithin(0, 4, long.MaxValue), Is.EqualTo(GridPathStatus.Unreachable));
            Assert.That(search.Path.Length, Is.Zero);
            Assert.That(search.FindWithin(0, 0, 0), Is.EqualTo(GridPathStatus.Found));
            Assert.That(search.Path.ToArray(), Is.EqualTo(new[] { 0 }));
            Assert.That(search.FindWithin(0, 8, 2828), Is.EqualTo(GridPathStatus.Unreachable));
            Assert.That(search.FindWithin(0, 8, 4000), Is.EqualTo(GridPathStatus.Found));
            Assert.That(search.TotalCostWide, Is.EqualTo(4000));
            Assert.That(search.FindWithin(0, 8, 3999), Is.EqualTo(GridPathStatus.Unreachable));
            Assert.That((search.Path.Length, search.TotalCostWide), Is.EqualTo((0, 0L)));
        }

        internal const string LanesPath = "Assets/WarCore/Maps/lanes-playtest.map.json";
        internal const string TerrainPath = "Assets/WarCore/Maps/terrain-demo.map.json";

        // The search a world built from this description moves with: weighted when the map has terrain.
        internal static GridPathfinder MapSearch(string path, out NavGrid grid, out bool weighted)
        {
            MapBakeResult baked = MapBake.Run(File.ReadAllBytes(path));
            WcMap map = WcMapReader.Read(baked.WcMapBytes);
            weighted = map.HasTerrain;
            if (!weighted) { grid = WcMapNavigation.Create(map); return new GridPathfinder(grid); }
            TerrainTables tables = TerrainDerivation.Derive(map, RwrPack.Load(baked.PackId));
            grid = tables.Nav;
            return new GridPathfinder(tables);
        }

        [TestCase(LanesPath, false, 2101)]
        [TestCase(TerrainPath, true, 2102)]
        public void MapFiles_ThousandRandomPairsEqualFindAndHoldTheLimitEdge(string path, bool weighted, int seed)
        {
            GridPathfinder search = MapSearch(path, out NavGrid grid, out bool isWeighted);
            Assert.That(isWeighted, Is.EqualTo(weighted), "the map's movement search is weighted");
            var random = new Random(seed);
            int found = 0, unreachable = 0, rejected = 0;
            for (int i = 0; i < 1000; i++)
            {
                int start = random.Next(grid.Count), target = random.Next(grid.Count);
                Count(Check(search, start, target, path + " " + start + "->" + target), ref found, ref unreachable, ref rejected);
            }
            TestContext.Out.WriteLine($"{path}: found={found} unreachable={unreachable} rejected={rejected}");
            Assert.That(found, Is.GreaterThan(0));
            Assert.That(unreachable + rejected, Is.GreaterThan(0));
        }

        private static void Count(GridPathStatus status, ref int found, ref int unreachable, ref int rejected)
        {
            if (status == GridPathStatus.Found) found++;
            else if (status == GridPathStatus.Unreachable) unreachable++;
            else rejected++;
        }

        // ---- acceptance 3: the limit really cuts the search ----

        // 512 x 512 open grid, target in the middle walled in on all eight sides, start 10 cells west. With a 30 m limit only cells
        // whose octile distance to the target is at most 30 can leave the heap with F <= limit: a 61 x 61 square, 3721 cells.
        [Test]
        public void SealedTargetOn512Grid_ThirtyMetreLimitExpandsAtMostTheSquareAroundTheTarget()
        {
            const int side = 512;
            var bits = new byte[side * side / 8];
            for (int i = 0; i < bits.Length; i++) bits[i] = 255;
            int target = 256 * side + 256, start = target - 10;
            foreach (int dz in new[] { -1, 0, 1 })
                foreach (int dx in new[] { -1, 0, 1 })
                {
                    if (dx == 0 && dz == 0) continue;
                    int blocked = target + dz * side + dx;
                    bits[blocked / 8] &= (byte)~(1 << (blocked % 8));
                }
            var search = new GridPathfinder(new NavGrid(side, side, default, default, bits));
            Assert.That(search.FindWithin(start, target, 30000), Is.EqualTo(GridPathStatus.Unreachable));
            int limited = search.Expanded;
            Assert.That(search.Find(start, target), Is.EqualTo(GridPathStatus.Unreachable));
            int full = search.Expanded;
            TestContext.Out.WriteLine($"512 sealed target: expanded with 30 m limit={limited}, without={full}");
            Assert.That(limited, Is.LessThanOrEqualTo(3721));
            Assert.That(limited, Is.GreaterThan(0));
            Assert.That(full, Is.EqualTo(side * side - 9), "the search without a limit expands every open cell");
            // A limit below the start's own estimate (octile 10 cells = 10000) expands nothing.
            Assert.That(search.FindWithin(start, target, 9999), Is.EqualTo(GridPathStatus.Unreachable));
            Assert.That(search.Expanded, Is.Zero);
        }

        // ---- acceptance 4: zero allocation ----

        [Test]
        public void HundredLimitedQueries_ZeroAllocationAfterWarmup()
        {
            NavGrid grid = WcMapNavigation.Create(M2DemoMap.Build());
            var search = new GridPathfinder(grid);
            var random = new Random(717);
            var starts = new int[100]; var targets = new int[100]; var limits = new long[100];
            int OpenCell()
            {
                int cell;
                do { cell = random.Next(grid.Count); } while (!grid.IsPassable(cell % grid.Width, cell / grid.Width));
                return cell;
            }
            int found = 0;
            for (int i = 0; i < 100; i++)
            {
                starts[i] = OpenCell(); targets[i] = OpenCell(); limits[i] = random.Next(0, 40000);
                if (search.FindWithin(starts[i], targets[i], limits[i]) == GridPathStatus.Found) found++;
            }
            Assert.That(found, Is.GreaterThan(0));
            Assert.That(found, Is.LessThan(100));
            for (int pass = 0; pass < 5; pass++)
                for (int i = 0; i < 100; i++) search.FindWithin(starts[i], targets[i], limits[i]);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++) search.FindWithin(starts[i], targets[i], limits[i]);
            Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.Zero);
        }

        // ---- acceptance 5: the port does not change the simulation ----

        // Calls the port ten times per step when active; the same stateless system sits in both worlds, so their hashes compare.
        private sealed class Probe : IRetainedGameplaySystem
        {
            private readonly IMoveOrders port;
            private readonly GridPathfinder reference;
            private readonly NavGrid grid;
            private readonly Random random;
            internal int Found, NotFound, AtCost, AtCostMinusOne;
            internal Probe(int order, IMoveOrders port, GridPathfinder reference, NavGrid grid, int seed)
            {
                Order = order; this.port = port; this.reference = reference; this.grid = grid;
                random = port == null ? null : new Random(seed);
            }
            public int Order { get; }
            public string StateTypeId => "test.ml2-probe";
            public int StateVersion => 1;
            public void Step(SimContext context)
            {
                if (port == null) return;
                for (int i = 0; i < 10; i++)
                {
                    SimVec3 from = Point(), to = Point();
                    long limit;
                    bool exists = reference.FindWithin(from, to, long.MaxValue) == GridPathStatus.Found;
                    long full = reference.TotalCostWide;
                    int pick = random.Next(4);
                    if (exists && pick == 0) { limit = full; AtCost++; }
                    else if (exists && pick == 1) { limit = full - 1; AtCostMinusOne++; }
                    else if (pick == 2) limit = long.MaxValue;
                    else limit = random.Next(0, 150000);
                    GridPathStatus expected = reference.FindWithin(from, to, limit);
                    bool ok = port.TryRouteCost(from, to, limit, out long cost);
                    Assert.That(ok, Is.EqualTo(expected == GridPathStatus.Found), "tick " + context.Clock.Tick + " query " + i);
                    Assert.That(cost, Is.EqualTo(ok ? reference.TotalCostWide : 0L), "tick " + context.Clock.Tick + " query " + i);
                    if (ok) Found++; else NotFound++;
                }
            }
            // A random point anywhere on the grid, a few just outside it.
            private SimVec3 Point()
            {
                SimVec3 centre = grid.Center(random.Next(grid.Count), default);
                long dx = random.Next(-32768, 32768), dz = random.Next(-32768, 32768);
                if (random.Next(20) == 0) dx -= (long)(grid.Width + 2) * SimFix.Scale;
                return new SimVec3(SimFix.FromRaw(centre.X.Raw + dx), default, SimFix.FromRaw(centre.Z.Raw + dz));
            }
            public void SaveState(SimStateWriter w) { }
            public void LoadState(SimStateReader r) { }
            public void WriteState(StateHash h) { }
        }

        private static IMoveOrders Bridge(M6World w) =>
            (IMoveOrders)typeof(SimpleAiSystem).GetField("moves", Private).GetValue(w.System<SimpleAiSystem>());

        // A fresh search over the mover's own grid (and terrain), so the port's answers can be compared with a direct search.
        private static GridPathfinder Reference(M6World w, out NavGrid grid)
        {
            InterimLocomotion locomotion = w.System<InterimLocomotion>();
            grid = (NavGrid)typeof(InterimLocomotion).GetField("grid", Private).GetValue(locomotion);
            var terrain = (TerrainTables)typeof(InterimLocomotion).GetField("terrain", Private).GetValue(locomotion);
            return terrain != null ? new GridPathfinder(terrain) : new GridPathfinder(grid);
        }

        // Free Requests-phase orders right before and right after the AI: no other Requests system between them and the AI.
        private static (int before, int after) AroundAi(M6World w)
        {
            int ai = w.System<SimpleAiSystem>().Order;
            var used = new SortedDictionary<int, bool>();
            foreach (object entry in (IEnumerable)typeof(Simulation).GetField("systems", Private).GetValue(w.Sim))
            {
                Type t = entry.GetType();
                used[(int)t.GetField("Order").GetValue(entry)] = (SimSystemPhase)t.GetField("Phase").GetValue(entry) == SimSystemPhase.Requests;
            }
            int before = ai - 1, after = ai + 1;
            while (used.ContainsKey(before)) { Assert.That(used[before], Is.False, "a Requests system sits right before the AI"); before--; }
            while (used.ContainsKey(after)) { Assert.That(used[after], Is.False, "a Requests system sits right after the AI"); after++; }
            return (before, after);
        }

        public static IEnumerable<TestCaseData> Worlds()
        {
            yield return new TestCaseData("m7").SetName("Port_TenCallsBeforeAndAfterTheAi_EveryTickHashUnchanged(m7)");
            yield return new TestCaseData("terrain").SetName("Port_TenCallsBeforeAndAfterTheAi_EveryTickHashUnchanged(terrain)");
        }

        private static M6World World(string which) => which == "m7"
            ? new M6World(DemoRecipes.M7(), DemoRecipes.M7MapBytes())
            : new M6World(DemoRecipes.TerrainMatch(), TerrainDemo.MapBytes());

        [TestCaseSource(nameof(Worlds))]
        public void Port_TenCallsBeforeAndAfterTheAi_EveryTickHashUnchanged(string which)
        {
            M6World quiet = World(which), asked = World(which);
            (int before, int after) = AroundAi(asked);
            IMoveOrders port = Bridge(asked);
            GridPathfinder reference = Reference(asked, out NavGrid grid);
            var first = new Probe(before, port, reference, grid, 5001);
            var second = new Probe(after, port, reference, grid, 5002);
            asked.Sim.RegisterGameplay("test.ml2-before", first, SimSystemPhase.Requests);
            asked.Sim.RegisterGameplay("test.ml2-after", second, SimSystemPhase.Requests);
            quiet.Sim.RegisterGameplay("test.ml2-before", new Probe(before, null, null, null, 0), SimSystemPhase.Requests);
            quiet.Sim.RegisterGameplay("test.ml2-after", new Probe(after, null, null, null, 0), SimSystemPhase.Requests);
            Assert.That(asked.Hash, Is.EqualTo(quiet.Hash), "tick 0");
            for (int tick = 1; tick <= 600; tick++)
            {
                quiet.Step(); asked.Step();
                if (asked.Hash != quiet.Hash) Assert.Fail("hash differs at tick " + tick);
            }
            int found = first.Found + second.Found, notFound = first.NotFound + second.NotFound;
            TestContext.Out.WriteLine($"{which}: found={found} notFound={notFound} atCost={first.AtCost + second.AtCost} atCostMinusOne={first.AtCostMinusOne + second.AtCostMinusOne}");
            Assert.That(found + notFound, Is.EqualTo(600 * 20));
            Assert.That(found, Is.GreaterThan(0));
            Assert.That(first.AtCostMinusOne + second.AtCostMinusOne, Is.GreaterThan(0));
        }

        // The port forwards the limit unchanged: at limit = cost it answers that cost, at cost - 1 it answers false.
        [Test]
        public void Port_ForwardsTheLimitAsIs()
        {
            M6World w = World("m7");
            IMoveOrders port = Bridge(w);
            GridPathfinder reference = Reference(w, out NavGrid grid);
            SimVec3 from = grid.Center(grid.Width * (grid.Depth / 2) + 3, default), to = grid.Center(grid.Width * (grid.Depth / 2) + grid.Width - 4, default);
            Assert.That(reference.FindWithin(from, to, long.MaxValue), Is.EqualTo(GridPathStatus.Found));
            long cost = reference.TotalCostWide;
            Assert.That(port.TryRouteCost(from, to, cost, out long answer), Is.True);
            Assert.That(answer, Is.EqualTo(cost));
            Assert.That(port.TryRouteCost(from, to, cost - 1, out answer), Is.False);
            Assert.That(answer, Is.Zero);
            Assert.That(port.TryRouteCost(from, to, long.MaxValue, out answer), Is.True);
            Assert.That(answer, Is.EqualTo(cost));
        }

        // Battles always run with crowd movement, so the bridge is built here over a straight-line mover (no navigation) of a
        // battle world, with that world's own deploy ground: it answers false, as does the mover.
        [Test]
        public void Port_WithoutNavigationAnswersFalse()
        {
            M6World w = World("m7");
            object bridge = Bridge(w);
            Type type = bridge.GetType();
            var straight = new InterimLocomotion(w.Sim.Entities);
            Assert.That(typeof(InterimLocomotion).GetField("grid", Private).GetValue(straight), Is.Null, "no navigation");
            ConstructorInfo make = type.GetConstructors(Private)[0];
            var args = new object[make.GetParameters().Length];
            args[0] = w.Sim; args[1] = straight; args[2] = w.Recipe;
            args[3] = type.GetField("ground", Private).GetValue(bridge); args[4] = type.GetField("groundRaw", Private).GetValue(bridge);
            var port = (IMoveOrders)make.Invoke(args);
            GridPathfinder reference = Reference(w, out NavGrid grid);
            SimVec3 from = grid.Center(grid.Width * (grid.Depth / 2) + 3, default), to = grid.Center(grid.Width * (grid.Depth / 2) + 5, default);
            Assert.That(reference.FindWithin(from, to, long.MaxValue), Is.EqualTo(GridPathStatus.Found), "a route exists on the world's grid");
            Assert.That(Bridge(w).TryRouteCost(from, to, long.MaxValue, out long cost), Is.True);
            Assert.That(port.TryRouteCost(from, to, long.MaxValue, out cost), Is.False);
            Assert.That(cost, Is.Zero);
            Assert.That(straight.TryRouteCost(from, to, long.MaxValue, out cost), Is.False);
            Assert.That(cost, Is.Zero);
        }

        // ---- acceptance 6: the default method ----

        private sealed class Minimal : IMoveOrders
        {
            public void Order(StableId unit, SimVec3 target) { }
            public void Stop(StableId unit) { }
            public bool TryGetOrder(StableId unit, out SimVec3 target) { target = default; return false; }
        }

        [Test]
        public void DefaultPort_AnswersFalse()
        {
            IMoveOrders orders = new Minimal();
            Assert.That(orders.TryRouteCost(default, default, long.MaxValue, out long cost), Is.False);
            Assert.That(cost, Is.Zero);
        }
    }

    internal static class Ml2SpanExtensions
    {
        internal static bool SameAs(this ReadOnlySpan<int> span, int[] other)
        {
            if (span.Length != other.Length) return false;
            for (int i = 0; i < other.Length; i++) if (span[i] != other[i]) return false;
            return true;
        }
    }
}
