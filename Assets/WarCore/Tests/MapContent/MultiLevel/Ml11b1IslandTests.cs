using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Content;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.Ml11aWorld;
using static WarCore.Tests.Ml6Nav;

namespace WarCore.Tests
{
    // ML-11b-1 acceptance 11: which nodes can be walked between (LayeredReach.Islands) against the pathfinder, and the port the
    // AI reads it through (IPointReach.Connected, AiField.Connected).
    public sealed class Ml11b1IslandTests
    {
        // Two walkable nodes share an island exactly when the pathfinder finds a route from the one to the other; a node nobody
        // walks on has -1 and is found from nowhere.
        private static (int routes, int none) Compare(LayeredNav nav, IEnumerable<int> sources)
        {
            int[] islands = LayeredReach.Islands(nav);
            var finder = new LayeredPathfinder(nav);
            int routes = 0, none = 0;
            Assert.That(islands.Length, Is.EqualTo(nav.Count));
            for (int n = 0; n < nav.Count; n++) Assert.That(islands[n] >= 0, Is.EqualTo(nav.IsPassable(n)), "node " + n);
            foreach (int a in sources)
                for (int b = 0; b < nav.Count; b++)
                {
                    bool route = finder.Find(a, b) == GridPathStatus.Found;
                    Assert.That(islands[a] >= 0 && islands[a] == islands[b], Is.EqualTo(route), a + " to " + b);
                    if (route) routes++; else none++;
                }
            return (routes, none);
        }

        private static LayeredNav NavOf(byte[] bytes)
        {
            WcMap map = WcMapReader.Read(bytes);
            return WcMapLayeredNav.Build(map, null, map.ContentId);
        }

        // Islands are numbered from 0 in ascending order of their lowest node; a seam joins what it joins; a node nobody
        // walks on has -1.
        [Test]
        public void IslandsAreNumberedByTheirLowestNode()
        {
            Assert.That(LayeredReach.Islands(Nav(Open(5, 1, (1, 0), (3, 0)))), Is.EqualTo(new[] { 0, -1, 1, -1, 2 }));
            Assert.That(LayeredReach.Islands(Nav(Open(6, 1, (2, 0), (3, 0)))), Is.EqualTo(new[] { 0, 0, -1, -1, 1, 1 }));
            // The same row with a plate over the two blocked cells, joined to the ground at both ends: one island.
            LayeredNav bridged = Nav(Open(6, 1, (2, 0), (3, 0)), Ml5Plates.Flat(1, 2, 0, 2, 1, 19660, 32768));
            Assert.That(LayeredReach.Islands(bridged), Is.EqualTo(new[] { 0, 0, -1, -1, 0, 0, 0, 0 }));
            Assert.That(Compare(bridged, Enumerable.Range(0, bridged.Count)), Is.EqualTo((36, 28)));
            // A plate too high to step on is an island of its own, numbered after the ground's two.
            LayeredNav high = Nav(Open(6, 1, (2, 0), (3, 0)), Ml5Plates.Flat(1, 2, 0, 2, 1, 3 * S, 32768));
            Assert.That(LayeredReach.Islands(high), Is.EqualTo(new[] { 0, 0, -1, -1, 1, 1, 2, 2 }));
            Assert.Throws<ArgumentNullException>(() => LayeredReach.Islands(null));
        }

        // Two cells that touch at a corner only are not joined (a diagonal step needs both orthogonal cells); with one of the
        // orthogonal cells walkable they are.
        [Test]
        public void ACornerJoinsNothing()
        {
            LayeredNav corner = Nav(Ground(2, 2, new byte[] { 1, 0, 0, 1 }));
            Assert.That(LayeredReach.Islands(corner), Is.EqualTo(new[] { 0, -1, -1, 1 }));
            Assert.That(Compare(corner, Enumerable.Range(0, 4)), Is.EqualTo((2, 14)));
            LayeredNav round = Nav(Ground(2, 2, new byte[] { 1, 0, 1, 1 }));
            Assert.That(LayeredReach.Islands(round), Is.EqualTo(new[] { 0, -1, 0, 0 }));
            Assert.That(Compare(round, Enumerable.Range(0, 4)), Is.EqualTo((9, 7)));
        }

        // The fixture houses and the two shipped plate maps, from nodes of every layer (walkable and not) to every node.
        [Test]
        public void TheHousesMatchThePathfinder()
        {
            LayeredNav house = NavOf(House());
            // The ground, the ground under the stair (blocked), the floor, the stair.
            int[] from = { 0, Node(house, 0, 37, 31), Node(house, 0, 41, 31), Node(house, 1, 40, 30), Node(house, 1, 43, 33), Node(house, 2, 36, 31), Node(house, 2, 39, 32) };
            Assert.That(LayeredReach.Islands(house).Max(), Is.EqualTo(0), "the house is one island");
            Assert.That(Compare(house, from).routes, Is.EqualTo(6 * (house.Count - 8)));

            LayeredNav cut = NavOf(House(false));
            int[] islands = LayeredReach.Islands(cut);
            Assert.That(islands[0], Is.EqualTo(0));
            Assert.That(islands[Node(cut, 1, 40, 30)], Is.EqualTo(1), "the floor without a stair is its own island");
            Assert.That(islands.Max(), Is.EqualTo(1));
            Compare(cut, new[] { 0, Node(cut, 0, 41, 31), Node(cut, 1, 40, 30), Node(cut, 1, 43, 33) });

            LayeredNav walled = NavOf(Walled());
            Assert.That(LayeredReach.Islands(walled)[Node(walled, 1, 43, 33)], Is.EqualTo(-1), "the cell under the wall");
            Compare(walled, new[] { 0, Node(walled, 1, 43, 33), Node(walled, 1, 43, 32), Node(walled, 2, 38, 31) });

            LayeredNav assault = NavOf(PlateAssaultMap.Bytes());
            Compare(assault, Enumerable.Range(0, assault.Count).Where(n => n % 97 == 0 || assault.LayerOf(n) != 0 && n % 5 == 0));
            LayeredNav demo = NavOf(PlateDemoMap.Bytes());
            Compare(demo, Enumerable.Range(0, demo.Count).Where(n => n % 7 == 0));
        }

        // 600 seeded maps (up to 12 x 12, ground costs 0-5, 0-4 flat or sloped plates with holes and costs), three sources each,
        // to every node.
        [Test]
        public void SeededMapsMatchThePathfinder()
        {
            var rnd = new Random(11101);
            int routes = 0, none = 0, split = 0, joined = 0;
            for (int m = 0; m < 600; m++)
            {
                LayeredNav nav = Seeded(rnd);
                int[] islands = LayeredReach.Islands(nav);
                if (islands.Max() >= 1) split++;
                // A map where a plate's node shares an island with a ground node: a seam was walked.
                if (Enumerable.Range(nav.GroundCount, nav.Count - nav.GroundCount).Any(n => islands[n] >= 0 && Enumerable.Range(0, nav.GroundCount).Any(g => islands[g] == islands[n]))) joined++;
                (int r, int n0) = Compare(nav, new[] { rnd.Next(nav.Count), rnd.Next(nav.Count), rnd.Next(nav.Count) });
                routes += r; none += n0;
            }
            TestContext.Out.WriteLine("ML-11b-1 islands: routes " + routes + ", no route " + none + ", maps in pieces " + split + ", maps with a plate joined to the ground " + joined);
            Assert.That(routes, Is.GreaterThanOrEqualTo(20000));
            Assert.That(none, Is.GreaterThanOrEqualTo(5000));
            Assert.That(split, Is.GreaterThanOrEqualTo(100));
            Assert.That(joined, Is.GreaterThanOrEqualTo(100));
        }

        // Ml11aReachTests' generator.
        private static LayeredNav Seeded(Random rnd)
        {
            int w = rnd.Next(3, 13), d = rnd.Next(3, 13);
            var groundCost = new byte[w * d];
            for (int k = 0; k < groundCost.Length; k++) groundCost[k] = rnd.Next(7) == 0 ? (byte)0 : (byte)rnd.Next(1, 6);
            var slabs = new List<PlateSlab>();
            var costs = new List<byte[]>();
            int count = rnd.Next(5), solidCells = 0;
            for (int k = 0; k < count; k++)
            {
                int pw = rnd.Next(1, Math.Min(w, 5) + 1), pd = rnd.Next(1, Math.Min(d, 5) + 1);
                int px = rnd.Next(0, w - pw + 1), pz = rnd.Next(0, d - pd + 1);
                long from = new long[] { 0, 16384, 19660, 19661, 65536 }[rnd.Next(5)];
                int axis = rnd.Next(3);
                var points = new int[(pw + 1) * (pd + 1)];
                for (int j = 0; j <= pd; j++)
                    for (int i = 0; i <= pw; i++) points[j * (pw + 1) + i] = (int)(from + 16384 * (axis == 1 ? i : axis == 2 ? j : 0));
                var solid = new bool[pw * pd];
                var cost = new byte[pw * pd];
                for (int c = 0; c < solid.Length; c++)
                {
                    solid[c] = rnd.Next(10) != 0;
                    if (solid[c] && rnd.Next(10) != 0) cost[c] = (byte)rnd.Next(1, 6);
                }
                int cells = solid.Count(s => s);
                if (solidCells + cells > w * d) break;
                solidCells += cells;
                slabs.Add(new PlateSlab((ulong)k + 1, px, pz, pw, pd, points, 32768, solid));
                costs.Add(cost);
            }
            return new LayeredNav(Ground(w, d, groundCost), 0, slabs, costs.ToArray(), costs.Select(Speeds).ToArray());
        }

        private static IPointReach Reach(M6World w) =>
            (IPointReach)typeof(AiField).GetField("reach", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(Field(w));

        // The port: two places are connected when both are walkable cells of their layers and share an island, in either order.
        [Test]
        public void ThePortAnswersForPlacesOnLayers()
        {
            var units = new[] { Walker(1, 30500, 31500) };
            M6World w = World(units, new[] { PointUp(1, 41500, 32500) });
            IPointReach reach = Reach(w);
            SimVec3 ground = Flat(30500, 31500), floor = Up(41500, 32500), under = Flat(41500, 32500), stair = new SimVec3(SimFix.Ratio(38500, 1000), SimFix.Ratio(1875, 1000), SimFix.Ratio(31500, 1000));
            Assert.That(reach.Connected(ground, 0, floor, 1), Is.True, "up the stair");
            Assert.That(reach.Connected(floor, 1, ground, 0), Is.True, "and down");
            Assert.That(reach.Connected(ground, 0, under, 0), Is.True);
            Assert.That(reach.Connected(ground, 0, stair, 2), Is.True);
            Assert.That(reach.Connected(floor, 1, floor, 1), Is.True);
            // The layer is read, not the height: the floor's place on the ground's layer is the cell under it.
            Assert.That(reach.Connected(ground, 0, floor, 0), Is.True);
            // Not a walkable cell: the ground under the stair, the floor's hole, a place off the floor, a place off the map.
            Assert.That(reach.Connected(ground, 0, Flat(37500, 31500), 0), Is.False);
            Assert.That(reach.Connected(Flat(37500, 31500), 0, ground, 0), Is.False);
            Assert.That(reach.Connected(ground, 0, Up(42500, 31500), 1), Is.False);
            Assert.That(reach.Connected(ground, 0, Up(30500, 31500), 1), Is.False);
            Assert.That(reach.Connected(ground, 0, Flat(-500, 31500), 0), Is.False);
            Assert.That(reach.Connected(Flat(90500, 31500), 0, ground, 0), Is.False);
            // A layer the map does not have, and the layer of a unit in the air.
            Assert.That(reach.Connected(ground, 0, floor, 3), Is.False);
            Assert.That(reach.Connected(ground, 0, floor, 65535), Is.False);
            Assert.That(reach.Connected(floor, 65535, ground, 0), Is.False);
            Assert.That(reach.Connected(ground, -1, ground, 0), Is.False);
            Assert.That(Field(w).Connected(ground, 0, floor, 1), Is.True);
            Assert.That(Field(w).Connected(ground, 0, floor, 65535), Is.False);

            // Without the stair nothing joins the floor and the ground; each is still joined to itself.
            M6World cut = World(units, new[] { PointDown(1, 50500, 32500) }, stair: false);
            Assert.That(Reach(cut).Connected(ground, 0, floor, 1), Is.False);
            Assert.That(Reach(cut).Connected(floor, 1, ground, 0), Is.False);
            Assert.That(Reach(cut).Connected(floor, 1, Up(40500, 30500), 1), Is.True);
            Assert.That(Reach(cut).Connected(ground, 0, under, 0), Is.True);
            Assert.That(Field(cut).Connected(ground, 0, floor, 1), Is.False);

            // The cell under a wall on the floor.
            M6World walled = World(units, new[] { PointUp(1, 41500, 32500) }, map: Walled());
            Assert.That(Reach(walled).Connected(ground, 0, Up(43500, 33500), 1), Is.False);
            Assert.That(Reach(walled).Connected(ground, 0, Up(43500, 32500), 1), Is.True);
        }

        // Without floor plates the field has no port and every place is connected, as before.
        [Test]
        public void WithoutPlatesEverythingIsConnected()
        {
            MatchPack pack = M6World.Pack(new[] { PointDown(1, 41500, 32500) }, interval: 1000000, think: 10, bases: RespawnWorld.Bases(new SpawnAnchor(1, RespawnWorld.P(5, 30))));
            var flat = new M6World(M6World.Endless(RespawnWorld.Recipe(new[] { Walker(1, 30500, 31500) }, pack, false)), Ml9bMatchTests.Arena(false));
            Assert.That(Reach(flat), Is.Null);
            Assert.That(Field(flat).Connected(Flat(30500, 31500), 0, Up(41500, 32500), 1), Is.True);
            Assert.That(Field(flat).Connected(Flat(-500, 31500), 7, Flat(30500, 31500), 65535), Is.True);
        }
    }
}
