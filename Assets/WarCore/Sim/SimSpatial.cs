using System;

namespace WarCore.Sim
{
    public enum SimSystemPhase
    {
        Automatic = -1,
        Requests,
        Movement,
        Attachment,
        Queries
    }

    public interface ISimPhaseSystem
    {
        SimSystemPhase Phase { get; }
    }

    public sealed partial class Sim
    {
        private GridSpatialIndex spatialIndex;
        private SpatialQueryView spatialQuery;
        private ComponentType<Position> spatialPositions;
        private SimVec3[] movementOutput;
        private bool[] movementWritten;
        private bool advancingMovement;
        internal GridSpatialIndex SpatialIndex => spatialIndex;
        public ISpatialQueryModes SpatialQuery => spatialQuery;

        // Assembly is explicit because component handles and posture data belong to content.
        // Ownership, synchronization and restoration remain engine responsibilities.
        public ISpatialQueryModes ConfigureSpatial(ComponentType<Position> positions, ComponentType<Collider> colliders,
            SimFix cellSize, int maxMemberships = 0, PostureGeometry postures = null)
        {
            if (started || warmed || spatialIndex != null)
                throw new InvalidOperationException("Configure the simulation spatial index once before warmup.");
            spatialIndex = new GridSpatialIndex(Entities, positions, colliders, cellSize, Entities.Capacity,
                maxMemberships == 0 ? Config.SpatialMembershipCapacity : maxMemberships, postures, readLiveLayers: false,
                maxCells: Config.SpatialCellCapacity, maxCellsPerBody: Config.SpatialMaxCellsPerBody,
                staticMaxMemberships: Config.StaticSpatialMembershipCapacity, staticMaxCells: Config.StaticSpatialCellCapacity);
            spatialQuery = new SpatialQueryView(spatialIndex, new GridQueryContext(Entities.Capacity));
            spatialPositions = positions;
            spatialColliders = colliders;
            movementOutput = new SimVec3[Entities.Capacity];
            movementWritten = new bool[Entities.Capacity];
            Entities.ProtectWrites(positions);
            Entities.ProtectWrites(colliders);
            postures?.ProtectWrites();
            ConfigureSpatialMotion();
            Context.Owner = this;
            Context.SpatialQuery = spatialQuery;
            return spatialQuery;
        }

        public ISpatialQueryModes BindSpatialQuery(GridQueryContext context)
        {
            if (spatialQuery == null)
                throw new InvalidOperationException("Configure the simulation spatial index before binding queries.");
            return spatialQuery.Bind(context);
        }

        // Setup edits and out-of-tick deaths use the same boundary as snapshot saving.
        public void CompleteSpatialBoundary()
        {
            if (stepping || faulted)
                throw new InvalidOperationException("Spatial boundaries require a completed simulation tick.");
            RequireSpatialCapacity();
            gameplay?.Cleanup();
            spatialIndex?.SynchronizeBodies();
            Entities.RecycleDestroyed();
            Entities.SynchronizeStableEntities();
            gameplay?.ValidateBindings();
            Entities.RecycleRetiredIdentities();
        }

        private void RequireSpatialCapacity()
        {
            if (spatialIndex != null && Entities.Capacity > spatialIndex.Capacity)
                throw new SimBoundaryException("Reserve entity capacity before configuring the spatial index.");
        }

        private void RunPhase(SimSystemPhase phase)
        {
            foreach (Registration entry in systems)
            {
                if (entry.Phase != phase)
                    continue;
                entry.System.Step(Context);
                Entities.ValidateSpatialWrites();
            }
        }

        private void BeginMovement()
        {
            if (spatialIndex == null)
                return;
            Array.Clear(movementWritten, 0, movementWritten.Length);
            // All movers read one authority snapshot and the previous published index.
            // Their output is write-only until every movement mode has completed.
            Entities.SealSpatialWrites();
            advancingMovement = true;
        }

        internal bool BufferMovement(EntityId entity, SimVec3 position)
        {
            if (!advancingMovement)
                return false;
            if (!Entities.IsAlive(entity) || entity.Id >= movementWritten.Length)
                throw new SimBoundaryException("Movement output requires a live entity within reserved capacity.");
            position.ValidateWorld();
            if (movementWritten[entity.Id])
                throw new SimBoundaryException("Each entity may have only one movement result per tick.");
            movementOutput[entity.Id] = position;
            movementWritten[entity.Id] = true;
            return true;
        }

        private void CommitMovement()
        {
            if (!advancingMovement)
                return;
            Entities.ValidateSpatialWrites();
            advancingMovement = false;
            Entities.OpenSpatialWrites();
            foreach (EntityId entity in Entities)
                if (movementWritten[entity.Id])
                    Entities.Set(spatialPositions, entity, new Position(movementOutput[entity.Id]));
        }

        private void PublishSpatialState()
        {
            RequireSpatialCapacity();
            SettleStaticRequests();
            spatialIndex?.SynchronizeBodies();
            Entities.RecycleDestroyed();
            Entities.SynchronizeStableEntities();
            Entities.SealSpatialWrites();
        }

        private void RebuildSpatialState()
        {
            // All serializers and the final reader validation finish before this call.
            // No system registration or posture callback controls derived-state lifetime.
            RequireSpatialCapacity();
            spatialIndex?.Rebuild();
            advancingMovement = false;
            Entities.OpenSpatialWrites();
        }
    }
}
