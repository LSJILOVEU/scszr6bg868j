namespace WarCore.Sim
{
    // Local value-type traversal state is private to a query invocation; the table stays read-only.
    internal struct SpatialGridScan
    {
        private SpatialCellTable table;
        private readonly SpatialCellTable first;
        private readonly SpatialCellTable second;
        private readonly bool firstStatic;
        private bool secondPending;
        private int keyHash;
        private RayCellEvent currentRay;
        private readonly GridQueryContext context;
        private readonly int level;
        private readonly bool ray;
        private readonly bool regionScan;
        private readonly long x0;
        private readonly long y0;
        private readonly long z0;
        private readonly long x1;
        private readonly long y1;
        private readonly long z1;
        private long x;
        private long y;
        private long z;
        private int region;
        private bool started;
        private bool ended;

        internal SpatialGridScan(SpatialCellTable table, GridQueryContext context, in SpatialRequest request, long baseSize,
            SpatialCellTable second = null, bool firstStatic = false)
        {
            this.table = table;
            first = table;
            this.second = second;
            this.firstStatic = firstStatic;
            secondPending = false;
            keyHash = 0;
            currentRay = default;
            this.context = context;
            ray = request.Kind == SpatialQueryKind.Ray;
            long width = ray ? (request.Range.Raw + 63) / 64 : Max(request.Maximum.X.Raw - request.Minimum.X.Raw,
                Max(request.Maximum.Y.Raw - request.Minimum.Y.Raw, request.Maximum.Z.Raw - request.Minimum.Z.Raw)) / 4;
            int index = 0;
            while (index < 4 && Size(baseSize, GridSpatialIndex.Level(index)) < width)
                index++;
            level = GridSpatialIndex.Level(index);
            long size = Size(baseSize, level);
            x0 = SpatialGridBounds.Floor(request.Minimum.X.Raw, size);
            y0 = SpatialGridBounds.Floor(request.Minimum.Y.Raw, size);
            z0 = SpatialGridBounds.Floor(request.Minimum.Z.Raw, size);
            x1 = SpatialGridBounds.Floor(request.Maximum.X.Raw, size);
            y1 = SpatialGridBounds.Floor(request.Maximum.Y.Raw, size);
            z1 = SpatialGridBounds.Floor(request.Maximum.Z.Raw, size);
            long nx = x1 - x0 + 1;
            long ny = y1 - y0 + 1;
            long nz = z1 - z0 + 1;
            // Tiny grids use the top-cell bounds tree instead of an unbounded empty walk.
            regionScan = ray ? request.Range.Raw / size > 128 : nx > 4096 || ny > 4096 / nx || nz > 4096 / nx / ny;
            x = x0;
            y = y0;
            z = z0;
            region = 0;
            started = false;
            ended = false;
            if (ray && !regionScan)
                context.Traversal = new RayCellTraversal(in request, size);
            if (context.Diagnostics != null)
                context.Diagnostics.TraversalStarts++;
        }

        private static long Size(long size, int level) => size > long.MaxValue >> level ? long.MaxValue : size << level;
        private static long Max(long a, long b) => a > b ? a : b;
        internal bool Coarse => level != 0;
        internal bool OrderedRay => ray && !regionScan;
        internal SpatialCellTable Table => table;
        internal bool Stationary => firstStatic && table == first;

        internal bool MoveNext(out int slot, out RayCellEvent next)
        {
            next = default;
            slot = 0;
            if (regionScan)
            {
                // Region trees have different occupied bounds, so each keeps its own pruning.
                if (MoveNextRegion(out slot))
                    return true;
                if (second == null || table == second)
                    return false;
                table = second;
                region = 0;
                return MoveNextRegion(out slot);
            }
            if (secondPending)
            {
                secondPending = false;
                table = second;
                next = currentRay;
                slot = Find();
                return true;
            }
            table = first;
            if (ray)
            {
                if (!context.Traversal.MoveNext(out next))
                    return false;
                x = next.X;
                y = next.Y;
                z = next.Z;
            }
            else
            {
                if (ended)
                    return false;
                if (started)
                {
                    if (z < z1)
                        z++;
                    else if (y < y1)
                    {
                        z = z0;
                        y++;
                    }
                    else if (x < x1)
                    {
                        z = z0;
                        y = y0;
                        x++;
                    }
                    else
                    {
                        ended = true;
                        return false;
                    }
                }
                started = true;
            }
            if (context.Diagnostics != null)
                context.Diagnostics.WalkCalls++;
            // The cell sequence and hash depend on geometry, not on partition membership.
            keyHash = SpatialCellTable.Hash(x, y, z, level);
            currentRay = next;
            secondPending = second != null;
            slot = Find();
            return true;
        }

        private int Find()
        {
            if (context.Diagnostics != null)
            {
                if (Stationary)
                    context.Diagnostics.StaticLookups++;
                else
                    context.Diagnostics.UnitLookups++;
            }
            return table.FindHashed(x, y, z, level, keyHash, context.Diagnostics);
        }

        private bool MoveNextRegion(out int slot)
        {
            slot = 0;
            while (region < table.Regions.Count)
            {
                SpatialCellRegion node = table.Regions.Nodes[region];
                if (context.Diagnostics != null)
                {
                    context.Diagnostics.RegionNodes++;
                    if (Stationary)
                        context.Diagnostics.StaticRegionNodes++;
                    else
                        context.Diagnostics.UnitRegionNodes++;
                }
                if (node.X1 < x0 || node.X0 > x1 || node.Y1 < y0 || node.Y0 > y1 || node.Z1 < z0 || node.Z0 > z1)
                {
                    region = node.End;
                    continue;
                }
                region++;
                slot = node.Slot;
                ref SpatialCell cell = ref table.Cells[slot];
                if (cell.X >= x0 && cell.X <= x1 && cell.Y >= y0 && cell.Y <= y1 && cell.Z >= z0 && cell.Z <= z1)
                    return true;
            }
            return false;
        }
    }
}
