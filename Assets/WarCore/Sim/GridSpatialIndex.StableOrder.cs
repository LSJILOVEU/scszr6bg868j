namespace WarCore.Sim
{
    internal sealed partial class GridSpatialIndex
    {
        private void SortLarge(int[] ids, int count)
        {
            for (int root = count/2-1; root >= 0; root--)
                SiftLarge(ids,root,count,ids[root]);
            for (int end = count-1; end > 0; end--)
            {
                int value = ids[end];
                ids[end] = ids[0];
                SiftLarge(ids,0,end,value);
            }
        }
        private void SiftLarge(int[] ids, int root, int count, int value)
        {
            ulong key = store.StableAt(value).Value;
            while (root < count/2)
            {
                int child = root*2+1;
                if (child+1 < count && store.StableAt(ids[child]).Value < store.StableAt(ids[child+1]).Value)
                    child++;
                if (key >= store.StableAt(ids[child]).Value)
                    break;
                ids[root] = ids[child];
                root = child;
            }
            ids[root] = value;
        }

        // A cold audit detects stale or corrupt derived order without charging each query for validation.
        internal void ValidateStableQueryOrder()
        {
            ValidateStableQueryOrder(active.Static);
            ValidateStableQueryOrder(active.Unit);
        }

        private void ValidateStableQueryOrder(SpatialPartitionState partition)
        {
            if (partition.OrderedLargeIds == null)
            {
                if (store.IdentityMode == IdentityMode.StableWorld)
                    throw new SimBoundaryException("Stable query order is missing.");
                return;
            }
            if (partition.Table.Count != 0)
                return;
            ulong previous = 0;
            for (int i = 0; i < partition.LargeCount; i++)
            {
                int id = partition.OrderedLargeIds[i];
                if (id < 0 || id >= Capacity || !active.Present[id] || active.Partition(id) != partition
                    || active.Bounds[id].Volume(maxCellsPerBody) >= 0)
                    throw new SimBoundaryException("Stable query order contains an invalid bypass member.");
                ulong key = store.StableAt(id).Value;
                if (i > 0 && key <= previous)
                    throw new SimBoundaryException("Stable query order disagrees with the identity table.");
                previous = key;
            }
        }
    }
}
