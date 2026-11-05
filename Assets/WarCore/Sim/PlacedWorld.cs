namespace WarCore.Sim
{
    public interface IRetainedGameplaySystem : ISimSystem, IStableStateSerializer { }
    public interface IRetainedGameplayMode : IMatchMode, IStableStateSerializer { }

    public sealed partial class Sim
    {
        public const int PlacedHashVersion = 5;
        public const int PlacedSnapshotVersion = 7;
        internal const string RetainedRequired = "M0C_RETAINED_REQUIRED: New gameplay requires CreateRetainedWorld with a placed catalog.";
        private bool placedRestorePhase;
        public PlacedDestructionState Placed => Entities.Placed;
        public static Sim CreateRetainedWorld(ulong seed, PlacedCatalog catalog, SimConfig config = null,
            RetainedWorldOptions options = null, StableContentManifest content = null,
            InitialPlacedState initial = null, IRetainedGameplayMode mode = null, string modeId = "single-match-v1", GameplaySchema gameplaySchema = null)
        {
            if (catalog == null)
                throw new SimBoundaryException("M0C_PLACED_CATALOG: A catalog is required.");
            content = content ?? new StableContentManifest(0);
            if (catalog.WorldId != content.WorldId || catalog.ContentDigest != content.Digest)
                throw new SimBoundaryException("M0C_PLACED_CATALOG: Content mismatch.");
            var result = new Sim(seed,mode:mode,modeId:modeId,config:config,identityMode:IdentityMode.StableWorld,
                stableContent:content,retainedWorld:options ?? new RetainedWorldOptions(),placedCatalog:catalog,initialPlaced:initial);
            if (gameplaySchema != null)
                result.gameplay = new GameplayState(gameplaySchema,result);
            return result;
        }
        private void RequireGameplayProtocol()
        {
            if (!Entities.UsesRetainedProtocol || Placed == null)
                throw new SimBoundaryException(RetainedRequired);
        }
        public void RegisterGameplay(string stableId, IRetainedGameplaySystem system, SimSystemPhase phase = SimSystemPhase.Automatic)
        {
            RequireGameplayProtocol();
            Register(stableId,system,phase);
        }
        internal void RestorePlacedCore(StableEntityKey key)
        {
            RequireGameplayProtocol();
            if (!placedRestorePhase)
                throw new SimBoundaryException("M0C_RESTORE_PHASE: RestorePlaced requires the requests phase.");
            int index = Placed.Catalog.Index(key);
            if (Entities.FindIdentity(key.Id) >= 0)
                throw new SimBoundaryException("M0C_RESTORE_TOO_EARLY: Identity record has not been reclaimed.");
            Placed.Restore(index);
        }
    }
    public sealed partial class SimContext
    {
        public void RestorePlaced(StableEntityKey key) => Owner.RestorePlacedCore(key);
    }
    public sealed partial class EntityStore
    {
        public PlacedDestructionState Placed { get; internal set; }
        private void ValidatePlacedCreation(in StableEntityKey key)
        {
            if (Placed != null && key.Domain == 1 && Placed.IsDestroyed(key))
                throw new SimBoundaryException("M0C_PLACED_DESTROYED: Cannot recreate destroyed placement.");
        }
        private void InitializePlacedCursors(in StableEntityKey key, EntityId entity)
        {
            if (Placed == null || key.Domain != 1)
                return;
            int index = Placed.Catalog.Index(key);
            if (Placed.Catalog.Entry(index).Policy != 1)
                return;
            ulong start = Placed.CursorStart(index);
            SetSpawnSequence(entity,start);
            identities[identitySlots[entity.Id]].NextTask = start;
        }
        private void MarkPlacedDestroyed(EntityId entity)
        {
            if (Placed == null)
                return;
            StableEntityKey key = identities[identitySlots[entity.Id]].Source;
            if (key.Domain == 1)
                Placed.MarkDestroyed(Placed.Catalog.Index(key));
        }
        internal void RequirePlacedCursor(StableId id, ulong value, bool task)
        {
            if (Placed == null)
                return;
            int index = Placed.Catalog.Find(id);
            if (index < 0 || Placed.Catalog.Entry(index).Policy != 1)
                return;
            Placed.RequireCursor(index,value,task ? "Stable task tick or counter exhausted." : "Spawn counter exhausted.");
            if (task && value < TaskCounter(id,false))
                throw new SimBoundaryException("Stable task tick or counter exhausted.");
        }
        internal void ValidatePlacedRestore()
        {
            if (Placed == null)
                return;
            for (int i = 0; i < identityCount; i++)
            {
                IdentityRecord record = identities[i];
                if (record.Source.Domain != 1)
                    continue;
                int index = Placed.Catalog.Find(record.Id);
                SimStateReader.Require(index >= 0 && Placed.Catalog.Entry(index).Key.Equals(record.Source),"M0C_PLACED_UNKNOWN: Snapshot key absent from catalog.");
                SimStateReader.Require(Placed.IsDestroyedIndex(index) == (record.Life == IdentityLife.Retired),"M0C_PLACED_LIFE: Snapshot bit disagrees with identity life.");
                if (Placed.Catalog.Entry(index).Policy != 1)
                    continue;
                ulong spawn = record.Life == IdentityLife.Retired ? record.RetiredSpawnSequence
                    : spawnSequences.Storage.Values[record.Address.Slot].Next;
                SimStateReader.Require(Placed.CursorValid(index,spawn) && Placed.CursorValid(index,record.NextTask),
                    "M0C_PLACED_CURSOR: Snapshot cursor belongs to another generation.");
            }
        }
    }
}
