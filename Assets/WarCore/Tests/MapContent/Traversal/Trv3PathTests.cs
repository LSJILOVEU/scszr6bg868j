using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Content.MapTools;
using WarCore.Sim;
using static WarCore.Tests.Trv2Maps;
using static WarCore.Tests.Trv3Maps;

namespace WarCore.Tests
{
    // TRV-3: A* takes links (T3-04..07, 11, 17, 21). Every cell of the fixtures costs 1, so a step costs 1000 (1414
    // diagonally), an edge link 1000 + its path cost and an over link 2000 + its path cost. trv-test's path costs: step 0,
    // climb 2000, drop 1000, vault 2000.
    public sealed class Trv3PathTests
    {
        private static void Found(LayeredPathfinder f, int from, int to, long cost, params int[] path)
        {
            Assert.That(f.Find(from, to), Is.EqualTo(GridPathStatus.Found));
            Assert.That(f.TotalCostWide, Is.EqualTo(cost), "cost");
            if (path.Length > 0) Assert.That(PathOf(f), Is.EqualTo(path), "path");
        }

        [Test]
        public void TheFixturesCostOnePerCell()
        {
            foreach (MapDesc d in new[] { Box1(), WindowDoor(), BoxWall(), LowWall() })
            {
                LayeredNav nav = Linked(d);
                Assert.That(Enumerable.Range(0, nav.Count).Where(nav.IsPassable).All(n => nav.CostAt(n) == 1), Is.True);
            }
        }

        // T3-04: (8, 5) walks to (8, 7) (2000), climbs onto the box (1000 + 2000).
        [Test]
        public void T3_04_OntoTheBox()
        {
            LayeredNav nav = Linked(Box1());
            var f = new LayeredPathfinder(nav);
            int top = Node(nav, 1, 8, 8);
            Found(f, Node(nav, 0, 8, 5), top, 5000, Node(nav, 0, 8, 5), Node(nav, 0, 8, 6), Node(nav, 0, 8, 7), top);
            Assert.That(nav.TryLink(Node(nav, 0, 8, 7), top, out _), Is.True, "the last step is a link");
            Assert.That(f.Find(top, Node(nav, 0, 8, 5)), Is.EqualTo(GridPathStatus.Found));
            Assert.That(f.TotalCostWide, Is.EqualTo(1000 + 1000 + 2000), "down: drop to (8, 7) (1000 + 1000), walk 2000");
            Assert.That(new LayeredPathfinder(Plain(Box1())).Find(Node(nav, 0, 8, 5), top), Is.EqualTo(GridPathStatus.Unreachable), "no route without links");
        }

        // T3-05: through the window: walk 2000, vault 2000 + 2000, walk 2000 = 8000. Around through (15, 8): (8, 5) to
        // (15, 7) is 2 diagonal + 5 straight = 7828, (15, 7) to (15, 9) 2000, (15, 9) to (8, 11) 7828: 17656. With the vault
        // costing 11656 both are 17656; with 11657 the walk around is cheaper by 1.
        [Test]
        public void T3_05_ThroughTheWindowUntilItCostsMore()
        {
            WcMap map = Bake(WindowDoor());
            LayeredNav nav = Linked(map);
            int from = Node(nav, 0, 8, 5), to = Node(nav, 0, 8, 11);
            var f = new LayeredPathfinder(nav);
            Found(f, from, to, 8000, from, Node(nav, 0, 8, 6), Node(nav, 0, 8, 7), Node(nav, 0, 8, 9), Node(nav, 0, 8, 10), to);

            var dearer = new LayeredPathfinder(Linked(map, WithVaultCost(11657)));
            Found(dearer, from, to, 17656);
            Assert.That(PathOf(dearer), Does.Contain(Node(nav, 0, 15, 8)), "around");

            var level = new LayeredPathfinder(Linked(map, WithVaultCost(11656)));
            Found(level, from, to, 17656);
            Assert.That(PathOf(level), Does.Contain(Node(nav, 0, 8, 9)), "a tie keeps the window (the existing tie rule, recorded)");
            Assert.That(PathOf(level).Length, Is.EqualTo(6));
        }

        // T3-06: the box gets over the wall one way only. South to north: climb from (8, 6) (3000), vault the wall down to
        // (8, 9) (2000 + 2000) = 7000. North to south: around the east end of the wall: (8, 9) to (12, 9) 4000, (12, 9) to
        // (12, 7) 2000, (12, 7) to (8, 6) 1 diagonal + 3 straight = 4414: 10414.
        [Test]
        public void T3_06_OverTheWallOneWay()
        {
            LayeredNav nav = Linked(BoxWall());
            var f = new LayeredPathfinder(nav);
            int south = Node(nav, 0, 8, 6), north = Node(nav, 0, 8, 9), top = Node(nav, 1, 8, 7);
            Found(f, south, north, 7000, south, top, north);
            Found(f, north, south, 10414);
            Assert.That(PathOf(f), Does.Contain(Node(nav, 0, 12, 8)), "around the east end");
            Assert.That(PathOf(f).Contains(top), Is.False, "not over the box");
        }

        // T3-07: the heuristic still never overestimates: A* equals a plain Dijkstra on every pair.
        [Test]
        public void T3_07_AStarEqualsDijkstraOnEveryPair()
        {
            foreach (MapDesc d in new[] { Window(), LowWall() })
            {
                LayeredNav nav = Linked(d);
                Assert.That(nav.LinkCount, Is.GreaterThan(0));
                var f = new LayeredPathfinder(nav);
                int pairs = 0;
                for (int a = 0; a < nav.Count; a++)
                {
                    if (!nav.IsPassable(a)) continue;
                    long[] dist = Dijkstra(nav, a);
                    for (int b = 0; b < nav.Count; b++)
                    {
                        if (!nav.IsPassable(b)) continue;
                        GridPathStatus s = f.Find(a, b);
                        Assert.That(s == GridPathStatus.Found ? f.TotalCostWide : -1, Is.EqualTo(dist[b]), a + " -> " + b);
                        pairs++;
                    }
                }
                Assert.That(pairs, Is.GreaterThan(50000));
            }
        }

        // T3-11: the limit with a link on the route.
        [Test]
        public void T3_11_TheLimitCountsTheLink()
        {
            LayeredNav nav = Linked(BoxWall());
            var f = new LayeredPathfinder(nav);
            int south = Node(nav, 0, 8, 6), north = Node(nav, 0, 8, 9);
            Assert.That(f.FindWithin(south, north, 7000), Is.EqualTo(GridPathStatus.Found));
            Assert.That(f.TotalCostWide, Is.EqualTo(7000));
            Assert.That(f.FindWithin(south, north, 6999), Is.EqualTo(GridPathStatus.Unreachable));
        }

        // T3-17: one link left out.
        [Test]
        public void T3_17_OneLinkLeftOut()
        {
            LayeredNav nav = Linked(WindowDoor());
            var f = new LayeredPathfinder(nav);
            int from = Node(nav, 0, 8, 5), to = Node(nav, 0, 8, 11);
            Assert.That(nav.TryLink(Node(nav, 0, 8, 7), Node(nav, 0, 8, 9), out int window), Is.True);
            Assert.That(nav.TryLink(Node(nav, 0, 8, 9), Node(nav, 0, 8, 7), out int back), Is.True);
            Assert.That(f.FindWithin(from, to, long.MaxValue, window), Is.EqualTo(GridPathStatus.Found));
            Assert.That(f.TotalCostWide, Is.EqualTo(17656), "around");
            Assert.That(f.FindWithin(from, to, long.MaxValue, back), Is.EqualTo(GridPathStatus.Found));
            Assert.That(f.TotalCostWide, Is.EqualTo(8000), "a link off the route changes nothing");
            Assert.That(PathOf(f).Length, Is.EqualTo(6));
            Assert.That(f.FindWithin(from, to, long.MaxValue, -1), Is.EqualTo(GridPathStatus.Found));
            Assert.That(f.TotalCostWide, Is.EqualTo(8000));

            LayeredNav closed = Linked(Window());
            Assert.That(closed.TryLink(Node(closed, 0, 8, 7), Node(closed, 0, 8, 9), out int only), Is.True);
            var g = new LayeredPathfinder(closed);
            Assert.That(g.FindWithin(Node(closed, 0, 8, 5), Node(closed, 0, 8, 11), long.MaxValue, only), Is.EqualTo(GridPathStatus.Unreachable), "no other way");
            Assert.That(g.Find(Node(closed, 0, 8, 5), Node(closed, 0, 8, 11)), Is.EqualTo(GridPathStatus.Found));

            Assert.Throws<ArgumentOutOfRangeException>(() => f.FindWithin(from, to, long.MaxValue, -2));
            Assert.Throws<ArgumentOutOfRangeException>(() => f.FindWithin(from, to, long.MaxValue, nav.LinkCount));
            var plain = new LayeredPathfinder(Plain(WindowDoor()));
            Assert.Throws<ArgumentOutOfRangeException>(() => plain.FindWithin(from, to, long.MaxValue, 0), "no table: only -1");
            Assert.That(plain.FindWithin(from, to, long.MaxValue, -1), Is.EqualTo(GridPathStatus.Found));
            Assert.That(plain.TotalCostWide, Is.EqualTo(17656));
        }

        // T3-21: edge links only: the window cannot be used, the walk goes around.
        [Test]
        public void T3_21_EdgeOnlyWalksAround()
        {
            LayeredNav nav = Linked(WindowDoor(), null, TraversalShapes.Edge);
            var f = new LayeredPathfinder(nav);
            Found(f, Node(nav, 0, 8, 5), Node(nav, 0, 8, 11), 17656);
        }

        // The new overload hands the no-traverse zones to LinkDerive: a zone over the box takes its links away.
        [Test]
        public void TheZonesReachTheMeasure()
        {
            LayeredNav zoned = Linked(BoxWall(), null, TraversalShapes.All, new NoTraverseZone(8, 7, 1, 1));
            Assert.That(zoned.LinkCount, Is.EqualTo(Linked(BoxWall()).LinkCount - 9), "3 climbs, 3 drops, the vault off the box and the 2 vaults across it");
            var f = new LayeredPathfinder(zoned);
            Found(f, Node(zoned, 0, 8, 6), Node(zoned, 0, 8, 9), 10414);
        }

        // D7: the new overload builds the graph twice; the time on a 512 x 512 map with 4096 boxes, old and new (report only).
        [Test, Explicit, Category("Manual")]
        public void D7_BuildTimes()
        {
            var plates = new List<MapPlate>();
            for (int j = 0; j < 64; j++)
                for (int i = 0; i < 64; i++) plates.Add(Box((ulong)plates.Count + 1, 4 + 8 * i, 4 + 8 * j, M));
            WcMap map = Bake(Make(512, 512, null, plates.ToArray()));
            for (int round = 0; round < 3; round++)
            {
                var clock = Stopwatch.StartNew();
                LayeredNav old = WcMapLayeredNav.Build(map, Test.Terrain, Test.Id);
                long oldMs = clock.ElapsedMilliseconds;
                clock.Restart();
                LayeredNav linked = Linked(map);
                long newMs = clock.ElapsedMilliseconds;
                TestContext.WriteLine("TRV-3 D7 round " + round + ": old " + oldMs + " ms, new " + newMs + " ms, " + linked.LinkCount + " links, nodes " + old.Count);
            }
        }

        // The link prices the cell it enters: ground 0..4 with (3, 0) blocked and cost 4 at (4, 0); a link (2, 0) -> (4, 0),
        // span 2, path cost 7: 1000 + 1000 to walk to (2, 0), then 2000 x 4 + 7.
        [Test]
        public void TheLinkPricesTheCellItEnters()
        {
            TerrainTables g = Ml6Nav.Ground(5, 1, new byte[] { 1, 1, 1, 0, 4 });
            var nav = new LayeredNav(g, 0, new PlateSlab[0], new byte[0][], new ushort[0][], new[] { new NavLink(2, 4, 2, 7, 0, 1, 0, NavLink.NoGap) });
            var f = new LayeredPathfinder(nav);
            Found(f, 0, 4, 10007, 0, 1, 2, 4);
            Assert.That(new LayeredReach(nav, 4).CostFrom(0), Is.EqualTo(10007));
            Assert.That(f.Find(4, 0), Is.EqualTo(GridPathStatus.Unreachable), "one way");
            Assert.That(f.FindWithin(0, 4, 10007), Is.EqualTo(GridPathStatus.Found));
            Assert.That(f.FindWithin(0, 4, 10006), Is.EqualTo(GridPathStatus.Unreachable));
        }

        // Writes the demonstration paths of the hand-off picture (one line per path: name, cost, then layer:x:z per node with
        // "*" before a node reached over a link) to the folder in TRV3_OUT. Not part of any run.
        [Test, Explicit, Category("Manual")]
        public void WriteDemoPaths()
        {
            string dir = Environment.GetEnvironmentVariable("TRV3_OUT");
            if (string.IsNullOrEmpty(dir)) Assert.Ignore("TRV3_OUT not set");
            var lines = new List<string>();
            void Add(string name, MapDesc d, int fl, int fx, int fz, int tl, int tx, int tz, int skip = -2)
            {
                LayeredNav nav = Linked(d);
                var f = new LayeredPathfinder(nav);
                int from = Node(nav, fl, fx, fz), to = Node(nav, tl, tx, tz);
                int s = skip == -2 ? -1 : skip;
                Assert.That(f.FindWithin(from, to, long.MaxValue, s), Is.EqualTo(GridPathStatus.Found), name);
                int[] path = PathOf(f);
                var parts = new List<string> { name, f.TotalCostWide.ToString() };
                for (int i = 0; i < path.Length; i++)
                    parts.Add((i > 0 && nav.TryLink(path[i - 1], path[i], out _) ? "*" : "") + nav.LayerOf(path[i]) + ":" + nav.XOf(path[i]) + ":" + nav.ZOf(path[i]));
                lines.Add(string.Join(" ", parts));
            }
            Add("box", Box1(), 0, 8, 5, 1, 8, 8);
            Add("window", WindowDoor(), 0, 8, 5, 0, 8, 11);
            LayeredNav door = Linked(WindowDoor());
            door.TryLink(Node(door, 0, 8, 7), Node(door, 0, 8, 9), out int window);
            Add("window-skipped", WindowDoor(), 0, 8, 5, 0, 8, 11, window);
            Add("low-wall", LowWall(), 0, 8, 5, 0, 8, 11);
            Add("box-wall-out", BoxWall(), 0, 8, 6, 0, 8, 9);
            Add("box-wall-back", BoxWall(), 0, 8, 9, 0, 8, 6);
            System.IO.File.WriteAllLines(System.IO.Path.Combine(dir, "demo-paths.txt"), lines);
        }
    }
}
