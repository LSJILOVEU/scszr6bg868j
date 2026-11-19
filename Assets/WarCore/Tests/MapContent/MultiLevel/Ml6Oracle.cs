using System.Collections.Generic;

namespace WarCore.Tests
{
    // ML-6 acceptance 4: an independent layered graph and a plain Dijkstra, built from the same raw inputs as LayeredNav
    // (ground costs and heights, the flat ground height, each plate's points, solid cells and costs). Nothing here calls
    // LayeredNav or LayeredPathfinder.
    internal sealed class Ml6Oracle
    {
        internal const long Step = 19660;
        internal readonly int Width, Depth, Count;
        internal readonly int[] Layer, X, Z;
        internal readonly byte[] Cost;
        internal readonly List<int>[] Seams;
        private readonly List<(int to, long w)>[] edges;
        private readonly int[] groundHeights;
        private readonly long flat;
        private readonly Ml6Plate[] plates;
        private readonly Dictionary<long, int> nodeOf = new Dictionary<long, int>();
        // Candidate pairs (different layers, orthogonal neighbours, both walkable) whose larger end point difference is
        // exactly 19660 / 19661.
        internal int AtStep, OverStep;

        internal Ml6Oracle(int width, int depth, byte[] groundCost, int[] groundHeights, long flat, Ml6Plate[] plates)
        {
            Width = width; Depth = depth; this.groundHeights = groundHeights; this.flat = flat; this.plates = plates;
            var layer = new List<int>(); var xs = new List<int>(); var zs = new List<int>(); var cost = new List<byte>();
            for (int z = 0; z < depth; z++)
                for (int x = 0; x < width; x++) Add(layer, xs, zs, cost, 0, x, z, groundCost[z * width + x]);
            for (int k = 0; k < plates.Length; k++)
            {
                Ml6Plate p = plates[k];
                for (int j = 0; j < p.D; j++)
                    for (int i = 0; i < p.W; i++)
                        if (p.Solid[j * p.W + i]) Add(layer, xs, zs, cost, k + 1, p.X + i, p.Z + j, p.Cost[j * p.W + i]);
            }
            Count = layer.Count; Layer = layer.ToArray(); X = xs.ToArray(); Z = zs.ToArray(); Cost = cost.ToArray();
            Seams = new List<int>[Count];
            edges = new List<(int, long)>[Count];
            for (int a = 0; a < Count; a++) { Seams[a] = new List<int>(); edges[a] = new List<(int, long)>(); }
            for (int a = 0; a < Count; a++)
            {
                if (Cost[a] == 0) continue;
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dz == 0) continue;
                        int b = Find(Layer[a], X[a] + dx, Z[a] + dz);
                        if (b < 0 || Cost[b] == 0) continue;
                        if (dx != 0 && dz != 0 && (Walk(Layer[a], X[a] + dx, Z[a]) < 0 || Walk(Layer[a], X[a], Z[a] + dz) < 0)) continue;
                        edges[a].Add((b, (dx != 0 && dz != 0 ? 1414 : 1000) * (long)Cost[b]));
                    }
                for (int l = 0; l <= plates.Length; l++)
                {
                    if (l == Layer[a]) continue;
                    foreach ((int dx, int dz) in new[] { (0, 1), (1, 0), (0, -1), (-1, 0) })
                    {
                        int b = Find(l, X[a] + dx, Z[a] + dz);
                        if (b < 0 || Cost[b] == 0) continue;
                        // The shared edge's end points.
                        int i0 = X[a] + (dx == 1 ? 1 : 0), j0 = Z[a] + (dz == 1 ? 1 : 0);
                        int i1 = dx == 0 ? i0 + 1 : i0, j1 = dz == 0 ? j0 + 1 : j0;
                        long d0 = Abs(H(Layer[a], i0, j0) - H(l, i0, j0)), d1 = Abs(H(Layer[a], i1, j1) - H(l, i1, j1));
                        long worst = d0 > d1 ? d0 : d1;
                        if (a < b && worst == Step) AtStep++;
                        if (a < b && worst == Step + 1) OverStep++;
                        if (worst > Step) continue;
                        Seams[a].Add(b);
                        edges[a].Add((b, 1000 * (long)Cost[b]));
                    }
                }
                Seams[a].Sort();
            }
        }

        private void Add(List<int> layer, List<int> xs, List<int> zs, List<byte> cost, int l, int x, int z, byte c)
        {
            nodeOf[Key(l, x, z)] = layer.Count;
            layer.Add(l); xs.Add(x); zs.Add(z); cost.Add(c);
        }

        private static long Key(int l, int x, int z) => ((long)l << 40) | ((long)(x + 1) << 20) | (long)(z + 1);
        private static long Abs(long v) => v < 0 ? -v : v;

        internal int Find(int l, int x, int z) => nodeOf.TryGetValue(Key(l, x, z), out int n) ? n : -1;
        private int Walk(int l, int x, int z) { int n = Find(l, x, z); return n >= 0 && Cost[n] != 0 ? n : -1; }

        private long H(int l, int i, int j)
        {
            if (l == 0) return groundHeights == null ? flat : groundHeights[j * (Width + 1) + i];
            Ml6Plate p = plates[l - 1];
            return p.Points[(j - p.Z) * (p.W + 1) + i - p.X];
        }

        // Weight of edge a -> b, or -1.
        internal long Edge(int a, int b)
        {
            foreach ((int to, long w) in edges[a]) if (to == b) return w;
            return -1;
        }

        // Cheapest cost from start to every node (long.MaxValue where unreachable); plain O(n^2) Dijkstra.
        internal long[] Distances(int start)
        {
            var dist = new long[Count];
            var done = new bool[Count];
            for (int n = 0; n < Count; n++) dist[n] = long.MaxValue;
            if (Cost[start] == 0) return dist;
            dist[start] = 0;
            while (true)
            {
                int best = -1;
                for (int n = 0; n < Count; n++)
                    if (!done[n] && dist[n] != long.MaxValue && (best < 0 || dist[n] < dist[best])) best = n;
                if (best < 0) return dist;
                done[best] = true;
                foreach ((int to, long w) in edges[best])
                    if (dist[best] + w < dist[to]) dist[to] = dist[best] + w;
            }
        }
    }

    // One plate's raw inputs, as the generator made them.
    internal sealed class Ml6Plate
    {
        internal int X, Z, W, D;
        internal int[] Points;
        internal bool[] Solid;
        internal byte[] Cost;
    }
}
