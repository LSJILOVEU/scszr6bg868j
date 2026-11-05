namespace WarCore.Sim
{
    // Compatibility adapter for independently owned indexes, including presentation queries.
    // Sim exposes a SpatialQueryView instead, so content cannot update its index.
    public sealed class GridSpatialQuery : SpatialQueryView
    {
        public int MembershipCount => Index.MembershipCount;
        public PostureGeometry Postures => Index.Postures;

        public GridSpatialQuery(EntityStore store, ComponentType<Position> positions, ComponentType<Collider> colliders,
            SimFix cellSize, int maxEntities = 4096, int maxMemberships = 32768,
            PostureGeometry postures = null, GridQueryContext queryContext = null)
            : base(new GridSpatialIndex(store, positions, colliders, cellSize, maxEntities, maxMemberships, postures),
                queryContext ?? new GridQueryContext(maxEntities))
        {
        }

        internal GridSpatialQuery(GridSpatialIndex index, GridQueryContext context) : base(index, context)
        {
        }

        public void Rebuild() => Index.Rebuild();
        public void Upsert(EntityId entity) => Index.Upsert(entity);
        public bool Remove(EntityId entity) => Index.Remove(entity);
        public void Synchronize() => Index.Synchronize();
    }
}
