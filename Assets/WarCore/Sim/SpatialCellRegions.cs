namespace WarCore.Sim
{
    internal struct SpatialCellRegion
    {
        internal long X0;
        internal long Y0;
        internal long Z0;
        internal long X1;
        internal long Y1;
        internal long Z1;
        internal int Slot;
        internal int End;
    }

    // Legal tiny raw cell sizes can exhaust the reach of the five fixed levels. A compact
    // top-cell bounding tree bounds those queries without scanning the directory or live bodies.
    internal sealed class SpatialCellRegions
    {
        internal readonly SpatialCellRegion[] Nodes;
        private readonly int[] order;
        internal int Count;

        internal SpatialCellRegions(int capacity)
        {
            Nodes = new SpatialCellRegion[capacity];
            order = new int[capacity];
        }

        internal void Build(SpatialCellTable table)
        {
            Count = 0;
            int count = 0;
            for (int i = 0; i < table.Count; i++)
            {
                int slot = table.Occupied[i];
                if (table.Cells[slot].Level == 14)
                    order[count++] = slot;
            }
            if (count != 0)
                Build(table.Cells, 0, count, 0);
        }

        private int Build(SpatialCell[] cells, int start, int count, int depth)
        {
            int middle = start + count / 2;
            Select(cells, start, start + count - 1, middle, depth % 3);
            int at = Count++;
            SpatialCell cell = cells[order[middle]];
            var node = new SpatialCellRegion { X0 = cell.X, X1 = cell.X, Y0 = cell.Y, Y1 = cell.Y, Z0 = cell.Z, Z1 = cell.Z, Slot = order[middle] };
            int left = middle - start;
            int right = count - left - 1;
            // Balanced halves bound setup recursion by log2(reserved cells), independent of keys.
            if (left != 0)
                Expand(ref node, Nodes[Build(cells, start, left, depth + 1)]);
            if (right != 0)
                Expand(ref node, Nodes[Build(cells, middle + 1, right, depth + 1)]);
            node.End = Count;
            Nodes[at] = node;
            return at;
        }

        private void Select(SpatialCell[] cells, int low, int high, int target, int axis)
        {
            // Stable key tie-breaks make the partition independent of hash-table slot layout.
            while (low < high)
            {
                SpatialCell pivot = cells[order[low + (high - low) / 2]];
                int left = low;
                int right = high;
                while (left <= right)
                {
                    while (Compare(cells[order[left]], pivot, axis) < 0)
                        left++;
                    while (Compare(cells[order[right]], pivot, axis) > 0)
                        right--;
                    if (left <= right)
                    {
                        int swap = order[left];
                        order[left++] = order[right];
                        order[right--] = swap;
                    }
                }
                if (target <= right)
                    high = right;
                else if (target >= left)
                    low = left;
                else
                    return;
            }
        }

        private static int Compare(SpatialCell a, SpatialCell b, int axis)
        {
            long first = axis == 0 ? a.X : axis == 1 ? a.Y : a.Z;
            long second = axis == 0 ? b.X : axis == 1 ? b.Y : b.Z;
            int result = first.CompareTo(second);
            if (result == 0)
                result = a.X.CompareTo(b.X);
            if (result == 0)
                result = a.Y.CompareTo(b.Y);
            if (result == 0)
                result = a.Z.CompareTo(b.Z);
            return result;
        }

        private static void Expand(ref SpatialCellRegion a, SpatialCellRegion b)
        {
            if (b.X0 < a.X0)
                a.X0 = b.X0;
            if (b.Y0 < a.Y0)
                a.Y0 = b.Y0;
            if (b.Z0 < a.Z0)
                a.Z0 = b.Z0;
            if (b.X1 > a.X1)
                a.X1 = b.X1;
            if (b.Y1 > a.Y1)
                a.Y1 = b.Y1;
            if (b.Z1 > a.Z1)
                a.Z1 = b.Z1;
        }

        internal long AllocatedBytes => Nodes.LongLength * 56 + order.LongLength * 4;
        internal long UsedBytes => (long)Count * 60;
    }
}
