using NUnit.Framework;
using WarCore.Sim;
using static WarCore.Tests.Ml6Nav;

namespace WarCore.Tests
{
    // ML-6 acceptance 2: hand vectors on graphs built in Sim (no .wcmap). Ground node = z * W + x; plate nodes follow.
    public sealed class Ml6VectorTests
    {
        private static PlateSlab Flat(ulong id, int x, int z, int w, int d, long top, params (int, int)[] holes) =>
            Ml5Plates.Flat(id, x, z, w, d, top, 32768, holes);

        // 2a fixture: ground 6 x 1 at height 0, cells 2 and 3 blocked; plate 1 on cells 2..3 with the given top.
        // Nodes: ground 0..5, plate 6 (x = 2) and 7 (x = 3).
        private static LayeredNav Corridor(long top) => Nav(Open(6, 1, (2, 0), (3, 0)), Flat(1, 2, 0, 2, 1, top));

        // 2a: up a 19660 raw step onto the plate and down again: 0 -> 1 -> 6 -> 7 -> 4 -> 5, five orthogonal steps.
        // One raw higher both seams are gone.
        [Test]
        public void CorridorOverALowPlate()
        {
            Found(new LayeredPathfinder(Corridor(19660)), 0, 5, 5000, 0, 1, 6, 7, 4, 5);
            var high = new LayeredPathfinder(Corridor(19661));
            Assert.That(high.Find(0, 5), Is.EqualTo(GridPathStatus.Unreachable));
            Assert.That(high.Path.Length, Is.EqualTo(0));
        }

        // 2b: plate on cell (1,0) of a 3 x 1 ground, sloped along z: its points are h0 on z line 0 and h1 on z line 1, on both
        // the edge x = 1 (to ground 0) and x = 2 (to ground 2). Both end points must be within the step, whichever is low.
        [TestCase(0, 19661, false)]
        [TestCase(19661, 0, false)]
        [TestCase(0, 19660, true)]
        [TestCase(19660, 0, true)]
        [TestCase(19660, 19660, true)]
        [TestCase(-19660, 19660, true)]
        [TestCase(-19661, 0, false)]
        public void BothEndPointsOfTheEdge(long h0, long h1, bool seam)
        {
            LayeredNav nav = Nav(Open(3, 1), Ml5Plates.AlongZ(1, 1, 0, 1, 1, new[] { h0, h1 }));
            Assert.That(Seams(nav, 3), Is.EqualTo(seam ? new[] { 0, 2 } : new int[0]));
            Assert.That(Seams(nav, 0), Is.EqualTo(seam ? new[] { 3 } : new int[0]));
            Assert.That(Seams(nav, 2), Is.EqualTo(seam ? new[] { 3 } : new int[0]));
            Assert.That(Seams(nav, 1), Is.Empty, "the ground under the plate is not its neighbour");
            Assert.That(new LayeredPathfinder(nav).Find(0, 3), Is.EqualTo(seam ? GridPathStatus.Found : GridPathStatus.Unreachable));
        }

        // 2c: ground 3 x 3 open only at (0,0); plate 1 on (1,1) at the ground's height. Diagonal neighbours on different layers
        // are not joined. Opening ground (1,0) gives the plate an orthogonal seam: 0 -> 1 -> 9.
        [Test]
        public void NoDiagonalBetweenLayers()
        {
            var only = new byte[9]; only[0] = 1;
            LayeredNav nav = Nav(Ground(3, 3, only), Flat(1, 1, 1, 1, 1, 0));
            Assert.That(new LayeredPathfinder(nav).Find(0, 9), Is.EqualTo(GridPathStatus.Unreachable));
            Assert.That(Seams(nav, 9), Is.Empty);
            only[1] = 1;
            LayeredNav open = Nav(Ground(3, 3, only), Flat(1, 1, 1, 1, 1, 0));
            Assert.That(Seams(open, 9), Is.EqualTo(new[] { 1 }));
            Found(new LayeredPathfinder(open), 0, 9, 2000, 0, 1, 9);
        }

        // 2d: inside one plate a diagonal needs both orthogonal cells of that plate walkable (plate 2 x 2 on a blocked
        // 2 x 2 ground: nodes 4 (0,0), 5 (1,0), 6 (0,1), 7 (1,1)). Four 1 x 1 plates at one height are joined only
        // orthogonally, by seams, never by a diagonal step.
        [Test]
        public void DiagonalsStayOnOnePlate()
        {
            TerrainTables blocked = Ground(2, 2, new byte[4]);
            PlateSlab p = Flat(1, 0, 0, 2, 2, 0);
            Found(new LayeredPathfinder(Nav(blocked, 0, (p, Costs(p, 1, (1, 0))))), 4, 7, 2000, 4, 6, 7);
            Found(new LayeredPathfinder(Nav(blocked, 0, (p, Costs(p, 1, (0, 1))))), 4, 7, 2000, 4, 5, 7);
            Found(new LayeredPathfinder(Nav(blocked, p)), 4, 7, 1414, 4, 7);
            LayeredNav four = Nav(blocked, Flat(1, 0, 0, 1, 1, 0), Flat(2, 1, 0, 1, 1, 0), Flat(3, 0, 1, 1, 1, 0), Flat(4, 1, 1, 1, 1, 0));
            Assert.That(Seams(four, 4), Is.EqualTo(new[] { 5, 6 }));
            Assert.That(Seams(four, 7), Is.EqualTo(new[] { 5, 6 }));
            // From 4: 5 and 6 tie on F and H; 5 leaves the heap first and becomes 7's parent.
            Found(new LayeredPathfinder(four), 4, 7, 2000, 4, 5, 7);
        }

        // 2e: a seam costs 1000 times the cost of the cell entered: from ground cost 1 onto a plate cell of cost 3.
        [Test]
        public void SeamCostIsTheEnteredCell()
        {
            PlateSlab p = Flat(1, 1, 0, 1, 1, 0);
            var nav = Nav(Ground(2, 1, new byte[] { 1, 0 }), 0, (p, Costs(p, 3)));
            Found(new LayeredPathfinder(nav), 0, 2, 3000, 0, 2);
            Found(new LayeredPathfinder(nav), 2, 0, 1000, 2, 0);
        }

        // 2f and 2g: ground 2 x 100, column x = 0 cost 3, column x = 1 blocked; plate 1 on x = 1, z 0..11, cost 1 (nodes
        // 200..211). From ground (0,0) to ground (0,11): onto the plate (1000), eleven plate steps (11000), off it (3000):
        // 15000. Only a heuristic scaled by the plate's cost 1 finds it: scaled by the ground's 3, every plate node's F is
        // above the ground route's 33000. Plate nodes' cells come from the node table: read off the node number (200..211
        // as z * 2 + x) they would sit near z = 100 and the plate route would again lose to the 33000 ground route.
        [Test]
        public void HeuristicUsesTheCheapestLayerAndTheNodeTable()
        {
            var cost = new byte[200];
            for (int z = 0; z < 100; z++) cost[z * 2] = 3;
            PlateSlab p = Flat(1, 1, 0, 1, 12, 0);
            var f = new LayeredPathfinder(Nav(Ground(2, 100, cost), 0, (p, Costs(p))));
            Found(f, 0, 22, 15000, 0, 200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 22);
        }

        // 2h (1): ground 2 x 3 open, plate 1 on 2 x 2 at the ground's height (plate nodes 6 (0,0), 7 (1,0), 8 (0,1),
        // 9 (1,1)); from plate (0,0) to ground (1,2) = 5. Two parents give 2414: ground 2 = (0,1) (seam 1000, then a ground
        // diagonal 1414; F 1000 + 1414, H 1414) and plate 9 = (1,1) (plate diagonal 1414, then a seam 1000; F 1414 + 1000,
        // H 1000). Equal F: the smaller H leaves first, although its node number is larger: path 6 -> 9 -> 5.
        [Test]
        public void SmallerHLeavesFirst()
        {
            LayeredNav nav = Nav(Open(2, 3), Flat(1, 0, 0, 2, 2, 0));
            Assert.That(Seams(nav, 6), Is.EqualTo(new[] { 1, 2 }));
            Found(new LayeredPathfinder(nav), 6, 5, 2414, 6, 9, 5);
        }

        // 2h (2): ground 2 x 2 with (1,1) blocked, plate 1 on (0,0) at the ground's height (node 4); from ground (0,1) = 2 to
        // ground (1,0) = 1. Ground 0 (same layer) and plate 4 (by a seam) both give 2000 with the same F and H: the smaller
        // node number, ground 0, leaves first: 2 -> 0 -> 1.
        [Test]
        public void EqualFAndHGoByNodeNumber()
        {
            LayeredNav nav = Nav(Open(2, 2, (1, 1)), Flat(1, 0, 0, 1, 1, 0));
            Assert.That(Seams(nav, 4), Is.EqualTo(new[] { 1, 2 }));
            Found(new LayeredPathfinder(nav), 2, 1, 2000, 2, 0, 1);
        }

        // 2i: start blocked or off the node range is Rejected, target likewise Unreachable.
        [Test]
        public void RejectedAndUnreachable()
        {
            var f = new LayeredPathfinder(Corridor(19660));
            foreach (int start in new[] { 2, 3, -1, 8 }) Assert.That(f.Find(start, 5), Is.EqualTo(GridPathStatus.Rejected), "start " + start);
            foreach (int target in new[] { 2, 3, -1, 8 }) Assert.That(f.Find(0, target), Is.EqualTo(GridPathStatus.Unreachable), "target " + target);
            Assert.That(f.Find(-1, -1), Is.EqualTo(GridPathStatus.Rejected));
            Found(f, 6, 6, 0, 6);
        }

        // 2j: a limit equal to the best cost finds the same path; one less finds nothing.
        [Test]
        public void FindWithinTheLimit()
        {
            var f = new LayeredPathfinder(Corridor(19660));
            Assert.That(f.FindWithin(0, 5, 5000), Is.EqualTo(GridPathStatus.Found));
            Assert.That((Path(f), f.TotalCostWide, f.TotalCost), Is.EqualTo((new[] { 0, 1, 6, 7, 4, 5 }, 5000L, 5000)));
            Assert.That(f.FindWithin(0, 5, 4999), Is.EqualTo(GridPathStatus.Unreachable));
            Assert.That(f.Path.Length, Is.EqualTo(0));
        }

        // 2k: the public queries on the 2a fixture, and a cutout cell on its own fixture.
        [Test]
        public void PublicQueries()
        {
            LayeredNav nav = Corridor(19660);
            Assert.That((nav.Count, nav.GroundCount, nav.LayerCount, nav.Width, nav.Depth), Is.EqualTo((8, 6, 2, 6, 1)));
            Assert.That((nav.LayerOf(6), nav.XOf(6), nav.ZOf(6), nav.LayerOf(7), nav.XOf(7), nav.ZOf(7)), Is.EqualTo((1, 2, 0, 1, 3, 0)));
            Assert.That((nav.LayerOf(4), nav.XOf(4), nav.ZOf(4)), Is.EqualTo((0, 4, 0)));
            for (int node = 0; node < nav.Count; node++)
                Assert.That(Node(nav, nav.LayerOf(node), nav.XOf(node), nav.ZOf(node)), Is.EqualTo(node), "round trip " + node);
            Assert.That(nav.TryNode(1, 1, 0, out int off), Is.False, "left of the plate");
            Assert.That(off, Is.EqualTo(-1));
            Assert.That(nav.TryNode(1, 4, 0, out _), Is.False, "right of the plate");
            Assert.That(nav.TryNode(0, 6, 0, out _), Is.False, "off the map");
            Assert.That(nav.TryNode(0, 0, 1, out _), Is.False, "off the map");
            Assert.That(nav.TryNode(2, 2, 0, out _), Is.False, "no layer 2");
            Assert.That(nav.TryNode(-1, 2, 0, out _), Is.False, "no layer -1");
            Assert.That(Node(nav, 0, 3, 0), Is.EqualTo(3), "ground node = NavGrid key");
            Assert.That(Passable(nav), Is.EqualTo(new[] { true, true, false, false, true, true, true, true }));
            Assert.That((nav.CostAt(6), nav.SpeedMilliAt(6), nav.CostAt(2), nav.SpeedMilliAt(2)), Is.EqualTo(((byte)1, (ushort)1000, (byte)0, (ushort)0)));
            var seams = new[] { new int[0], new[] { 6 }, new int[0], new int[0], new[] { 7 }, new int[0], new[] { 1 }, new[] { 4 } };
            for (int node = 0; node < nav.Count; node++) Assert.That(Seams(nav, node), Is.EqualTo(seams[node]), "seams of " + node);
            Assert.That(nav.TryLayerHeight(1, 2, 0, out long h) && h == 19660, Is.True);
            Assert.That(nav.TryLayerHeight(1, 4, 1, out h) && h == 19660, Is.True, "far corner of the plate");
            Assert.That(nav.TryLayerHeight(1, 1, 0, out _), Is.False, "left of the plate");
            Assert.That(nav.TryLayerHeight(1, 5, 0, out _), Is.False, "right of the plate");
            Assert.That(nav.TryLayerHeight(0, 0, 0, out h) && h == 0, Is.True);
            Assert.That(nav.TryLayerHeight(0, 6, 1, out h) && h == 0, Is.True, "far corner of the map");
            Assert.That(nav.TryLayerHeight(0, 7, 0, out _), Is.False);
            Assert.That(nav.TryLayerHeight(2, 2, 0, out _), Is.False);
            Assert.That(LayeredNav.StepRaw, Is.EqualTo(19660));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => nav.LayerOf(8));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => nav.Seam(1, 1));

            LayeredNav holed = Nav(Open(2, 1), Flat(1, 0, 0, 2, 1, 0, (1, 0)));
            Assert.That(holed.Count, Is.EqualTo(3));
            Assert.That(Node(holed, 1, 0, 0), Is.EqualTo(2));
            Assert.That(holed.TryNode(1, 1, 0, out _), Is.False, "cutout cell");
            Assert.That(Seams(holed, 2), Is.EqualTo(new[] { 1 }), "only the open ground beside the cutout's plate cell");
        }

        private static bool[] Passable(LayeredNav nav)
        {
            var open = new bool[nav.Count];
            for (int node = 0; node < nav.Count; node++) open[node] = nav.IsPassable(node);
            return open;
        }

        // 2l: a flat map's ground is the height given to the constructor (the bridge passes Min.Y), not 0.
        [Test]
        public void FlatGroundHeight()
        {
            const long floor = 131072;
            PlateSlab near = Flat(1, 1, 0, 1, 1, floor + 19660), far = Flat(1, 1, 0, 1, 1, floor + 19661);
            LayeredNav up = Nav(Open(2, 1, (1, 0)), floor, (near, Costs(near)));
            Assert.That(Seams(up, 0), Is.EqualTo(new[] { 2 }));
            Assert.That(up.TryLayerHeight(0, 0, 0, out long h) && h == floor, Is.True);
            Found(new LayeredPathfinder(up), 0, 2, 1000, 0, 2);
            LayeredNav off = Nav(Open(2, 1, (1, 0)), floor, (far, Costs(far)));
            Assert.That(Seams(off, 0), Is.Empty);
            // With a height field the given height is ignored.
            LayeredNav field = Nav(Ground(2, 1, new byte[] { 1, 0 }, new[] { (int)floor, (int)floor, (int)floor, (int)floor, (int)floor, (int)floor }), 0, (near, Costs(near)));
            Assert.That(Seams(field, 0), Is.EqualTo(new[] { 2 }));
        }
    }
}
