using System;

namespace WarCore.Sim
{
    internal sealed partial class GridSpatialIndex
    {
        internal int Execute(GridQueryContext context, in SpatialRequest request, Span<EntityId> entities, Span<RayHit> hits)
        {
            context.Begin();
            int count = 0;
            SelectPartitions(context, request.LayerMask, out SpatialPartitionState first, out SpatialPartitionState second);
            if (first == null)
                return 0;
            AccumulateLarge(context, first, in request, entities, hits, ref count);
            if (second != null)
                AccumulateLarge(context, second, in request, entities, hits, ref count);
            var scan = new SpatialGridScan(first.Table, context, in request, cellSize, second?.Table, first == active.Static);
            while (scan.MoveNext(out int slot, out _))
            {
                SpatialCellTable table = scan.Table;
                ref SpatialCell cell = ref table.Cells[slot];
                if (!VisitCell(context, in cell, request.LayerMask))
                    continue;
                for (int i = 0; i < cell.Count; i++)
                {
                    int id = table.Members[cell.Start + i];
                    if (Candidate(context, id, in request, scan.Coarse, scan.Stationary) && SpatialQueryBase.Add(in active.Bodies.Geometry[id], in request, entities, hits, ref count))
                        context.NarrowPhaseCount++;
                }
            }
            SpatialQueryBase.Sort(request, entities, hits, count, store, context.SortKeys);
            return count;
        }

        private void AccumulateLarge(GridQueryContext context, SpatialPartitionState partition,
            in SpatialRequest request, Span<EntityId> entities, Span<RayHit> hits, ref int count)
        {
            for (int i = 0; i < partition.LargeCount; i++)
            {
                int id = partition.LargeIds[i];
                if (Candidate(context, id, in request, true, partition == active.Static) && SpatialQueryBase.Add(in active.Bodies.Geometry[id], in request, entities, hits, ref count))
                    context.NarrowPhaseCount++;
            }
        }

        internal void ExecuteMode(GridQueryContext context, in SpatialRequest request, ref SpatialHitCollector collector)
        {
            context.Begin();
            AccumulateMode(context, in request, ref collector);
        }

        // One event stream bounds both partitions; finish both bypass lists before any stop.
        internal void AccumulateMode(GridQueryContext context, in SpatialRequest request, ref SpatialHitCollector collector)
        {
            SelectPartitions(context, request.LayerMask, out SpatialPartitionState first, out SpatialPartitionState second);
            if (first == null)
                return;
            if (AccumulateLargeMode(context, first, in request, ref collector))
                return;
            if (second != null && AccumulateLargeMode(context, second, in request, ref collector))
                return;
            var scan = new SpatialGridScan(first.Table, context, in request, cellSize, second?.Table, first == active.Static);
            ulong directionSquared = request.Kind == SpatialQueryKind.Ray ? SpatialModeMath.DirectionSquared(request.Direction) : 0;
            while (scan.MoveNext(out int slot, out RayCellEvent next))
            {
                // Every nonlarge body occupies all intersecting cells at the selected level.
                // Same-event neighbours and the whole winning raw bucket remain eligible.
                if (scan.OrderedRay && collector.HasBest && SpatialModeMath.BeyondBest(in next, directionSquared, collector.Best.Distance.Raw, request.Range.Raw))
                    return;
                SpatialCellTable table = scan.Table;
                ref SpatialCell cell = ref table.Cells[slot];
                if (!VisitCell(context, in cell, request.LayerMask))
                    continue;
                for (int i = 0; i < cell.Count; i++)
                    if (AddMode(context, table.Members[cell.Start + i], in request, ref collector, scan.Coarse, scan.Stationary))
                        return;
            }
        }

        private bool AccumulateLargeMode(GridQueryContext context, SpatialPartitionState partition,
            in SpatialRequest request, ref SpatialHitCollector collector)
        {
            int[] ids = collector.UseStableOrder && partition.Table.Count == 0 ? partition.OrderedLargeIds : partition.LargeIds;
            for (int i = 0; i < partition.LargeCount; i++)
                if (AddMode(context, ids[i], in request, ref collector, true, partition == active.Static))
                    return true;
            return false;
        }

        private bool VisitPartition(GridQueryContext context, SpatialPartitionState partition, uint mask)
        {
            // Independent adapters can read unsynchronized layers, so only the owned path
            // uses the published union. Empty partitions are safe to skip in both paths.
            bool visit = partition.BodyCount != 0 && mask != 0 && (readLiveLayers || (partition.LayerUnion & mask) != 0);
            if (context.Diagnostics != null)
            {
                if (visit)
                    context.Diagnostics.VisitedPartitions++;
                else
                    context.Diagnostics.SkippedPartitions++;
            }
            return visit;
        }

        private void SelectPartitions(GridQueryContext context, uint mask, out SpatialPartitionState first, out SpatialPartitionState second)
        {
            bool stationary = VisitPartition(context, active.Static, mask);
            bool unit = VisitPartition(context, active.Unit, mask);
            first = stationary ? active.Static : unit ? active.Unit : null;
            second = stationary && unit ? active.Unit : null;
        }

        private bool VisitCell(GridQueryContext context, in SpatialCell cell, uint mask)
        {
            if (cell.Count == 0)
                return false;
            context.VisitedCells++;
            if (!readLiveLayers && (cell.Mask & mask) == 0)
            {
                if (context.Diagnostics != null)
                    context.Diagnostics.MaskRejectedCells++;
                return false;
            }
            return true;
        }

        private bool Candidate(GridQueryContext context, int id, in SpatialRequest request, bool coarse, bool stationary)
        {
            if (context.Diagnostics != null)
            {
                context.Diagnostics.LeafMembers++;
                if (stationary)
                    context.Diagnostics.StaticMembers++;
                else
                    context.Diagnostics.UnitMembers++;
                context.Diagnostics.DedupChecks++;
            }
            if (!context.Visit(id))
            {
                if (context.Diagnostics != null)
                    context.Diagnostics.DuplicateSkips++;
                return false;
            }
            ref readonly SpatialBody body = ref active.Bodies.Geometry[id];
            if (!store.IsAlive(body.Entity) || (Layer(in body) & request.LayerMask) == 0)
                return false;
            if (coarse && !CoarseOverlap(in body, in request))
                return false;
            context.CandidateCount++;
            return true;
        }

        private static bool CoarseOverlap(in SpatialBody body, in SpatialRequest request)
        {
            // Coarse cells intentionally admit extra bodies; cheap bounds restore useful candidates.
            if (request.Kind == SpatialQueryKind.Ray)
                return SpatialBounds.RayMayHit(in body, in request);
            if (body.Collider.ShapeKind != ShapeKind.VerticalCapsule)
                return BoxCoarseOverlap(in body, in request);
            long r = body.Collider.Radius.Raw;
            long h = body.Collider.Height.Raw / 2;
            return body.Position.X.Raw - r <= request.Maximum.X.Raw && body.Position.X.Raw + r >= request.Minimum.X.Raw
                && body.Position.Y.Raw - h <= request.Maximum.Y.Raw && body.Position.Y.Raw + h >= request.Minimum.Y.Raw
                && body.Position.Z.Raw - r <= request.Maximum.Z.Raw && body.Position.Z.Raw + r >= request.Minimum.Z.Raw;
        }

        // Out of line so the capsule branch above keeps its pre-3a size.
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static bool BoxCoarseOverlap(in SpatialBody body, in SpatialRequest request)
        {
            long rx = SpatialBox.ExtentX(body.Collider);
            long rz = SpatialBox.ExtentZ(body.Collider);
            long h = body.Collider.Height.Raw / 2;
            return body.Position.X.Raw - rx <= request.Maximum.X.Raw && body.Position.X.Raw + rx >= request.Minimum.X.Raw
                && body.Position.Y.Raw - h <= request.Maximum.Y.Raw && body.Position.Y.Raw + h >= request.Minimum.Y.Raw
                && body.Position.Z.Raw - rz <= request.Maximum.Z.Raw && body.Position.Z.Raw + rz >= request.Minimum.Z.Raw;
        }

        private bool AddMode(GridQueryContext context, int id, in SpatialRequest request, ref SpatialHitCollector collector, bool coarse, bool stationary)
        {
            if (!Candidate(context, id, in request, coarse, stationary))
                return false;
            ref readonly SpatialBody body = ref active.Bodies.Geometry[id];
            bool stop = collector.Add(in body, in request, out bool precise);
            if (precise)
                context.NarrowPhaseCount++;
            return stop;
        }
    }
}
