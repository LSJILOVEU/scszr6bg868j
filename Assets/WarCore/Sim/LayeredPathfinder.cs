using System;

namespace WarCore.Sim
{
    // ML-6: A* over a LayeredNav, with GridPathfinder's rules. Path borrows storage until the next Find or FindWithin call.
    //
    // Entering a node costs 1000 x its cost on an orthogonal step or a seam and 1414 x its cost on a diagonal step (diagonals
    // only inside one layer, both orthogonal cells of that layer walkable). The heuristic is the octile distance between the
    // node table's cells times the smallest cost of any walkable node on any layer; a seam joins orthogonal neighbours, so it
    // stays consistent. Heap order is F, H, node, G. With equal costs the parent is the candidate that leaves the heap first:
    // a node's parent changes only on a strictly smaller cost, so the neighbour order (eight cells, then seams by ascending
    // other node) does not change any result. Without plates every status, path, total and Expanded equals GridPathfinder's
    // on the same TerrainTables: same walkable test (cost > 0), step costs, heuristic, comparator and loop.
    public sealed class LayeredPathfinder
    {
        private readonly LayeredNav nav;
        private readonly long minCellCost;
        private readonly long[] costs;
        // costs[node] belongs to the current search only when stamps[node] == generation; no per-search clear.
        private readonly uint[] stamps;
        private uint generation;
        private readonly int[] parents;
        private readonly int[] path;
        private Entry[] heap;
        private int heapCount;
        private int pathCount;
        public ReadOnlySpan<int> Path => new ReadOnlySpan<int>(path, 0, pathCount);
        public int TotalCost => TotalCostWide <= int.MaxValue ? (int)TotalCostWide : throw new SimOverflowException("Total path cost exceeds an int; read TotalCostWide.");
        public long TotalCostWide { get; private set; }
        // Nodes taken off the open set and expanded by the last search (stale heap entries not counted). Diagnostics only.
        internal int Expanded { get; private set; }

        private readonly struct Entry
        {
            internal readonly long F, H, G;
            internal readonly int Key;
            internal Entry(int key, long g, long h) { Key = key; G = g; H = h; F = g + h; }
        }

        public LayeredPathfinder(LayeredNav nav)
        {
            this.nav = nav ?? throw new ArgumentNullException(nameof(nav));
            int count = nav.Count;
            costs = new long[count]; stamps = new uint[count]; parents = new int[count]; path = new int[count];
            // Each node is expanded once (consistent heuristic) and inserts at most eight neighbours plus its seams.
            heap = new Entry[8L * count + nav.Seams.Length + 1 > int.MaxValue ? int.MaxValue : 8 * count + nav.Seams.Length + 1];
            long min = long.MaxValue;
            for (int node = 0; node < count; node++)
                if (nav.Cost[node] != 0 && nav.Cost[node] < min) min = nav.Cost[node];
            minCellCost = min == long.MaxValue ? 1 : min;
        }

        public GridPathStatus Find(int start, int target) => FindWithin(start, target, long.MaxValue);

        // As Find, but gives up once no route can cost limitCost or less: Found exactly when the cheapest route costs at most
        // limitCost (then Path and TotalCostWide equal Find's), else Unreachable; a blocked start stays Rejected.
        public GridPathStatus FindWithin(int start, int target, long limitCost)
        {
            pathCount = 0; TotalCostWide = 0; heapCount = 0; Expanded = 0;
            if (!nav.IsPassable(start)) return GridPathStatus.Rejected;
            if (!nav.IsPassable(target)) return GridPathStatus.Unreachable;
            generation = unchecked(generation + 1);
            if (generation == 0)
            {
                Array.Clear(stamps, 0, stamps.Length);
                generation = 1;
            }
            stamps[start] = generation; costs[start] = 0; parents[start] = -1;
            Push(new Entry(start, 0, Heuristic(start, target)));
            while (heapCount != 0)
            {
                Entry current = Pop();
                if (current.G != costs[current.Key]) continue;
                if (current.F > limitCost) return GridPathStatus.Unreachable;
                Expanded++;
                if (current.Key == target)
                {
                    Complete(target);
                    return GridPathStatus.Found;
                }
                int layer = nav.NodeLayer[current.Key], x = nav.NodeX[current.Key], z = nav.NodeZ[current.Key];
                for (int direction = 0; direction < 8; direction++)
                {
                    // N, E, S, W, NE, SE, SW, NW.
                    int dx = direction == 1 || direction == 4 || direction == 5 ? 1
                        : direction == 3 || direction == 6 || direction == 7 ? -1 : 0;
                    int dz = direction == 0 || direction == 4 || direction == 7 ? 1
                        : direction == 2 || direction == 5 || direction == 6 ? -1 : 0;
                    int next = Walkable(layer, x + dx, z + dz);
                    if (next < 0) continue;
                    bool diagonal = dx != 0 && dz != 0;
                    if (diagonal && (Walkable(layer, x + dx, z) < 0 || Walkable(layer, x, z + dz) < 0)) continue;
                    Relax(current, next, (diagonal ? 1414 : 1000) * (long)nav.Cost[next], target);
                }
                for (int s = nav.SeamStart[current.Key]; s < nav.SeamStart[current.Key + 1]; s++)
                {
                    int next = nav.Seams[s];
                    Relax(current, next, 1000 * (long)nav.Cost[next], target);
                }
            }
            return GridPathStatus.Unreachable;
        }

        // The walkable node of (layer, x, z), or -1.
        private int Walkable(int layer, int x, int z)
        {
            int node = nav.Node(layer, x, z);
            return node >= 0 && nav.Cost[node] != 0 ? node : -1;
        }

        private void Relax(Entry current, int next, long step, int target)
        {
            long g = current.G + step;
            if (stamps[next] == generation && g >= costs[next]) return;
            stamps[next] = generation; costs[next] = g; parents[next] = current.Key;
            Push(new Entry(next, g, Heuristic(next, target)));
        }

        private long Heuristic(int key, int target)
        {
            int dx = nav.NodeX[key] - nav.NodeX[target];
            int dz = nav.NodeZ[key] - nav.NodeZ[target];
            if (dx < 0) dx = -dx;
            if (dz < 0) dz = -dz;
            return (1000L * (dx + dz) - 586L * (dx < dz ? dx : dz)) * minCellCost;
        }

        private void Complete(int target)
        {
            TotalCostWide = costs[target];
            for (int key = target; key != -1; key = parents[key]) path[pathCount++] = key;
            for (int a = 0, b = pathCount - 1; a < b; a++, b--)
            {
                int key = path[a]; path[a] = path[b]; path[b] = key;
            }
        }

        private static bool Before(Entry a, Entry b)
        {
            if (a.F != b.F) return a.F < b.F;
            if (a.H != b.H) return a.H < b.H;
            if (a.Key != b.Key) return a.Key < b.Key;
            return a.G < b.G;
        }

        private void Push(Entry entry)
        {
            if (heapCount == heap.Length) Array.Resize(ref heap, heap.Length * 2);
            int at = heapCount++;
            while (at > 0)
            {
                int parent = (at - 1) / 2;
                if (!Before(entry, heap[parent])) break;
                heap[at] = heap[parent]; at = parent;
            }
            heap[at] = entry;
        }

        private Entry Pop()
        {
            Entry result = heap[0], last = heap[--heapCount];
            if (heapCount == 0) return result;
            int at = 0;
            while (at * 2 + 1 < heapCount)
            {
                int child = at * 2 + 1;
                if (child + 1 < heapCount && Before(heap[child + 1], heap[child])) child++;
                if (!Before(heap[child], last)) break;
                heap[at] = heap[child]; at = child;
            }
            heap[at] = last;
            return result;
        }
    }
}
