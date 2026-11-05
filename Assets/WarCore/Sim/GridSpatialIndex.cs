using System;

namespace WarCore.Sim
{
    // The index contains only derived geometry and membership. Queries never mutate it.
    // Publication is atomic: validate and pack the spare state before exchanging the two banks.
    internal sealed partial class GridSpatialIndex
    {
        private readonly EntityStore store;
        internal EntityStore Store => store;
        private readonly ComponentType<Position> positions;
        private readonly ComponentType<Collider> colliders;
        private readonly long cellSize;
        private readonly bool readLiveLayers;
        private readonly int maxCellsPerBody;
        private readonly string entityError;
        private SpatialGridState active;
        private SpatialGridState spare;
        private int used;
        public int MembershipCount => used;
        internal int Capacity => active.Bodies.Length;
        internal int MembershipCapacity { get; }
        internal int CellCapacity => active.Unit.Table.Cells.Length;
        internal int LargeBodyCount => active.LargeCount;
        internal int OccupiedCells => active.Static.Table.Count + active.Unit.Table.Count;
        internal uint LayerUnion => active.LayerUnion;
        internal long AllocatedBytes => active.AllocatedBytes + spare.AllocatedBytes;
        internal long UsedBytes => active.UsedBytes + spare.UsedBytes;
        internal long ActiveUsedBytes => active.UsedBytes;
        public PostureGeometry Postures { get; }
        internal void ValidateRead() => store.ValidateSpatialWrites();

        internal GridSpatialIndex(EntityStore store, ComponentType<Position> positions, ComponentType<Collider> colliders,
            SimFix cellSize, int maxEntities = 4096, int maxMemberships = 32768, PostureGeometry postures = null,
            bool readLiveLayers = true, int maxCells = 0, int maxCellsPerBody = 64,
            int staticMaxMemberships = 0, int staticMaxCells = 0)
        {
            if (store == null)
                throw new ArgumentNullException(nameof(store));
            if (positions.Owner != store || colliders.Owner != store || (postures != null && postures.Entities != store))
                throw new ArgumentException("Handles must belong to the grid store.");
            if (cellSize.Raw <= 0 || maxEntities < 1 || maxMemberships < 1 || maxCellsPerBody < 1)
                throw new ArgumentOutOfRangeException(nameof(cellSize));
            if (maxMemberships > int.MaxValue / 5)
                throw new SimBoundaryException("Spatial membership capacity exceeds the supported range.");
            maxCells = ResolveCellCapacity(maxEntities, maxCells);
            this.store = store;
            this.positions = positions;
            this.colliders = colliders;
            this.cellSize = cellSize.Raw;
            this.readLiveLayers = readLiveLayers;
            this.maxCellsPerBody = maxCellsPerBody;
            Postures = postures;
            MembershipCapacity = maxMemberships;
            entityError = "Spatial entity capacity " + maxEntities + " exhausted.";
            if (staticMaxMemberships < 0 || staticMaxMemberships > int.MaxValue / 5)
                throw new SimBoundaryException("Static spatial membership capacity exceeds the supported range.");
            if (staticMaxMemberships == 0)
                staticMaxMemberships = maxMemberships < 16384 ? maxMemberships : 16384;
            staticMaxCells = staticMaxCells == 0 ? (maxCells < 16384 ? maxCells : 16384) : ResolveCellCapacity(maxEntities, staticMaxCells);
            active = new SpatialGridState(maxEntities, maxMemberships, maxCells, cellSize.Raw < SimFix.Scale, staticMaxMemberships, staticMaxCells, store.IdentityMode == IdentityMode.StableWorld);
            spare = new SpatialGridState(maxEntities, maxMemberships, maxCells, cellSize.Raw < SimFix.Scale, staticMaxMemberships, staticMaxCells, store.IdentityMode == IdentityMode.StableWorld);
        }

        internal static int ResolveCellCapacity(int entities, int cells)
        {
            if (cells != 0)
            {
                if (cells < 2 || cells > 1 << 26 || (cells & (cells - 1)) != 0)
                    throw new SimBoundaryException("Spatial cell capacity must be a power of two in [2, 67108864].");
                return cells;
            }
            // The default budgets directory slots, not worst-case volume. Content can reserve more.
            if (entities < 1 || entities > (1 << 26) / 16)
                throw new SimBoundaryException("Spatial entity capacity exceeds the default cell budget range.");
            int wanted = entities * 16;
            int result = 1024;
            while (result < wanted)
                result *= 2;
            return result;
        }

        internal static int Level(int index) => index == 4 ? 14 : index * 4;

        private bool ReadBody(EntityId entity, out SpatialBody body)
        {
            body = default;
            if (!store.TryGet(positions, entity, out Position position) || !store.TryGet(colliders, entity, out Collider collider))
                return false;
            if (entity.Id >= Capacity)
                throw new SimBoundaryException(entityError);
            body = Postures == null ? new SpatialBody(entity, position.Value, collider) : Postures.Body(entity, position.Value, collider);
            return true;
        }

        private static bool SameBody(in SpatialBody left, in SpatialBody right)
        {
            return left.Entity.Equals(right.Entity) && left.Position.Equals(right.Position)
                && left.Collider.ShapeKind == right.Collider.ShapeKind && left.Collider.Radius.Raw == right.Collider.Radius.Raw
                && left.Collider.Height.Raw == right.Collider.Height.Raw && left.Collider.Layer == right.Collider.Layer
                && left.Collider.IsStatic == right.Collider.IsStatic
                && left.Collider.HalfDepth.Raw == right.Collider.HalfDepth.Raw && left.Collider.Yaw.Equals(right.Collider.Yaw);
        }

        private bool Changed()
        {
            int count = 0;
            foreach (EntityId entity in store)
            {
                if (!ReadBody(entity, out SpatialBody body))
                    continue;
                count++;
                SpatialBody prior = active.Bodies[entity.Id];
                if (!active.Present[entity.Id] || !SameBody(in prior, in body))
                    return true;
            }
            return count != active.BodyCount;
        }

        public void Rebuild()
        {
            Array.Clear(spare.Present, 0, spare.Present.Length);
            foreach (EntityId entity in store)
                if (ReadBody(entity, out SpatialBody body))
                {
                    spare.Present[entity.Id] = true;
                    spare.Bodies[entity.Id] = body;
                }
            BuildAndPublish();
        }

        internal void SynchronizeBodies()
        {
            if (Changed())
                Rebuild();
        }

        public void Synchronize()
        {
            // Independent adapters keep explicit geometry Upsert; sync refreshes layers and deaths.
            bool changed = false;
            for (int id = 0; id < Capacity; id++)
                if (active.Present[id] && (!store.IsAlive(active.Bodies[id].Entity)
                    || Layer(active.Bodies[id]) != active.Bodies[id].Collider.Layer
                    || StaticFlag(active.Bodies[id]) != active.Bodies.Colliders[id].IsStatic))
                    changed = true;
            if (!changed)
                return;
            CopyPublished();
            for (int id = 0; id < Capacity; id++)
            {
                if (!spare.Present[id])
                    continue;
                SpatialBody body = spare.Bodies[id];
                if (!store.IsAlive(body.Entity))
                    spare.Present[id] = false;
                else
                    spare.Bodies[id] = new SpatialBody(body.Entity, body.Position, body.Collider.WithLayer(Layer(in body)).WithStatic(StaticFlag(in body)));
            }
            BuildAndPublish();
        }

        public void Upsert(EntityId entity)
        {
            if (entity.Id < 0 || entity.Id >= Capacity)
                throw new ArgumentOutOfRangeException(nameof(entity));
            if (!ReadBody(entity, out SpatialBody body))
            {
                if (active.Present[entity.Id] && (active.Bodies[entity.Id].Entity.Equals(entity) || !store.IsAlive(active.Bodies[entity.Id].Entity)))
                    Remove(active.Bodies[entity.Id].Entity);
                return;
            }
            SpatialBody prior = active.Bodies[entity.Id];
            if (active.Present[entity.Id] && SameBody(in prior, in body))
                return;
            CopyPublished();
            spare.Present[entity.Id] = true;
            spare.Bodies[entity.Id] = body;
            BuildAndPublish();
        }

        public bool Remove(EntityId entity)
        {
            if (entity.Id < 0 || entity.Id >= Capacity || !active.Present[entity.Id] || !active.Bodies[entity.Id].Entity.Equals(entity))
                return false;
            CopyPublished();
            spare.Present[entity.Id] = false;
            BuildAndPublish();
            return true;
        }

        private uint Layer(in SpatialBody body)
        {
            // The independent adapter preserves its immediate-layer API. Its cell masks cannot
            // prune until explicit synchronization; the Sim-owned path uses published colliders.
            return readLiveLayers && store.TryGet(colliders, body.Entity, out Collider current) ? current.Layer : body.Collider.Layer;
        }

        private bool StaticFlag(in SpatialBody body)
            => store.TryGet(colliders, body.Entity, out Collider current) ? current.IsStatic : body.Collider.IsStatic;

        private void CopyPublished()
        {
            active.Bodies.CopyTo(spare.Bodies);
            Array.Copy(active.Present, spare.Present, Capacity);
        }

        private void BuildAndPublish()
        {
            spare.Static.Reset();
            spare.Unit.Reset();
            for (int id = 0; id < Capacity; id++)
            {
                if (!spare.Present[id])
                    continue;
                SpatialBody body = spare.Bodies[id];
                SpatialPartitionState partition = spare.Partition(id);
                partition.BodyCount++;
                var bounds = new SpatialGridBounds(in body, cellSize);
                spare.Bounds[id] = bounds;
                int volume = bounds.Volume(maxCellsPerBody);
                if (volume < 0)
                {
                    partition.LargeIds[partition.LargeCount++] = id;
                    continue;
                }
                partition.Reserve(volume);
                VisitBounds(partition.Table, in bounds, id, false);
            }
            spare.Static.Table.Pack();
            spare.Unit.Table.Pack();
            for (int id = 0; id < Capacity; id++)
                if (spare.Present[id] && spare.Bounds[id].Volume(maxCellsPerBody) >= 0)
                    VisitBounds(spare.Partition(id).Table, in spare.Bounds[id], id, true);
            FlushPartition(spare.Static);
            FlushPartition(spare.Unit);
            int memberships = SimInteger.ToInt32((long)spare.LeafCount + spare.LargeCount);
            SpatialGridState old = active;
            active = spare;
            spare = old;
            used = memberships;
#if DEBUG
            ValidateStableQueryOrder();
#endif
        }

        private void FlushPartition(SpatialPartitionState partition)
        {
            // Repacked cells and partitions are dirty until this final exact OR pass.
            SpatialCellTable table = partition.Table;
            table.FlushMasks(spare.Bodies);
            uint mask = 0;
            for (int i = 0; i < table.Count; i++)
                mask |= table.Cells[table.Occupied[i]].Mask;
            for (int i = 0; i < partition.LargeCount; i++)
                mask |= spare.Bodies.Colliders[partition.LargeIds[i]].Layer;
            partition.LayerUnion = mask;
            table.Regions?.Build(table);
            // Pure bypass partitions benefit from stable top-K order. Mixed grids keep slot locality.
            // Any and Nearest always retain their original candidate stream.
            if (partition.OrderedLargeIds != null && partition.Table.Count == 0)
            {
                Array.Copy(partition.LargeIds,partition.OrderedLargeIds,partition.LargeCount);
                SortLarge(partition.OrderedLargeIds,partition.LargeCount);
            }
        }

        private static void VisitBounds(SpatialCellTable table, in SpatialGridBounds bounds, int id, bool fill)
        {
            for (int i = 0; i < 5; i++)
            {
                int level = Level(i);
                for (long x = SimInteger.ShiftRight(bounds.X0, level); x <= SimInteger.ShiftRight(bounds.X1, level); x++)
                    for (long y = SimInteger.ShiftRight(bounds.Y0, level); y <= SimInteger.ShiftRight(bounds.Y1, level); y++)
                        for (long z = SimInteger.ShiftRight(bounds.Z0, level); z <= SimInteger.ShiftRight(bounds.Z1, level); z++)
                        {
                            if (fill)
                                table.Fill(x, y, z, level, id);
                            else
                                table.CountMember(x, y, z, level);
                        }
            }
        }
    }
}
