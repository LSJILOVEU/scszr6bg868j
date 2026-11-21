using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Content.MapTools;
using WarCore.Sim;
using static WarCore.Tests.Trv2Maps;
using static WarCore.Tests.Trv3Maps;

namespace WarCore.Tests
{
    // TRV-3: the reach table follows links backwards and equals A* node by node (T3-08, 09, 12, 15).
    public sealed class Trv3ReachTests
    {
        // CostFrom(n) = the A* total from n to the target, -1 where A* finds no route.
        private static int Agree(LayeredNav nav, int target, string what)
        {
            var reach = new LayeredReach(nav, target);
            var f = new LayeredPathfinder(nav);
            int found = 0;
            for (int n = 0; n < nav.Count; n++)
            {
                long want = f.Find(n, target) == GridPathStatus.Found ? f.TotalCostWide : -1;
                Assert.That(reach.CostFrom(n), Is.EqualTo(want), what + ": node " + n + " to " + target);
                if (want > 0) found++;
            }
            return found;
        }

        private static int[] Targets(LayeredNav nav, int count, int seed)
        {
            int[] walkable = Enumerable.Range(0, nav.Count).Where(nav.IsPassable).ToArray();
            var r = new Rng((ulong)seed);
            return Enumerable.Range(0, count).Select(_ => walkable[r.Next(walkable.Length)]).ToArray();
        }

        [Test]
        public void T3_08_ReachEqualsAStarOnTheFixtures()
        {
            foreach (var (name, d) in new[] { ("F-box", Box1()), ("F-window", Window()), ("F-low-wall", LowWall()), ("F-box-wall", BoxWall()) })
            {
                LayeredNav nav = Linked(d);
                int[] targets = Targets(nav, 4, name.Length).Concat(new[] { Enumerable.Range(0, nav.Count).Last(nav.IsPassable) }).ToArray();
                foreach (int t in targets) Assert.That(Agree(nav, t, name), Is.GreaterThan(100), name);
            }
        }

        [Test]
        public void T3_08_ReachEqualsAStarOn200RandomMaps()
        {
            var r = new Rng(20261122);
            int attempts = 0, links = 0, oneWay = 0;
            for (int m = 0; m < 200; m++)
            {
                var (_, nav) = NextLinked(r, m % 2 == 0 ? Rules : Wide, ref attempts);
                links += nav.LinkCount;
                for (int i = 0; i < nav.LinkCount; i++)
                    if (!nav.TryLink(nav.Link(i).To, nav.Link(i).From, out _)) oneWay++;
                foreach (int t in Targets(nav, 5, m)) Agree(nav, t, "map " + m);
            }
            TestContext.WriteLine("TRV-3 reach: 200 maps from " + attempts + " attempts, " + links + " links, " + oneWay + " one way");
            Assert.That(links, Is.GreaterThan(2000));
            Assert.That(oneWay, Is.GreaterThan(200), "the maps have one-way links");
        }

        // T3-09: a one-way link is one way in the table too.
        [Test]
        public void T3_09_OneWay()
        {
            LayeredNav nav = Linked(BoxWall());
            int south = Node(nav, 0, 8, 6), north = Node(nav, 0, 8, 9);
            Assert.That(new LayeredReach(nav, north).CostFrom(south), Is.EqualTo(7000), "over the box");
            Assert.That(new LayeredReach(nav, south).CostFrom(north), Is.EqualTo(10414), "around the wall");
            LayeredNav plain = Plain(BoxWall());
            Assert.That(new LayeredReach(plain, north).CostFrom(south), Is.EqualTo(10414), "without links both ways go around");
        }

        // T3-12: one link per (from, to) pair, never beside a walking step or a seam.
        [Test]
        public void T3_12_OneLinkPerPairAndNeverBesideAStep()
        {
            var r = new Rng(20261123);
            int attempts = 0, checkedPairs = 0;
            for (int m = 0; m < 200; m++)
            {
                var (_, nav) = NextLinked(r, m % 2 == 0 ? Rules : Wide, ref attempts);
                for (int i = 0; i < nav.LinkCount; i++)
                {
                    NavLink l = nav.Link(i);
                    Assert.That(nav.TryLink(l.From, l.To, out int at), Is.True);
                    Assert.That(at, Is.EqualTo(i));
                }
                for (int u = 0; u < nav.Count; u++)
                    foreach (var (v, _) in Edges(nav, u, -1, false))
                    {
                        Assert.That(nav.TryLink(u, v, out int none), Is.False, "a step or seam " + u + " -> " + v + " is not a link");
                        Assert.That(none, Is.EqualTo(-1));
                        checkedPairs++;
                    }
            }
            Assert.That(checkedPairs, Is.GreaterThan(100000));
        }

        // A graph where every walkable node has up to 8 links: ground (cell costs 1 to 3) and a plate 2 m above it (cost 2, no
        // seams), links to the same
        // cell and the four orthogonal cells on the other layer (span 1) and to the cells two east, two west and two north on
        // the same layer (span 2).
        internal static LayeredNav Dense(int size)
        {
            PlateSlab plate = Ml5Plates.Flat(1, 0, 0, size, size, 2 * M, M);
            var cost = new byte[size * size];
            for (int k = 0; k < cost.Length; k++) cost[k] = (byte)((k % size * 7 + k / size * 3) % 3 + 1);
            LayeredNav bare = Ml6Nav.Nav(Ml6Nav.Ground(size, size, cost), 0, (plate, Ml6Nav.Costs(plate, 2)));
            var links = new List<NavLink>();
            for (int n = 0; n < bare.Count; n++)
            {
                int layer = bare.LayerOf(n), x = bare.XOf(n), z = bare.ZOf(n), other = 1 - layer;
                foreach (var (l, dx, dz, span) in new[] { (other, 0, 0, 1), (other, 1, 0, 1), (other, -1, 0, 1), (other, 0, 1, 1), (other, 0, -1, 1),
                    (layer, 2, 0, 2), (layer, -2, 0, 2), (layer, 0, 2, 2) })
                    if (bare.TryNode(l, x + dx, z + dz, out int to)) links.Add(new NavLink(n, to, span, (n + to) % 5 * 100, 0, 1, 0, NavLink.NoGap));
            }
            return bare.WithLinks(links.OrderBy(k => k.From).ThenBy(k => k.To).ToArray());
        }

        // T3-15: the heaps hold every push on the densest graph (the reach heap never grows).
        [Test]
        public void T3_15_TheDenseGraphFitsTheHeaps()
        {
            LayeredNav nav = Dense(24);
            Assert.That(nav.LinkFromCount(Node(nav, 0, 10, 10)), Is.EqualTo(8));
            Assert.That(nav.LinkFromCount(Node(nav, 1, 10, 10)), Is.EqualTo(8));
            foreach (int t in new[] { Node(nav, 0, 0, 0), Node(nav, 1, 23, 23), Node(nav, 1, 11, 12) })
                Assert.That(Agree(nav, t, "dense"), Is.EqualTo(nav.Count - 1));
        }

        // The heaps are sized for every push: eight per node, the seams, the links, the start.
        [Test]
        public void T3_15_TheHeapsCountTheLinks()
        {
            foreach (LayeredNav nav in new[] { Dense(24), Linked(BoxWall()), Plain(BoxWall()) })
            {
                long seams = Enumerable.Range(0, nav.Count).Sum(n => (long)nav.SeamCount(n));
                long want = 8L * nav.Count + seams + nav.LinkCount + 1;
                Assert.That(new LayeredPathfinder(nav).HeapCapacity, Is.EqualTo(want), "A*");
                int target = Enumerable.Range(0, nav.Count).First(nav.IsPassable);
                Assert.That(new LayeredReach(nav, target).HeapCapacity, Is.EqualTo(want), "reach");
            }
        }
    }
}
