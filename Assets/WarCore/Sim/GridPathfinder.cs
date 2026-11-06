using System;

namespace WarCore.Sim
{
    public enum GridPathStatus { Found, Unreachable, Rejected }

    // Synchronous search. Path borrows storage until the next Find call; consume it immediately.
    public sealed class GridPathfinder
    {
        private readonly NavGrid grid;
        private readonly TerrainTables terrain;
        // Smallest cell cost over walkable cells: keeps the octile heuristic admissible with weighted cells.
        private readonly long minCellCost = 1;
        private readonly long[] costs;
        // costs[key] belongs to the current search only when stamps[key] == generation; no per-search clear.
        private readonly uint[] stamps;
        private uint generation;
        private readonly int[] parents;
        private readonly int[] path;
        private Entry[] heap;
        private int heapCount;
        private int pathCount;
        public ReadOnlySpan<int> Path => new ReadOnlySpan<int>(path, 0, pathCount);
        // Without terrain every cell costs one, so the total is at most (512 x 512 - 1) edges x 1414 < 2^29 and fits an int.
        // With cell costs it can exceed an int (up to (512 x 512 - 1) x 1414 x 255 < 2^37): read TotalCostWide there.
        public int TotalCost => TotalCostWide <= int.MaxValue ? (int)TotalCostWide : throw new SimOverflowException("Total path cost exceeds an int; read TotalCostWide.");
        public long TotalCostWide { get; private set; }

        private readonly struct Entry
        {
            internal readonly long F, H, G;
            internal readonly int Key;
            internal Entry(int key, long g, long h) { Key = key; G = g; H = h; F = g + h; }
        }

        public GridPathfinder(NavGrid grid)
        {
            this.grid = grid ?? throw new ArgumentNullException(nameof(grid));
            costs = new long[grid.Count]; stamps = new uint[grid.Count]; parents = new int[grid.Count]; path = new int[grid.Count];
            heap = new Entry[16];
        }

        // Weighted search over the final walkable cells: entering a cell costs (1000 or 1414) x CostAt(cell).
        // Tie-breaks are unchanged, so cost tables that are all 1 give the same paths as the plain constructor.
        public GridPathfinder(TerrainTables terrain) : this(terrain?.Nav ?? throw new ArgumentNullException(nameof(terrain)))
        {
            this.terrain = terrain;
            long min = long.MaxValue;
            for (int key = 0; key < grid.Count; key++)
                if (grid.IsPassable(key % grid.Width, key / grid.Width) && terrain.CostAt(key) < min) min = terrain.CostAt(key);
            minCellCost = min == long.MaxValue ? 1 : min;
        }

        public GridPathStatus Find(SimVec3 start, SimVec3 target)
        {
            grid.TryCell(start, out int from);
            grid.TryCell(target, out int to);
            return Find(from, to);
        }

        public GridPathStatus Find(int start, int target)
        {
            pathCount = 0; TotalCostWide = 0; heapCount = 0;
            if (start < 0 || start >= grid.Count || !grid.IsPassable(start % grid.Width, start / grid.Width))
                return GridPathStatus.Rejected;
            if (target < 0 || target >= grid.Count || !grid.IsPassable(target % grid.Width, target / grid.Width))
                return GridPathStatus.Unreachable;
            if (++generation == 0)
            {
                // Wrapped after 2^32 - 1 searches: old stamps could match again, so forget them all once.
                Array.Clear(stamps, 0, stamps.Length);
                generation = 1;
            }
            stamps[start] = generation; costs[start] = 0; parents[start] = -1;
            Push(new Entry(start, 0, Heuristic(start, target)));
            while (heapCount != 0)
            {
                Entry current = Pop();
                if (current.G != costs[current.Key]) continue;
                if (current.Key == target)
                {
                    Complete(target);
                    return GridPathStatus.Found;
                }
                int x = current.Key % grid.Width, z = current.Key / grid.Width;
                for (int direction = 0; direction < 8; direction++)
                {
                    // N, E, S, W, NE, SE, SW, NW.
                    int dx = direction == 1 || direction == 4 || direction == 5 ? 1
                        : direction == 3 || direction == 6 || direction == 7 ? -1 : 0;
                    int dz = direction == 0 || direction == 4 || direction == 7 ? 1
                        : direction == 2 || direction == 5 || direction == 6 ? -1 : 0;
                    if (!grid.IsPassable(x + dx, z + dz)) continue;
                    bool diagonal = dx != 0 && dz != 0;
                    if (diagonal && (!grid.IsPassable(x + dx, z) || !grid.IsPassable(x, z + dz))) continue;
                    int next = (z + dz) * grid.Width + x + dx;
                    long g = current.G + (diagonal ? 1414 : 1000) * (terrain == null ? 1 : terrain.CostAt(next));
                    if (stamps[next] == generation && g >= costs[next]) continue;
                    stamps[next] = generation; costs[next] = g; parents[next] = current.Key;
                    Push(new Entry(next, g, Heuristic(next, target)));
                }
            }
            return GridPathStatus.Unreachable;
        }

        private long Heuristic(int key, int target)
        {
            int dx = key % grid.Width - target % grid.Width;
            int dz = key / grid.Width - target / grid.Width;
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

        // Heap order uses the entry's immutable insertion values, never the mutable cost table.
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
