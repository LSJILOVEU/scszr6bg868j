using System;

namespace WarCore.Sim
{
    public readonly struct SpatialHit
    {
        public EntityId Entity { get; }
        public SimFix Distance { get; }

        public SpatialHit(EntityId entity, SimFix distance)
        {
            Entity = entity;
            Distance = distance;
        }
    }

    public readonly struct SpatialQueryResult
    {
        public int Written { get; }
        public int TotalHits { get; }
        public bool Truncated => TotalHits > Written;

        internal SpatialQueryResult(int written, int totalHits)
        {
            Written = written;
            TotalHits = totalHits;
        }
    }

    // A separate interface preserves existing providers and their full-output buffer contract.
    public interface ISpatialQueryModes : ISpatialQuery
    {
        bool Any(in SpatialRequest request);
        bool Nearest(in SpatialRequest request, out SpatialHit hit);
        bool Nearest(in SpatialRequest request, SimVec3 referencePoint, out SpatialHit hit);
        SpatialQueryResult AllCapped(in SpatialRequest request, int k, Span<SpatialHit> results);
        SpatialQueryResult AllCapped(in SpatialRequest request, SimVec3 referencePoint, int k, Span<SpatialHit> results);
    }

    // Requests contain validated shape inputs, so selecting a mode never changes the hit predicate.
    public static class SpatialQueries
    {
        public static SpatialRequest Sphere(SimVec3 center, SimFix radius, uint layerMask = uint.MaxValue)
            => new SpatialRequest(SpatialQueryKind.Sphere, center, radius, default, default, default, 0, layerMask);

        public static SpatialRequest Cylinder(SimVec3 center, SimFix radius, SimFix halfHeight, uint layerMask = uint.MaxValue)
            => new SpatialRequest(SpatialQueryKind.Cylinder, center, radius, default, default, halfHeight, 0, layerMask);

        public static SpatialRequest Sector(SimVec3 vertex, SimFix radius, SimVec3 direction, int cosineHalfAnglePermille, SimFix halfHeight, uint layerMask = uint.MaxValue)
        {
            direction.ValidateWorld();
            if (direction.Y.Raw != 0 || (direction.X.Raw == 0 && direction.Z.Raw == 0))
                throw new ArgumentException("Sector direction must be horizontal and nonzero.");
            if (cosineHalfAnglePermille < -1000 || cosineHalfAnglePermille > 1000)
                throw new ArgumentOutOfRangeException(nameof(cosineHalfAnglePermille));
            return new SpatialRequest(SpatialQueryKind.Sector, vertex, radius, direction, default, halfHeight, cosineHalfAnglePermille, layerMask);
        }

        public static SpatialRequest Capsule(SimVec3 start, SimVec3 end, SimFix radius, uint layerMask = uint.MaxValue)
        {
            // Validate both positions before subtraction; arbitrary raw input must not overflow first.
            start.ValidateWorld();
            end.ValidateWorld();
            SimVec3 delta = end - start;
            ulong limit = SimVec3.WorldLimitMetres * SimFix.Scale;
            if (delta.LengthSquared.Raw.Magnitude.CompareTo(FixedUInt128.Multiply(limit, limit)) > 0)
                throw new ArgumentOutOfRangeException(nameof(end), "Sweep length exceeds 16384 metres.");
            return new SpatialRequest(SpatialQueryKind.Capsule, start, radius, delta, default, default, 0, layerMask);
        }

        public static SpatialRequest Ray(SimVec3 origin, SimVec3 direction, SimFix maxDistance, uint layerMask = uint.MaxValue)
        {
            direction.ValidateWorld();
            if (direction.LengthSquared.Sign == 0)
                throw new ArgumentException("Ray direction must be nonzero.");
            return new SpatialRequest(SpatialQueryKind.Ray, origin, default, direction, maxDistance, default, 0, layerMask);
        }
    }
}
