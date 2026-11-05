using System;

namespace WarCore.Sim
{
    public partial class SpatialQueryView
    {
        public bool Any(in SpatialRequest request)
        {
            var collector = new SpatialHitCollector(SpatialResultMode.Any, default, default, 0, Index.Store, QueryContext.SortKeys);
            Index.ValidateRead();
            Index.ExecuteMode(QueryContext, in request, ref collector);
            return collector.TotalHits != 0;
        }

        public bool Nearest(in SpatialRequest request, out SpatialHit hit)
            => Nearest(in request, request.Origin, out hit);

        public bool Nearest(in SpatialRequest request, SimVec3 referencePoint, out SpatialHit hit)
        {
            referencePoint.ValidateWorld();
            var collector = new SpatialHitCollector(SpatialResultMode.Nearest, referencePoint, default, 0, Index.Store, QueryContext.SortKeys);
            Index.ValidateRead();
            Index.ExecuteMode(QueryContext, in request, ref collector);
            hit = collector.Best;
            return collector.HasBest;
        }

        public SpatialQueryResult AllCapped(in SpatialRequest request, int k, Span<SpatialHit> results)
            => AllCapped(in request, request.Origin, k, results);

        public SpatialQueryResult AllCapped(in SpatialRequest request, SimVec3 referencePoint, int k, Span<SpatialHit> results)
        {
            if (k < 0)
                throw new ArgumentOutOfRangeException(nameof(k), "Query cap must be nonnegative.");
            referencePoint.ValidateWorld();
            var collector = new SpatialHitCollector(SpatialResultMode.AllCapped, referencePoint, results, k, Index.Store, QueryContext.SortKeys);
            Index.ValidateRead();
            Index.ExecuteMode(QueryContext, in request, ref collector);
            SpatialQueryResult result = collector.Finish();
            QueryContext.TruncatedHits = (int)((long)result.TotalHits - result.Written);
            return result;
        }

    }
}
