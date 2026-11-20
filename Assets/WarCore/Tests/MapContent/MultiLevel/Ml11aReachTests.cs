using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Sim;
using static WarCore.Tests.Ml6Nav;

namespace WarCore.Tests
{
    // ML-11a acceptance 10: the cost table to one target (LayeredReach) against the pathfinder it stands in for.
    public sealed class Ml11aReachTests
    {
        private const long M = SimFix.Scale;

        // Every node's cost equals the pathfinder's total to that target, or -1 where the pathfinder finds no route.
        private static (int found, int none) Compare(LayeredNav nav, int target)
        {
            var reach = new LayeredReach(nav, target);
            var finder = new LayeredPathfinder(nav);
            int found = 0, none = 0;
            Assert.That(reach.Target, Is.EqualTo(target));
            for (int n = 0; n < nav.Count; n++)
            {
                bool route = finder.Find(n, target) == GridPathStatus.Found;
                Assert.That(reach.CostFrom(n), Is.EqualTo(route ? finder.TotalCostWide : -1L), "node " + n + " to " + target);
                if (route) found++; else none++;
            }
            return (found, none);
        }

        // One row of six ground cells with two blocked, a plate over them joined at both ends (Ml6AllocationTests' graph):
        // nodes 0..5 the ground, 6 and 7 the plate over cells 2 and 3. From cell 0 to cell 5 the only way is over the plate.
        [Test]
        public void TheTableCrossesSeams()
        {
            LayeredNav nav = Nav(Open(6, 1, (2, 0), (3, 0)), Ml5Plates.Flat(1, 2, 0, 2, 1, 19660, 32768));
            var reach = new LayeredReach(nav, 5);
            Assert.That(Enumerable.Range(0, nav.Count).Select(reach.CostFrom).ToArray(),
                Is.EqualTo(new long[] { 5000, 4000, -1, -1, 1000, 0, 3000, 2000 }));
            Assert.That(Compare(nav, 5), Is.EqualTo((6, 2)));
            Assert.That(Compare(nav, 6), Is.EqualTo((6, 2)));
        }

        // A target that is not walkable is reached from nowhere, itself included; a target off the graph is refused.
        [Test]
        public void ABlockedTargetIsReachedFromNowhere()
        {
            LayeredNav nav = Nav(Open(6, 1, (2, 0), (3, 0)), Ml5Plates.Flat(1, 2, 0, 2, 1, 19660, 32768));
            var reach = new LayeredReach(nav, 2);
            Assert.That(Enumerable.Range(0, nav.Count).Select(reach.CostFrom).ToArray(), Is.EqualTo(Enumerable.Repeat(-1L, nav.Count).ToArray()));
            Assert.Throws<ArgumentOutOfRangeException>(() => new LayeredReach(nav, nav.Count));
            Assert.Throws<ArgumentOutOfRangeException>(() => new LayeredReach(nav, -1));
            Assert.Throws<ArgumentNullException>(() => new LayeredReach(null, 0));
        }

        // Entering a cell costs that cell's cost: a 3 x 1 row with costs 1, 5, 2. To the last cell: 5000 + 2000 from the first,
        // 2000 from the middle; to the first: 1000 from the middle, 5000 + 1000 from the last. A diagonal costs 1414 a cell (2 x 2
        // with costs 1, 5, 5, 1: straight across) and is not taken past a blocked orthogonal cell (with (1, 0) blocked: round by
        // (0, 1), 5000 + 1000).
        [Test]
        public void StepsCostWhatTheCellEnteredCosts()
        {
            LayeredNav row = Nav(Ground(3, 1, new byte[] { 1, 5, 2 }));
            Assert.That(new[] { 0, 1, 2 }.Select(new LayeredReach(row, 2).CostFrom).ToArray(), Is.EqualTo(new long[] { 7000, 2000, 0 }));
            Assert.That(new[] { 0, 1, 2 }.Select(new LayeredReach(row, 0).CostFrom).ToArray(), Is.EqualTo(new long[] { 0, 1000, 6000 }));
            LayeredNav open = Nav(Ground(2, 2, new byte[] { 1, 5, 5, 1 }));
            Assert.That(new LayeredReach(open, 3).CostFrom(0), Is.EqualTo(1414L));
            LayeredNav corner = Nav(Ground(2, 2, new byte[] { 1, 0, 5, 1 }));
            Assert.That(new LayeredReach(corner, 3).CostFrom(0), Is.EqualTo(5000L + 1000));
        }

        // The demo house: every node to a ground cell outside, to the stair and to the first floor.
        [Test]
        public void TheDemoHouseMatchesThePathfinder()
        {
            WcMap map = WcMapReader.Read(PlateDemoMap.Bytes());
            LayeredNav nav = WcMapLayeredNav.Build(map, null, map.ContentId);
            Assert.That(nav.Count, Is.EqualTo(408));
            foreach (int target in new[] { 0, 5 * 24 + 2, 225, 384, 387, 388, 400, 407 })
                Assert.That(Compare(nav, target).found, Is.GreaterThan(300), "target " + target);
            // From the far corner outside to the first floor's cell (9, 6): the walk ends over the stair and both seams.
            Assert.That(new LayeredReach(nav, 388).CostFrom(0), Is.EqualTo(new LayeredReach(nav, 225).CostFrom(0) + 1000 + 3000 + 1000 + new LayeredReach(nav, 388).CostFrom(407)));
        }

        // 600 seeded maps (up to 12 x 12, ground costs 0-5, 0-4 flat or sloped plates with holes and costs), three targets each,
        // every node.
        [Test]
        public void SeededMapsMatchThePathfinder()
        {
            var rnd = new Random(11001);
            int maps = 0, found = 0, none = 0, plateTargets = 0;
            for (int m = 0; m < 600; m++)
            {
                LayeredNav nav = Seeded(rnd);
                maps++;
                for (int t = 0; t < 3; t++)
                {
                    int target = rnd.Next(nav.Count);
                    if (nav.LayerOf(target) != 0) plateTargets++;
                    (int f, int n) = Compare(nav, target);
                    found += f; none += n;
                }
            }
            TestContext.Out.WriteLine("ML-11a reach: maps " + maps + ", routes " + found + ", no route " + none + ", plate targets " + plateTargets);
            Assert.That(found, Is.GreaterThanOrEqualTo(20000));
            Assert.That(none, Is.GreaterThanOrEqualTo(5000));
            Assert.That(plateTargets, Is.GreaterThanOrEqualTo(100));
        }

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
                // Tops within a step of the ground (flat at 0) at one end, so seams appear; slopes rise 0.25 m a cell.
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
    }
}
