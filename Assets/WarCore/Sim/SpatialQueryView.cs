using System;

namespace WarCore.Sim
{
    // Binding is a cold operation. Each caller owns its context; the shared index stays read-only.
    public partial class SpatialQueryView : SpatialQueryBase, ISpatialQueryModes
    {
        internal GridSpatialIndex Index { get; }
        public GridQueryContext QueryContext { get; }
        public int LastVisitedCells => QueryContext.VisitedCells;
        public int LastCandidateCount => QueryContext.CandidateCount;
        public int LastNarrowPhaseCount => QueryContext.NarrowPhaseCount;
        public int LastTruncatedHits => QueryContext.TruncatedHits;
        internal SpatialQueryDiagnostics Diagnostics
        {
            get => QueryContext.Diagnostics;
            set => QueryContext.Diagnostics = value;
        }

        internal SpatialQueryView(GridSpatialIndex index, GridQueryContext context)
        {
            Index = index ?? throw new ArgumentNullException(nameof(index));
            QueryContext = context ?? throw new ArgumentNullException(nameof(context));
            if (context.Capacity < index.Capacity)
                throw new ArgumentException("Query context capacity is too small.", nameof(context));
        }

        public ISpatialQueryModes Bind(GridQueryContext context) => new SpatialQueryView(Index, context);

        protected override int Execute(SpatialRequest request, Span<EntityId> entities, Span<RayHit> hits)
        {
            Index.ValidateRead();
            return Index.Execute(QueryContext, request, entities, hits);
        }
    }
}
