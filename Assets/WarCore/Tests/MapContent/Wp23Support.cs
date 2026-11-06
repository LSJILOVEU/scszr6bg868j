using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // WP-23 test fixtures: small maps with or without terrain, the demo pack, and session helpers.
    internal static class Wp23
    {
        internal const long S = SimFix.Scale;
        internal static readonly string[] Names = { "grass", "mud", "road" };

        internal static ContentPack Pack(string json = TerrainDemoMap.PackJson)
        {
            const string id = "rwr-terrain-demo";
            return ContentLoader.Load(new MemoryContentSource(new Dictionary<string, string> { [id] = json }, FileContentSource.Rwr()), id);
        }

        // A map with optional boxes (wall height wallRaw, centre Y = height/2 as the file demands), optional height field
        // (raw per grid point) and optional surface cells. Min.Y = minY, Max.Y = maxY, FlatHeight = Min.Y.
        internal static WcMap Make(int width, int depth, long minY, long maxY, (int x, int z, int w, int d)[] boxes, long wallRaw,
            Func<int, int, int> height = null, string[] names = null, Func<int, int, byte> surface = null)
        {
            boxes ??= Array.Empty<(int, int, int, int)>();
            var walls = new WcMapWall[boxes.Length];
            for (int i = 0; i < boxes.Length; i++)
            {
                var b = boxes[i];
                walls[i] = new WcMapWall(0, (ulong)(i + 1), "wall", "walls",
                    new SimVec3(SimFix.FromInt(b.x) + SimFix.Ratio(b.w, 2), SimFix.FromRaw(wallRaw / 2), SimFix.FromInt(b.z) + SimFix.Ratio(b.d, 2)),
                    SimFix.Ratio(b.w, 2), SimFix.FromRaw(wallRaw), SimFix.Ratio(b.d, 2));
            }
            var bits = new byte[(width * depth + 7) / 8];
            for (int z = 0; z < depth; z++)
                for (int x = 0; x < width; x++)
                {
                    bool blocked = false;
                    foreach (var b in boxes) blocked |= x >= b.x && x < b.x + b.w && z >= b.z && z < b.z + b.d;
                    if (!blocked) { int k = z * width + x; bits[k / 8] |= (byte)(1 << (k % 8)); }
                }
            int[] points = null;
            if (height != null)
            {
                points = new int[(width + 1) * (depth + 1)];
                for (int j = 0; j <= depth; j++)
                    for (int i = 0; i <= width; i++) points[j * (width + 1) + i] = height(i, j);
            }
            byte[] cells = null;
            if (surface != null)
            {
                cells = new byte[width * depth];
                for (int z = 0; z < depth; z++)
                    for (int x = 0; x < width; x++) cells[z * width + x] = surface(x, z);
            }
            var min = new SimVec3(default, SimFix.FromRaw(minY), default);
            var max = new SimVec3(SimFix.FromInt(width), SimFix.FromRaw(maxY), SimFix.FromInt(depth));
            return new WcMap("wp23", "none", min, max, SimFix.FromInt(Math.Max(width, depth)), SimFix.FromRaw(minY),
                (uint)width, (uint)depth, new[] { new WcMapLayer("walls", 1) }, walls, bits, points, names, cells);
        }

        internal static SimSession Session(WcMap map, MoverKind mover, UnitSpawn[] units, BridgeCommand[] script = null, ContentPack pack = null)
        {
            var recipe = new WorldRecipe(41, mover, units, script ?? Array.Empty<BridgeCommand>());
            if (pack != null) recipe = recipe.WithTerrainRules(pack);
            return SimSession.Create(recipe, WcMapWriter.Write(map));
        }

        internal static UnitSpawn Unit(ulong key, int squad, long xMm, long zMm, long speed = 4000) => new UnitSpawn(key, 0, squad, xMm, zMm, speed);
        internal static SimVec3 At(SimSession s, ulong key = 1) => M3TestWorld.Position(s, key);
        internal static MoveIntent Intent(SimSession s, ulong key = 1) => M3TestWorld.Component<MoveIntent>(s, key);
        internal static long Step(long speedMmPerSec) => SimFix.Ratio(speedMmPerSec, 1000 * SimClock.TickRate).Raw;
        internal static SimFix Mm(long mm) => SimFix.Ratio(mm, 1000);
        internal static SimVec3 V(long xMm, long yRaw, long zMm) => new SimVec3(Mm(xMm), SimFix.FromRaw(yRaw), Mm(zMm));
    }

    // The A* of before WP-23, kept verbatim in int arithmetic as a frozen oracle: same cost model, same tie-breaks.
    internal sealed class FrozenPathfinder
    {
        private readonly NavGrid grid;
        private readonly int[] costs, parents;
        internal readonly List<int> Path = new List<int>();
        internal int TotalCost;
        private readonly List<(int F, int H, int Key, int G)> heap = new List<(int, int, int, int)>();

        internal FrozenPathfinder(NavGrid grid) { this.grid = grid; costs = new int[grid.Count]; parents = new int[grid.Count]; }

        private static bool Before((int F, int H, int Key, int G) a, (int F, int H, int Key, int G) b)
        {
            if (a.F != b.F) return a.F < b.F;
            if (a.H != b.H) return a.H < b.H;
            if (a.Key != b.Key) return a.Key < b.Key;
            return a.G < b.G;
        }

        private int Heuristic(int key, int target)
        {
            int dx = Math.Abs(key % grid.Width - target % grid.Width), dz = Math.Abs(key / grid.Width - target / grid.Width);
            return 1000 * (dx + dz) - 586 * Math.Min(dx, dz);
        }

        // Linear-scan priority queue: slow but obviously correct against the heap order.
        internal GridPathStatus Find(int start, int target)
        {
            Path.Clear(); TotalCost = 0; heap.Clear();
            if (start < 0 || start >= grid.Count || !grid.IsPassable(start % grid.Width, start / grid.Width)) return GridPathStatus.Rejected;
            if (target < 0 || target >= grid.Count || !grid.IsPassable(target % grid.Width, target / grid.Width)) return GridPathStatus.Unreachable;
            for (int i = 0; i < costs.Length; i++) costs[i] = int.MaxValue;
            costs[start] = 0; parents[start] = -1;
            int h0 = Heuristic(start, target); heap.Add((h0, h0, start, 0));
            while (heap.Count != 0)
            {
                int best = 0;
                for (int i = 1; i < heap.Count; i++) if (Before(heap[i], heap[best])) best = i;
                var current = heap[best]; heap.RemoveAt(best);
                if (current.G != costs[current.Key]) continue;
                if (current.Key == target)
                {
                    TotalCost = costs[target];
                    for (int key = target; key != -1; key = parents[key]) Path.Add(key);
                    Path.Reverse();
                    return GridPathStatus.Found;
                }
                int x = current.Key % grid.Width, z = current.Key / grid.Width;
                for (int direction = 0; direction < 8; direction++)
                {
                    int dx = direction == 1 || direction == 4 || direction == 5 ? 1 : direction == 3 || direction == 6 || direction == 7 ? -1 : 0;
                    int dz = direction == 0 || direction == 4 || direction == 7 ? 1 : direction == 2 || direction == 5 || direction == 6 ? -1 : 0;
                    if (!grid.IsPassable(x + dx, z + dz)) continue;
                    bool diagonal = dx != 0 && dz != 0;
                    if (diagonal && (!grid.IsPassable(x + dx, z) || !grid.IsPassable(x, z + dz))) continue;
                    int next = (z + dz) * grid.Width + x + dx;
                    int g = current.G + (diagonal ? 1414 : 1000);
                    if (g >= costs[next]) continue;
                    costs[next] = g; parents[next] = current.Key;
                    int h = Heuristic(next, target);
                    heap.Add((g + h, h, next, g));
                }
            }
            return GridPathStatus.Unreachable;
        }
    }

    // Weighted shortest-path cost by plain Dijkstra over the same edge rules (independent of A*).
    internal static class DijkstraOracle
    {
        internal static long Cost(TerrainTables t, int start, int target)
        {
            NavGrid grid = t.Nav;
            var dist = new long[grid.Count];
            var done = new bool[grid.Count];
            for (int i = 0; i < dist.Length; i++) dist[i] = long.MaxValue;
            dist[start] = 0;
            while (true)
            {
                int u = -1;
                for (int i = 0; i < dist.Length; i++) if (!done[i] && dist[i] != long.MaxValue && (u < 0 || dist[i] < dist[u])) u = i;
                if (u < 0) return -1;
                if (u == target) return dist[u];
                done[u] = true;
                int x = u % grid.Width, z = u / grid.Width;
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dz == 0) continue;
                        if (!grid.IsPassable(x + dx, z + dz)) continue;
                        bool diagonal = dx != 0 && dz != 0;
                        if (diagonal && (!grid.IsPassable(x + dx, z) || !grid.IsPassable(x, z + dz))) continue;
                        int v = (z + dz) * grid.Width + x + dx;
                        long g = dist[u] + (diagonal ? 1414L : 1000L) * t.CostAt(v);
                        if (g < dist[v]) dist[v] = g;
                    }
            }
        }
    }
}

namespace WarCore.Tests
{
    // Frozen fixtures of the T1a-2a failure and liveness acceptance (frozen). 16 x 16 cells of 1 m,
    // flat, with a surface table whose "mud" is blocked; coordinates are millimetres unless a name says raw.
    internal static class Wp23Fx
    {
        internal const long S = SimFix.Scale;
        // Speed 3750 mm/s = 8192 raw per tick.
        internal const long Speed = 3750;

        internal static ContentPack ClosedPack() => Wp23.Pack(TerrainDemoMap.PackJson.Replace(
            "{ \"id\": \"mud\", \"speedMilli\": 500, \"cost\": 3 }", "{ \"id\": \"mud\", \"passable\": false }"));

        // blocked(x, z) marks the cells that are not walkable.
        internal static WcMap Map(Func<int, int, bool> blocked) =>
            Wp23.Make(16, 16, 0, 6 * S, null, 4 * S, null, Wp23.Names, (x, z) => (byte)(blocked(x, z) ? 1 : 0));

        // The same fixture family on a 64 x 16 map (long detours).
        internal static WcMap Map64(Func<int, int, bool> blocked) =>
            Wp23.Make(64, 16, 0, 6 * S, null, 4 * S, null, Wp23.Names, (x, z) => (byte)(blocked(x, z) ? 1 : 0));

        internal static SimSession World(MoverKind mover, WcMap map, params UnitSpawn[] units) =>
            Wp23.Session(map, mover, units, null, ClosedPack());

        internal static SimVec3 Goal(long xMm, long zMm) => new SimVec3(Wp23.Mm(xMm), default, Wp23.Mm(zMm));

        internal static void Order(SimSession s, ulong key, long xMm, long zMm) =>
            M3TestWorld.Locomotion(s).Order(M3TestWorld.Unit(s, key), Goal(xMm, zMm));

        internal static bool Failed(SimSession s, ulong key) => M3TestWorld.Locomotion(s).HasFailed(M3TestWorld.Unit(s, key));
        internal static bool Active(SimSession s, ulong key) => Wp23.Intent(s, key).Active;
        internal static MoveProgress Progress(SimSession s, ulong key) => M3TestWorld.Component<MoveProgress>(s, key);

        // The step on which unit `key` first reports failure, or -1 within `limit` steps. Every step also checks that no
        // move cut closer than the footprint to a blocked cell.
        internal static int StepsToFailure(SimSession s, ulong key, int limit, NavGrid nav)
        {
            for (int step = 1; step <= limit; step++)
            {
                SimVec3 before = Wp23.At(s, key);
                s.StepOnce();
                SimVec3 after = Wp23.At(s, key);
                if (!before.Equals(after)) Assert.That(nav.SegmentClear(before, after, InterimLocomotion.UnitFootprint), Is.True, $"step {step}");
                if (Failed(s, key)) return step;
            }
            return -1;
        }

        internal static NavGrid Nav(WcMap map) => TerrainDerivation.Derive(map, ClosedPack()).Nav;
    }
}
