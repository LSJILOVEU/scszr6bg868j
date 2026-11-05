using System;
using System.Collections.Generic;
using WarCore.Sim;
using WarCore.Tests.Oracle;

namespace WarCore.Tests
{
    // The exhaustive provider uses frozen test arithmetic so grid regressions cannot change its answer.
    public sealed class NaiveSpatialQuery : SpatialQueryBase
    {
        private readonly SpatialBody[] bodies;
        public NaiveSpatialQuery(IEnumerable<SpatialBody> source)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            bodies = new List<SpatialBody>(source).ToArray();
            var ids = new HashSet<int>();
            foreach (SpatialBody body in bodies)
                if (body.Entity.Generation == 0 || !ids.Add(body.Entity.Id))
                    throw new ArgumentException("Invalid or duplicate spatial entity.");
        }

        protected override int Execute(SpatialRequest request, Span<EntityId> entities, Span<RayHit> hits)
        {
            if (request.LayerMask == 0)
                return 0;
            int count = 0;
            foreach (SpatialBody body in bodies)
            {
                if ((body.Collider.Layer & request.LayerMask) == 0)
                    continue;
                if (!OracleSpatialGeometry.Intersects(body, request, out SimFix distance))
                    continue;
                if (count == (request.Kind == SpatialQueryKind.Ray ? hits.Length : entities.Length))
                    throw new ArgumentException("Query output buffer is too small.");
                if (request.Kind == SpatialQueryKind.Ray)
                    hits[count] = new RayHit(body.Entity, distance);
                else
                    entities[count] = body.Entity;
                count++;
            }

            Sort(request, entities, hits, count);
            return count;
        }
    }
}
