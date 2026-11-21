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
    // TRV-3 shared helpers: graphs built with links from the TRV-2 fixtures, one more fixture, a forward Dijkstra over the
    // public LayeredNav queries (no heuristic, no shared code with LayeredPathfinder), and a copy of the TRV-2 random maps.
    internal static class Trv3Maps
    {
        // The graph of a fixture with the links of the given rules (trv-test's by default).
        internal static LayeredNav Linked(MapDesc d, TraversalRules rules = null, TraversalShapes shapes = TraversalShapes.All, params NoTraverseZone[] zones) =>
            Linked(Bake(d), rules, shapes, zones);

        internal static LayeredNav Linked(WcMap map, TraversalRules rules = null, TraversalShapes shapes = TraversalShapes.All, params NoTraverseZone[] zones) =>
            WcMapLayeredNav.Build(map, Test.Terrain, Test.Id, rules ?? Rules, zones, shapes);

        internal static LayeredNav Plain(MapDesc d) => WcMapLayeredNav.Build(Bake(d), Test.Terrain, Test.Id);

        // F-window with the wall east of the window one cell shorter (x 9..14): the cell (15, 8) is open, so there is a walk
        // around the window.
        internal static MapDesc WindowDoor() => Make(16, 16, null, new[] { Box(1, 1, 1, M) }, new[]
        {
            Wall(11, 0, 8, 8, 1, 163840), Wall(12, 9, 8, 6, 1, 163840), Wall(13, 8, 8, 1, 1, 58982), Wall(14, 8, 8, 1, 1, M / 2, 2 * M),
        });

        // trv-test's four kinds with the vault's path cost replaced.
        internal static TraversalRules WithVaultCost(long pathCost)
        {
            var kinds = new List<TraversalKind>();
            for (int k = 0; k < Rules.Count; k++)
            {
                TraversalKind t = Rules[k];
                kinds.Add(new TraversalKind(t.Id, t.Shape, t.RiseMin, t.RiseMax, t.FallMin, t.FallMax, t.GapMin, t.TicksBase, t.TicksPerMetreUp,
                    t.TicksPerMetreDown, t.Id == "vault" ? pathCost : t.PathCost, t.Fire, t.NoiseRaw));
            }
            return new TraversalRules(kinds);
        }

        // Edge links up to 3 m up or down and over links up to 3 m, path costs 0 and 700: many links on the random maps.
        internal static readonly TraversalRules Wide = new TraversalRules(new[]
        {
            new TraversalKind("e", TraversalShape.Edge, 0, 3 * M, 0, 3 * M, 0, 5, 1, 1, 0),
            new TraversalKind("o", TraversalShape.Over, 0, 3 * M, 0, 3 * M, 0, 9, 2, 2, 700),
        });

        internal static int[] PathOf(LayeredPathfinder f) => f.Path.ToArray();

        // ---- an independent Dijkstra over the public queries ----

        internal static long[] Dijkstra(LayeredNav nav, int source, int skipLink = -1)
        {
            var dist = new long[nav.Count];
            for (int i = 0; i < dist.Length; i++) dist[i] = -1;
            if (!nav.IsPassable(source)) return dist;
            var open = new SortedSet<(long Cost, int Node)>();
            dist[source] = 0;
            open.Add((0, source));
            while (open.Count > 0)
            {
                var (cost, u) = open.Min;
                open.Remove(open.Min);
                foreach (var (v, step) in Edges(nav, u, skipLink))
                {
                    long c = cost + step;
                    if (dist[v] >= 0 && dist[v] <= c) continue;
                    if (dist[v] >= 0) open.Remove((dist[v], v));
                    dist[v] = c;
                    open.Add((c, v));
                }
            }
            return dist;
        }

        internal static IEnumerable<(int Node, long Cost)> Edges(LayeredNav nav, int u, int skipLink = -1, bool links = true)
        {
            int layer = nav.LayerOf(u), x = nav.XOf(u), z = nav.ZOf(u);
            for (int dx = -1; dx <= 1; dx++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    if (dx == 0 && dz == 0) continue;
                    if (!Walk(nav, layer, x + dx, z + dz, out int v)) continue;
                    if (dx != 0 && dz != 0 && (!Walk(nav, layer, x + dx, z, out _) || !Walk(nav, layer, x, z + dz, out _))) continue;
                    yield return (v, (dx != 0 && dz != 0 ? 1414L : 1000L) * nav.CostAt(v));
                }
            for (int k = 0; k < nav.SeamCount(u); k++) yield return (nav.Seam(u, k), 1000L * nav.CostAt(nav.Seam(u, k)));
            if (!links) yield break;
            for (int i = nav.LinkFromFirst(u); i < nav.LinkFromFirst(u) + nav.LinkFromCount(u); i++)
            {
                if (i == skipLink) continue;
                NavLink l = nav.Link(i);
                yield return (l.To, 1000L * l.Span * nav.CostAt(l.To) + l.PathCost);
            }
        }

        private static bool Walk(LayeredNav nav, int layer, int x, int z, out int node) => nav.TryNode(layer, x, z, out node) && nav.IsPassable(node);

        // ---- the TRV-2 random maps (a copy: the TRV-2 tests stay untouched) ----

        internal const int Size = 16;

        internal sealed class Rng
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

        internal static MapDesc Random(Rng r)
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

        // The next random map that bakes, with its graph under the given rules.
        internal static (WcMap Map, LayeredNav Nav) NextLinked(Rng r, TraversalRules rules, ref int attempts)
        {
            while (true)
            {
                Assert.That(++attempts, Is.LessThan(20000), "too few random maps bake");
                WcMap map;
                try { map = MapBake.BuildMap(MapDescJson.Read(Ml4Maps.Bytes(Random(r)))); WcMapValidation.Validate(map); }
                catch (InvalidDataException) { continue; }
                return (map, Linked(map, rules));
            }
        }
    }
}
