using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using NUnit.Framework;
using WarCore.Sim;
using static WarCore.Tests.Ml6Nav;

namespace WarCore.Tests
{
    // ML-6 acceptance 4: 2000 seeded maps (up to 16 x 16, ground with and without a height field, 0-6 flat or sloped plates
    // with holes, blocked cells and costs 1-5, overlaps allowed), each with at least five start/target pairs, against an
    // independent graph and Dijkstra (Ml6Oracle). Plate tops sit at a neighbouring layer's height plus a delta that hits
    // the step: 0, +-16384, +-19659, +-19660, +-19661, +-32768 or more.
    public sealed class Ml6OracleTests
    {
        private static readonly int[] Seeds = { 6101, 6102, 6103, 6104 };
        private static readonly long[] Deltas = { 0, 16384, -16384, 19659, -19659, 19660, -19660, 19661, -19661, 32768, -32768, 65536, -131072 };

        private sealed class Counts
        {
            internal int Maps, Pairs, ViaSeam, Unreachable, TwoChanges, AtStep, OverStep;
        }

        [Test]
        public void MatchesTheOracle()
        {
            var counts = new Counts();
            var watch = Stopwatch.StartNew();
            foreach (int seed in Seeds)
            {
                var rnd = new Random(seed);
                for (int m = 0; m < 500; m++) OneMap(rnd, counts);
            }
            watch.Stop();
            TestContext.Out.WriteLine("ML-6 oracle: maps " + counts.Maps + ", pairs " + counts.Pairs + ", via a seam " + counts.ViaSeam
                + ", unreachable " + counts.Unreachable + ", two or more layer changes " + counts.TwoChanges + ", 19660 " + counts.AtStep
                + ", 19661 " + counts.OverStep + ", " + watch.ElapsedMilliseconds + " ms");
            Assert.That(counts.Maps, Is.EqualTo(2000));
            Assert.That(counts.Pairs, Is.GreaterThanOrEqualTo(10000));
            Assert.That(counts.ViaSeam, Is.GreaterThanOrEqualTo(500));
            Assert.That(counts.Unreachable, Is.GreaterThanOrEqualTo(200));
            Assert.That(counts.TwoChanges, Is.GreaterThanOrEqualTo(100));
            Assert.That(counts.AtStep, Is.GreaterThanOrEqualTo(50));
            Assert.That(counts.OverStep, Is.GreaterThanOrEqualTo(50));
            Assert.That(watch.ElapsedMilliseconds, Is.LessThanOrEqualTo(60000));
        }

        private static void OneMap(Random rnd, Counts counts)
        {
            int w = rnd.Next(3, 17), d = rnd.Next(3, 17);
            var groundCost = new byte[w * d];
            for (int k = 0; k < groundCost.Length; k++) groundCost[k] = rnd.Next(7) == 0 ? (byte)0 : (byte)rnd.Next(1, 6);
            int[] heights = null;
            long flat = 0;
            if (rnd.Next(2) == 0)
            {
                long g0 = (rnd.Next(9) - 4) * 16384;
                heights = new int[(w + 1) * (d + 1)];
                for (int k = 0; k < heights.Length; k++) heights[k] = (int)(g0 + (rnd.Next(4) == 0 ? Deltas[rnd.Next(Deltas.Length)] / 2 : 0));
            }
            else flat = (rnd.Next(9) - 4) * 16384 + (rnd.Next(5) == 0 ? rnd.Next(-3, 4) : 0);

            var plates = new List<Ml6Plate>();
            int solidCells = 0;
            int count = rnd.Next(7);
            for (int k = 0; k < count; k++)
            {
                var p = new Ml6Plate { W = rnd.Next(1, Math.Min(w, 6) + 1), D = rnd.Next(1, Math.Min(d, 6) + 1) };
                p.X = rnd.Next(0, w - p.W + 1); p.Z = rnd.Next(0, d - p.D + 1);
                // The reference: a neighbouring layer's height at the plate's minimum corner (the ground or an earlier plate there).
                long reference = heights == null ? flat : heights[p.Z * (w + 1) + p.X];
                if (plates.Count > 0 && rnd.Next(2) == 0)
                {
                    Ml6Plate q = plates[rnd.Next(plates.Count)];
                    int i = Math.Max(q.X, Math.Min(q.X + q.W, p.X)), j = Math.Max(q.Z, Math.Min(q.Z + q.D, p.Z));
                    reference = q.Points[(j - q.Z) * (q.W + 1) + i - q.X];
                }
                long from = reference + Deltas[rnd.Next(Deltas.Length)];
                int axis = rnd.Next(3);
                long step = axis == 0 ? 0 : new long[] { 16384, -16384, 19660, -19661, 49152, -65536, 131072 }[rnd.Next(7)];
                p.Points = new int[(p.W + 1) * (p.D + 1)];
                for (int j = 0; j <= p.D; j++)
                    for (int i = 0; i <= p.W; i++) p.Points[j * (p.W + 1) + i] = (int)(from + step * (axis == 1 ? i : axis == 2 ? j : 0));
                p.Solid = new bool[p.W * p.D];
                p.Cost = new byte[p.W * p.D];
                for (int c = 0; c < p.Solid.Length; c++)
                {
                    p.Solid[c] = rnd.Next(10) != 0;
                    if (p.Solid[c] && rnd.Next(10) != 0) p.Cost[c] = (byte)rnd.Next(1, 6);
                }
                int solid = p.Solid.Count(s => s);
                if (solidCells + solid > w * d) break;
                solidCells += solid;
                plates.Add(p);
            }

            var slabs = plates.Select((p, k) => new PlateSlab((ulong)k + 1, p.X, p.Z, p.W, p.D, p.Points, 32768, p.Solid)).ToArray();
            var nav = new LayeredNav(Ground(w, d, groundCost, heights), flat, slabs, plates.Select(p => p.Cost).ToArray(),
                plates.Select(p => Speeds(p.Cost)).ToArray());
            var oracle = new Ml6Oracle(w, d, groundCost, heights, flat, plates.ToArray());
            counts.Maps++;
            counts.AtStep += oracle.AtStep;
            counts.OverStep += oracle.OverStep;

            Assert.That(nav.Count, Is.EqualTo(oracle.Count), "node count");
            for (int n = 0; n < nav.Count; n++)
            {
                Assert.That((nav.LayerOf(n), nav.XOf(n), nav.ZOf(n), nav.CostAt(n)), Is.EqualTo((oracle.Layer[n], oracle.X[n], oracle.Z[n], oracle.Cost[n])), "node " + n);
                Assert.That(Node(nav, oracle.Layer[n], oracle.X[n], oracle.Z[n]), Is.EqualTo(n));
                Assert.That(Seams(nav, n), Is.EqualTo(oracle.Seams[n].ToArray()), "seams of " + n);
            }

            var finder = new LayeredPathfinder(nav);
            var walkable = Enumerable.Range(0, nav.Count).Where(n => oracle.Cost[n] != 0).ToArray();
            var onPlates = walkable.Where(n => oracle.Layer[n] != 0).ToArray();
            if (walkable.Length == 0) return;
            for (int s = 0; s < 5; s++)
            {
                int start = onPlates.Length > 0 && rnd.Next(2) == 0 ? onPlates[rnd.Next(onPlates.Length)] : walkable[rnd.Next(walkable.Length)];
                long[] dist = oracle.Distances(start);
                for (int t = 0; t < 2; t++)
                {
                    int target = rnd.Next(10) == 0 ? rnd.Next(nav.Count)
                        : onPlates.Length > 0 && rnd.Next(2) == 0 ? onPlates[rnd.Next(onPlates.Length)] : walkable[rnd.Next(walkable.Length)];
                    Pair(finder, oracle, start, target, dist, counts);
                }
            }
        }

        private static void Pair(LayeredPathfinder finder, Ml6Oracle oracle, int start, int target, long[] dist, Counts counts)
        {
            counts.Pairs++;
            GridPathStatus status = finder.Find(start, target);
            bool reachable = oracle.Cost[target] != 0 && dist[target] != long.MaxValue;
            if (!reachable)
            {
                Assert.That(status, Is.EqualTo(GridPathStatus.Unreachable), start + " -> " + target);
                counts.Unreachable++;
                return;
            }
            Assert.That(status, Is.EqualTo(GridPathStatus.Found), start + " -> " + target);
            Assert.That(finder.TotalCostWide, Is.EqualTo(dist[target]), "cost " + start + " -> " + target);
            int[] path = Path(finder);
            Assert.That((path[0], path[path.Length - 1]), Is.EqualTo((start, target)));
            long sum = 0;
            int changes = 0;
            for (int k = 1; k < path.Length; k++)
            {
                long w = oracle.Edge(path[k - 1], path[k]);
                Assert.That(w, Is.GreaterThan(0), "step " + path[k - 1] + " -> " + path[k] + " is an edge");
                sum += w;
                if (oracle.Layer[path[k - 1]] != oracle.Layer[path[k]]) changes++;
            }
            Assert.That(sum, Is.EqualTo(dist[target]), "steps add up");
            if (changes >= 1) counts.ViaSeam++;
            if (changes >= 2) counts.TwoChanges++;
            Assert.That(finder.FindWithin(start, target, dist[target]), Is.EqualTo(GridPathStatus.Found));
            Assert.That((Path(finder), finder.TotalCostWide), Is.EqualTo((path, dist[target])), "within the exact limit");
            if (dist[target] > 0) Assert.That(finder.FindWithin(start, target, dist[target] - 1), Is.EqualTo(GridPathStatus.Unreachable));
        }
    }
}
