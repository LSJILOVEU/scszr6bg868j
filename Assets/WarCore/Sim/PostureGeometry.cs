using System;

namespace WarCore.Sim
{
    // One adapter supplies both broad-phase bounds and every narrow-phase query shape.
    public sealed class PostureGeometry
    {
        private readonly EntityStore entities;
        private readonly ComponentType<Posture> postures;
        public PostureCatalog Catalog { get; }
        internal string ComponentId => postures.Storage.Name;
        internal EntityStore Entities => entities;
        internal void ProtectWrites() => entities.ProtectWrites(postures);

        public PostureGeometry(EntityStore entities, ComponentType<Posture> postures, PostureCatalog catalog)
        {
            if (entities == null || postures.Owner != entities)
                throw new ArgumentException("Posture handle must belong to the supplied store.");
            this.entities = entities;
            this.postures = postures;
            Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        }

        private int Current(EntityId entity) => entities.TryGet(postures, entity, out Posture value) ? value.CurrentId : 0;

        public SpatialBody Body(EntityId entity, SimVec3 baselineCenter, Collider baseline)
        {
            return BodyFor(entity, baselineCenter, baseline, Current(entity));
        }

        internal SpatialBody BodyFor(EntityId entity, SimVec3 center, Collider baseline, int postureId)
        {
            if (postureId == 0)
                return new SpatialBody(entity, center, baseline);
            // Posture heights are defined for upright capsules; box postures are deferred past block 3a.
            if (baseline.ShapeKind != ShapeKind.VerticalCapsule)
                throw new SimBoundaryException("M0C_SHAPE_BOX_POSTURE");
            Collider collider = baseline.WithHeight(Catalog.Get(postureId).Height);
            // Subtract separately truncated half heights to preserve the exact baseline foot raw unit.
            long offset = collider.Height.Raw / 2 - baseline.Height.Raw / 2;
            long y = SimChecked.Add(center.Y.Raw, offset);
            return new SpatialBody(entity, new SimVec3(center.X, SimFix.FromRaw(y), center.Z), collider);
        }

        public SimFix SpeedFactor(EntityId entity)
        {
            int id = Current(entity);
            return id == 0 ? SimFix.FromRaw(SimFix.Scale) : Catalog.Get(id).SpeedFactor;
        }

        public SimFix DetectionDistance(EntityId entity, SimFix baseline)
        {
            if (baseline.Raw < 0)
                throw new ArgumentOutOfRangeException(nameof(baseline));
            int id = Current(entity);
            return id == 0 ? baseline : PostureScale.Apply(baseline, Catalog.Get(id).DetectionDistanceFactor);
        }

        public SimFix HitModifier(EntityId entity)
        {
            int id = Current(entity);
            return id == 0 ? default : Catalog.Get(id).HitModifier;
        }
    }

    // A dimensionless factor needs a full-range scaled result, not the square-metre product guard.
    // This boundary belongs to posture data; the existing scalar math contract is unchanged.
    public static class PostureScale
    {
        public static SimFix Apply(SimFix value, SimFix factor)
        {
            if (factor.Raw < 0)
                throw new ArgumentOutOfRangeException(nameof(factor));
            ulong magnitude = value.Raw < 0 ? (ulong)(-(value.Raw + 1)) + 1 : (ulong)value.Raw;
            FixedUInt128 product = FixedUInt128.Multiply(magnitude, (ulong)factor.Raw).ShiftRight(16);
            ulong limit = value.Raw < 0 ? 1UL << 63 : long.MaxValue;
            if (product.High != 0 || product.Low > limit)
                throw new SimBoundaryException("Posture scaling exceeds the signed fixed-point range.");
            if (value.Raw < 0 && product.Low == (1UL << 63))
                return SimFix.FromRaw(long.MinValue);
            long raw = (long)product.Low;
            return SimFix.FromRaw(value.Raw < 0 ? -raw : raw);
        }
    }
}
