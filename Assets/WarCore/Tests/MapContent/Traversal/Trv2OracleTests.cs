using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Content.MapTools;
using WarCore.Sim;
using static WarCore.Tests.Trv2Maps;

namespace WarCore.Tests
{
    // TRV-2 T2-27: LinkDerive against a slow oracle written from K15 alone, on 200 random 16 x 16 plate maps (fixed seeds).
    // The oracle reads heights, walls and plates from the description (its own plate-top formula, its own seam test, the
    // fixed point by repeating passes over unsorted spans) and takes only walkability from the graph.
    public sealed class Trv2OracleTests
    {
        private const int Size = 16;

        private sealed class Rng
        {
            private ulong s;
            internal Rng(ulong seed) { s = seed * 0x9E3779B97F4A7C15UL + 1; }
            internal int Next(int n) { s ^= s << 13; s ^= s >> 7; s ^= s << 17; return (int)(s % (ulong)n); }
            internal T Pick<T>(T[] items) => items[Next(items.Length)];
        }

        private static readonly long[] PlateRise = { 6554, 19660, 29491, 39321, 39322, 65536, 78643, 98304, 131072, 163840, 196608 };
        private static readonly long[] Thick = { 32768, 65536, 131072 };
        private static readonly long[] WallTall = { 32768, 58982, 65536, 98304, 131072, 163840 };
        private static readonly long[] Slant = { 0, 1311, 3277, 6554 };

        private static MapDesc Random(Rng r)
        {
            long ax = r.Pick(Slant), az = r.Next(2) == 0 ? 0 : r.Pick(Slant);
            bool field = r.Next(2) == 0;
            Func<int, int, long> ground = (i, j) => field ? ax * i + az * j : 0;
            var plates = new List<MapPlate>();
            int plateCount = 1 + r.Next(5);
            for (int k = 0; k < plateCount; k++)
            {
                int x = r.Next(Size - 1), z = r.Next(Size - 1), w = 1 + r.Next(Math.Min(4, Size - x)), d = 1 + r.Next(Math.Min(4, Size - z));
                long high = ground(x + w, z + d);
                long from = high + r.Pick(PlateRise), thick = r.Pick(Thick);
                if (r.Next(4) == 0) plates.Add(Slope((ulong)k + 1, x, z, w, d, r.Next(2) == 0 ? "x" : "z", from, from + r.Pick(new long[] { -19660, 19660, 32768 }) + 19660, thick));
                else plates.Add(Flat((ulong)k + 1, x, z, w, d, from, thick));
            }
            var walls = new List<MapWall>();
            int wallCount = r.Next(7);
            for (int k = 0; k < wallCount; k++)
            {
                bool along = r.Next(2) == 0;
                int len = 1 + r.Next(5);
                int x = r.Next(along ? Size - len + 1 : Size), z = r.Next(along ? Size : Size - len + 1);
                int w = along ? len : 1, d = along ? 1 : len;
                long high = ground(x + w, z + d);
                if (r.Next(3) == 0)
                {
                    long tall = r.Pick(WallTall);
                    long bas = high + r.Pick(new long[] { 19660, 32768, 58982, 98304, 131072, 163840 }) + r.Next(3);
                    walls.Add(Wall((ulong)k + 1, x, z, w, d, tall, bas));
                }
                else walls.Add(Wall((ulong)k + 1, x, z, w, d, (field ? high + SimFix.Scale : 0) + r.Pick(WallTall)));
            }
            return Make(Size, Size, field ? Heights(Size, Size, ground) : null, plates.ToArray(), walls.ToArray());
        }

        // ---- the oracle ----

        private static long FloorDiv(long a, long b) { long q = a / b; return (a % b != 0 && (a < 0) != (b < 0)) ? q - 1 : q; }

        private static long Point(MapDesc d, int layer, int i, int j)
        {
            if (layer == 0) return d.Heights == null ? d.FloorRaw : d.Heights[j][i];
            MapPlate p = d.Plates[layer - 1];
            if (p.Slope == null) return p.YRaw.Value;
            if (p.Slope.Axis == "x") return p.Slope.FromRaw + FloorDiv((p.Slope.ToRaw - p.Slope.FromRaw) * (i - p.Cell.X), p.Size.X);
            return p.Slope.FromRaw + FloorDiv((p.Slope.ToRaw - p.Slope.FromRaw) * (j - p.Cell.Z), p.Size.Z);
        }

        private static long Lo(MapDesc d, int layer, int x, int z) => new[] { Point(d, layer, x, z), Point(d, layer, x + 1, z), Point(d, layer, x, z + 1), Point(d, layer, x + 1, z + 1) }.Min();
        private static long Hi(MapDesc d, int layer, int x, int z) => new[] { Point(d, layer, x, z), Point(d, layer, x + 1, z), Point(d, layer, x, z + 1), Point(d, layer, x + 1, z + 1) }.Max();

        private static bool Covers(MapXZ cell, MapXZ size, int x, int z) => x >= cell.X && x < cell.X + size.X && z >= cell.Z && z < cell.Z + size.Z;

        // Spans of cell (x, z): (bottom, top, layer), walls first (layer -1) then plates, in description order.
        private static List<(long B, long T, int L)> Spans(MapDesc d, int x, int z)
        {
            var spans = new List<(long, long, int)>();
            foreach (MapWall w in d.Walls)
                if (Covers(w.Cell, w.Size, x, z)) { long b = w.BaseRaw ?? d.FloorRaw; spans.Add((b, b + w.HeightRaw, -1)); }
            for (int k = 0; k < d.Plates.Count; k++)
                if (Covers(d.Plates[k].Cell, d.Plates[k].Size, x, z)) spans.Add((Lo(d, k + 1, x, z) - d.Plates[k].ThickRaw, Hi(d, k + 1, x, z), k + 1));
            return spans;
        }

        private static bool Walk(LayeredNav nav, int layer, int x, int z) => nav.TryNode(layer, x, z, out int n) && nav.IsPassable(n);

        private static List<TraversalLink> Oracle(MapDesc d, LayeredNav nav, TraversalRules rules, NoTraverseZone[] zones)
        {
            var found = new List<TraversalLink>();
            Func<int, int, bool> zoned = (x, z) => zones.Any(q => x >= q.X && x < q.X + q.Width && z >= q.Z && z < q.Z + q.Depth);
            int layers = d.Plates.Count + 1, W = d.Size.X, D = d.Size.Z;
            for (int la = 0; la < layers; la++)
                for (int az = 0; az < D; az++)
                    for (int ax = 0; ax < W; ax++)
                    {
                        if (!Walk(nav, la, ax, az) || zoned(ax, az)) continue;
                        int a = Node(nav, la, ax, az);
                        foreach ((int dx, int dz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                            for (int lb = 0; lb < layers; lb++)
                            {
                                // Edge to the neighbour.
                                int bx = ax + dx, bz = az + dz;
                                if (lb != la && bx >= 0 && bz >= 0 && bx < W && bz < D && Walk(nav, lb, bx, bz) && !zoned(bx, bz))
                                {
                                    var ends = new List<(int, int)>();
                                    for (int i = Math.Max(ax, bx); i <= Math.Min(ax, bx) + 1; i++)
                                        for (int j = Math.Max(az, bz); j <= Math.Min(az, bz) + 1; j++) ends.Add((i, j));
                                    Assert.That(ends.Count, Is.EqualTo(2));
                                    long[] diff = ends.Select(e => Point(d, lb, e.Item1, e.Item2) - Point(d, la, e.Item1, e.Item2)).ToArray();
                                    if (!(Math.Abs(diff[0]) <= LayeredNav.StepRaw && Math.Abs(diff[1]) <= LayeredNav.StepRaw))
                                    {
                                        long rise = Math.Max(0, diff.Max()), fall = Math.Max(0, -diff.Min());
                                        long top = Math.Max(Hi(d, la, ax, az), Hi(d, lb, bx, bz));
                                        var bottoms = Spans(d, ax, az).Where(s => s.L != la).Concat(Spans(d, bx, bz).Where(s => s.L != lb)).Where(s => s.B >= top).Select(s => s.B).ToList();
                                        long gap = bottoms.Count == 0 ? LinkDerive.NoGap : bottoms.Min() - top;
                                        Match(found, rules, a, Node(nav, lb, bx, bz), TraversalShape.Edge, rise, fall, gap, top);
                                    }
                                }
                                // Over the neighbour to the cell after it.
                                int mx = ax + dx, mz = az + dz, cx = ax + 2 * dx, cz = az + 2 * dz;
                                if (cx < 0 || cz < 0 || cx >= W || cz >= D || !Walk(nav, lb, cx, cz) || zoned(mx, mz) || zoned(cx, cz)) continue;
                                if (Walk(nav, la, mx, mz) || Walk(nav, lb, mx, mz)) continue;
                                long c0 = Math.Max(Math.Max(Hi(d, la, ax, az), Hi(d, lb, cx, cz)), Hi(d, 0, mx, mz));
                                List<(long B, long T, int L)> m = Spans(d, mx, mz);
                                if (!m.Any(s => s.B <= c0 && c0 < s.T)) continue;
                                long c = c0;
                                for (bool changed = true; changed;)
                                {
                                    changed = false;
                                    foreach (var s in m)
                                        if (s.B <= c && s.T > c) { c = s.T; changed = true; }
                                }
                                var above = m.Concat(Spans(d, ax, az)).Concat(Spans(d, cx, cz)).Where(s => s.B > c).Select(s => s.B).ToList();
                                Match(found, rules, a, Node(nav, lb, cx, cz), TraversalShape.Over, c - Lo(d, la, ax, az), c - Lo(d, lb, cx, cz),
                                    above.Count == 0 ? LinkDerive.NoGap : above.Min() - c, c);
                            }
                    }
            return found.OrderBy(l => l.From).ThenBy(l => l.To).ToList();
        }

        private static void Match(List<TraversalLink> found, TraversalRules rules, int a, int b, TraversalShape shape, long rise, long fall, long gap, long cross)
        {
            for (int k = 0; k < rules.Count; k++)
            {
                TraversalKind t = rules[k];
                if (t.Shape != shape || rise < t.RiseMin || rise > t.RiseMax || fall < t.FallMin || fall > t.FallMax || gap < t.GapMin) continue;
                long ticks = t.TicksBase + (long)Math.Ceiling(t.TicksPerMetreUp * (decimal)rise / SimFix.Scale) + (long)Math.Ceiling(t.TicksPerMetreDown * (decimal)fall / SimFix.Scale);
                found.Add(new TraversalLink(a, b, k, shape, rise, fall, gap, cross, (int)ticks));
                return;
            }
        }

        private static readonly TraversalRules WideBoth = new TraversalRules(new[]
        {
            new TraversalKind("e", TraversalShape.Edge, 0, 4 * SimFix.Scale, 0, 4 * SimFix.Scale, 32768, 3, 7, 5, 0),
            new TraversalKind("o", TraversalShape.Over, 0, 4 * SimFix.Scale, 0, 4 * SimFix.Scale, 0, 2, 11, 13, 0),
        });

        [Test]
        public void T2_27_DeriveEqualsTheOracleOn200RandomMaps()
        {
            int valid = 0, attempts = 0, total = 0, overs = 0;
            var r = new Rng(20261121);
            while (valid < 200)
            {
                Assert.That(++attempts, Is.LessThan(20000), "too few random maps bake");
                MapDesc d = Random(r);
                WcMap map;
                try { map = MapBake.BuildMap(MapDescJson.Read(Ml4Maps.Bytes(d))); WcMapValidation.Validate(map); }
                catch (InvalidDataException) { continue; }
                valid++;
                LayeredNav nav = Nav(map);
                TraversalRules rules = valid % 2 == 0 ? Rules : WideBoth;
                NoTraverseZone[] zones = valid % 3 == 0 ? new[] { new NoTraverseZone(r.Next(Size), r.Next(Size), 1 + r.Next(4), 1 + r.Next(4)) } : new NoTraverseZone[0];
                TraversalLink[] got = LinkDerive.Derive(map, nav, rules, zones);
                List<TraversalLink> want = Oracle(d, nav, rules, zones);
                Assert.That(got, Is.EqualTo(want), "map " + valid + " (attempt " + attempts + ")");
                total += got.Length;
                overs += got.Count(l => l.Shape == TraversalShape.Over);
            }
            TestContext.WriteLine("TRV-2 oracle: 200 maps from " + attempts + " attempts, " + total + " links, " + overs + " over");
            Assert.That(overs, Is.GreaterThan(200), "the random maps exercise over links");
            Assert.That(total - overs, Is.GreaterThan(500), "and edge links");
        }

        [Test]
        public void TheOracleAgreesOnTheFixtures()
        {
            foreach (MapDesc d in new[] { Box1(), Window(), LowWall(), BoxWall(), SlopeHouse() })
            {
                WcMap map = Bake(d);
                LayeredNav nav = Nav(map);
                Assert.That(LinkDerive.Derive(map, nav, Rules, null), Is.EqualTo(Oracle(d, nav, Rules, new NoTraverseZone[0])));
            }
        }
    }
}
