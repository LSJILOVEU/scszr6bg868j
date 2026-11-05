using System;

namespace WarCore.Sim
{
    public enum GridPathStatus { Found, Unreachable, Rejected }

    // Synchronous search. Path borrows storage until the next Find call; consume it immediately.
    public sealed class GridPathfinder
    {
        private readonly NavGrid grid;
        private readonly int[] costs;
        private readonly int[] parents;
        private readonly int[] path;
        private Entry[] heap;
        private int heapCount;
        private int pathCount;
        public ReadOnlySpan<int> Path => new ReadOnlySpan<int>(path, 0, pathCount);
        public int TotalCost { get; private set; }

        private readonly struct Entry
        {
            internal readonly int F, H, Key, G;
            internal Entry(int key, int g, int h) { Key = key; G = g; H = h; F = g + h; }
        }

        public GridPathfinder(NavGrid grid)
        {
            this.grid = grid ?? throw new ArgumentNullException(nameof(grid));
            costs = new int[grid.Count]; parents = new int[grid.Count]; path = new int[grid.Count];
            heap = new Entry[16];
        }

        public GridPathStatus Find(SimVec3 start, SimVec3 target)
        {
            grid.TryCell(start, out int from);
            grid.TryCell(target, out int to);
            return Find(from, to);
        }

        public GridPathStatus Find(int start, int target)
        {
            pathCount = 0; TotalCost = 0; heapCount = 0;
            if (start < 0 || start >= grid.Count || !grid.IsPassable(start % grid.Width, start / grid.Width))
                return GridPathStatus.Rejected;
            if (target < 0 || target >= grid.Count || !grid.IsPassable(target % grid.Width, target / grid.Width))
                return GridPathStatus.Unreachable;
            for (int i = 0; i < costs.Length; i++) costs[i] = int.MaxValue;
            costs[start] = 0; parents[start] = -1;
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
                    int g = current.G + (diagonal ? 1414 : 1000);
                    if (g >= costs[next]) continue;
                    costs[next] = g; parents[next] = current.Key;
                    Push(new Entry(next, g, Heuristic(next, target)));
                }
            }
            return GridPathStatus.Unreachable;
        }

        private int Heuristic(int key, int target)
        {
            int dx = key % grid.Width - target % grid.Width;
            int dz = key / grid.Width - target / grid.Width;
            if (dx < 0) dx = -dx;
            if (dz < 0) dz = -dz;
            return 1000 * (dx + dz) - 586 * (dx < dz ? dx : dz);
        }

        private void Complete(int target)
        {
            TotalCost = costs[target];
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
