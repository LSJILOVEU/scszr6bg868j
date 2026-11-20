using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    // ML-6 fixtures: layered graphs built straight from numbers (raw units, S = 65536).
    internal static class Ml6Nav
    {
        internal const long S = SimFix.Scale;

        // Ground tables: cost per cell row-major (0 = blocked), speed 1000 where walkable; heights (W+1)(D+1) or null (flat).
        internal static TerrainTables Ground(int w, int d, byte[] cost, int[] heights = null, long originX = 0, long originZ = 0)
        {
            var bits = new byte[(w * d + 7) / 8];
            var speed = new ushort[w * d];
            for (int k = 0; k < w * d; k++)
            {
                if (cost[k] == 0) continue;
                bits[k >> 3] |= (byte)(1 << (k & 7));
                speed[k] = 1000;
            }
            var nav = new NavGrid(w, d, SimFix.FromRaw(originX), SimFix.FromRaw(originZ), bits);
            HeightField field = heights == null ? null : new HeightField(w, d, SimFix.FromRaw(originX), SimFix.FromRaw(originZ), heights);
            return new TerrainTables(field, nav, (byte[])cost.Clone(), speed, new string[0], new byte[0], new byte[0]);
        }

        // Every cell cost 1 except the blocked ones.
        internal static TerrainTables Open(int w, int d, params (int x, int z)[] blocked) => Ground(w, d, Costs(w, d, 1, blocked));

        internal static byte[] Costs(int w, int d, byte c, params (int x, int z)[] blocked)
        {
            var cost = Enumerable.Repeat(c, w * d).ToArray();
            foreach ((int x, int z) in blocked) cost[z * w + x] = 0;
            return cost;
        }

        // A plate's cost table: c on its solid cells, 0 on the holes and on the blocked local cells.
        internal static byte[] Costs(PlateSlab p, byte c = 1, params (int x, int z)[] blocked)
        {
            var cost = new byte[p.Width * p.Depth];
            for (int j = 0; j < p.Depth; j++)
                for (int i = 0; i < p.Width; i++) cost[j * p.Width + i] = p.IsSolid(i, j) ? c : (byte)0;
            foreach ((int x, int z) in blocked) cost[z * p.Width + x] = 0;
            return cost;
        }

        internal static ushort[] Speeds(byte[] cost) => cost.Select(c => c == 0 ? (ushort)0 : (ushort)1000).ToArray();

        internal static LayeredNav Nav(TerrainTables ground, long flat, params (PlateSlab plate, byte[] cost)[] plates) =>
            new LayeredNav(ground, flat, plates.Select(p => p.plate).ToArray(), plates.Select(p => p.cost).ToArray(),
                plates.Select(p => Speeds(p.cost)).ToArray());

        internal static LayeredNav Nav(TerrainTables ground, params PlateSlab[] plates) =>
            Nav(ground, 0, plates.Select(p => (p, Costs(p))).ToArray());

        internal static int[] Seams(LayeredNav nav, int node) => Enumerable.Range(0, nav.SeamCount(node)).Select(k => nav.Seam(node, k)).ToArray();

        internal static int[] Path(LayeredPathfinder f) => f.Path.ToArray();

        internal static int Node(LayeredNav nav, int layer, int x, int z)
        {
            Assert.That(nav.TryNode(layer, x, z, out int node), Is.True, "node of layer " + layer + " at " + x + "," + z);
            return node;
        }

        internal static void Found(LayeredPathfinder f, int from, int to, long cost, params int[] path)
        {
            Assert.That(f.Find(from, to), Is.EqualTo(GridPathStatus.Found));
            Assert.That(f.TotalCostWide, Is.EqualTo(cost), "cost");
            if (path.Length > 0) Assert.That(Path(f), Is.EqualTo(path), "path");
        }

        internal static List<(int a, int b)> AllSeams(LayeredNav nav)
        {
            var list = new List<(int, int)>();
            for (int a = 0; a < nav.Count; a++)
                for (int k = 0; k < nav.SeamCount(a); k++) list.Add((a, nav.Seam(a, k)));
            return list;
        }
    }
}
