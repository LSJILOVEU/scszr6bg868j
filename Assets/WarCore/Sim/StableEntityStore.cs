using System;

namespace WarCore.Sim
{
    public sealed partial class EntityStore
    {
        internal struct IdentityRecord
        {
            internal StableEntityKey Source;
            internal StableId Id;
            internal EntityRef Address;
            internal IdentityLife Life;
            internal ulong Chunk;
            internal long RetiredAt;
            internal ulong NextTask;
            internal ulong RetiredSpawnSequence;
        }
        private StableId[] stableIds;
        private int[] identitySlots;
        private int[] identityBuckets;
        private IdentityRecord[] identities;
        private int identityCount;
        private ulong restoreGeneration = 1;
        public IdentityMode IdentityMode { get; }
        public long RetiredRetentionTicks { get; }
        public const long DefaultRetiredRetentionTicks = long.MaxValue;
        public int RegisteredIdentityCount => identityCount;
        /// <summary>Instance high-water count of active, cold and retained retired identities, including restores.
        /// Diagnostic only: never reset by reclamation or restore, and not part of hashes or snapshots.</summary>
        public int PeakIdentityCount { get; private set; }
        internal const string IdentityCapacityPlanningRule = " Plan identityCapacity >= maximumLiveCount + maximumDestroyedPerTick * (retentionTicks + 1), where maximumLiveCount includes cold identities; entityCapacity >= maximumLiveCount + maximumCreatedPerTick.";
        private void ObserveIdentityCount()
        {
            if (identityCount > PeakIdentityCount)
                PeakIdentityCount = identityCount;
        }
        public int IdentityCapacity => identities == null ? 0 : identities.Length;

        internal static int DefaultIdentityCapacity(int capacity)
        {
            if (capacity < 1 || capacity > (1 << 25))
                throw new SimBoundaryException("Default identity capacity requires four times the entity capacity; configure a supported explicit capacity."+IdentityCapacityPlanningRule);
            return capacity * 4;
        }

        internal static void ValidateIdentityCapacity(int capacity, int identityCapacity)
        {
            if (capacity < 1 || identityCapacity < capacity || identityCapacity > (1 << 27))
                throw new SimBoundaryException("Identity capacity must cover all entity slots and fit the lookup table."+IdentityCapacityPlanningRule);
        }
        private void InitializeIdentity(int capacity, int identityCapacity)
        {
            if (IdentityMode == IdentityMode.StableWorld)
                ValidateIdentityCapacity(capacity,identityCapacity);
            stableIds = new StableId[capacity];
            if (IdentityMode == IdentityMode.Legacy)
                return;
            InitializeActiveSet(capacity);
            entityLeafHash = new StateHash();
            entityLeafWriter = new SimStateWriter(this,hash:entityLeafHash);
            identityOrder = new int[identityCapacity];
            identitySlots = new int[capacity];
            identities = new IdentityRecord[identityCapacity];
            int size = 2;
            while (size / 2 < identityCapacity)
                size *= 2;
            identityBuckets = new int[size];
        }
        private int IdentityBucket(StableId id) => (int)(StableEntityKey.Mix(id.Value) & (ulong)(identityBuckets.Length - 1));
        internal int FindIdentity(StableId id)
        {
            int bucket = IdentityBucket(id);
            while (identityBuckets[bucket] != 0)
            {
                int index = identityBuckets[bucket] - 1;
                if (identities[index].Id.Equals(id))
                    return index;
                bucket = (bucket + 1) & (identityBuckets.Length - 1);
            }
            return -1;
        }
        private void AddIdentityBucket(int index)
        {
            int bucket = IdentityBucket(identities[index].Id);
            while (identityBuckets[bucket] != 0)
                bucket = (bucket + 1) & (identityBuckets.Length - 1);
            identityBuckets[bucket] = index + 1;
        }
        internal void ValidateNewIdentity(in StableEntityKey key, StableId id)
        {
            if (IdentityMode != IdentityMode.StableWorld || key.Domain == 0)
                throw new SimBoundaryException("Explicit identities require StableWorld and a source key.");
            ValidatePlacedCreation(in key);
            int previous = FindIdentity(id);
            if (previous >= 0)
                throw new StableIdentityConflictException(id, identities[previous].Source, key);
            if (identityCount == identities.Length)
                throw new SimBoundaryException("Identity capacity exhausted by active, cold and retained retired identities; increase identityCapacity or shorten retention."+IdentityCapacityPlanningRule);
            if (freeCount == 0 && allocated == Capacity)
                throw new SimBoundaryException("Entity capacity exhausted; synchronize destroyed slots or increase entity capacity.");
        }
        public EntityRef Create(in StableEntityKey key) => CreateIdentity(in key, key.Id);
        // Only the protocol decoder and collision tests can supply a precomputed digest.
        internal EntityRef CreateIdentity(in StableEntityKey key, StableId id)
        {
            ValidateNewIdentity(in key, id);
            EntityId entity = AllocateEntity();
            stableIds[entity.Id] = id;
            identitySlots[entity.Id] = identityCount;
            identities[identityCount] = new IdentityRecord { Source = key, Id = id, Address = entity,
                Life = IdentityLife.Active, Chunk = key.Domain == 1 ? key.First : 0 };
            AddIdentityBucket(identityCount++);
            ObserveIdentityCount();
            IdentityChanged();
            SetSpawnSequence(entity,0);
            InitializePlacedCursors(in key,entity);
            QueueActive(entity);
            return entity;
        }
        internal void SetSpawnSequence(EntityId entity, ulong next)
        {
            RequireStableWorld();
            RequireAlive(entity);
            spawnSequences.Storage.Values[entity.Id] = new SpawnSequence(next);
            spawnSequences.Storage.Present[entity.Id] = true;
        }
        public StableId StableOf(EntityId entity)
        {
            RequireAlive(entity);
            return stableIds[entity.Id];
        }
        internal StableId StableAt(int slot) => stableIds[slot];
        public EntityLink Link(EntityId entity) => new EntityLink(StableOf(entity));
        public bool TryResolve(StableId id, out EntityRef entity)
        {
            RequireStableWorld();
            int index = FindIdentity(id);
            entity = index < 0 ? default : identities[index].Address;
            return index >= 0 && identities[index].Life == IdentityLife.Active && IsAlive(entity);
        }
        public EntityId Resolve(EntityLink link)
        {
            if (!link.HasValue || !TryResolve(link.Id, out EntityRef entity))
                throw new SimBoundaryException("A strong entity link does not resolve to an active entity.");
            return entity;
        }
        public IdentityLife LifeOf(StableId id)
        {
            RequireStableWorld();
            int index = FindIdentity(id);
            if (index < 0)
                throw new SimBoundaryException("Unknown stable identity.");
            return identities[index].Life;
        }
        public ulong ChunkOf(StableId id)
        {
            RequireStableWorld();
            int index = FindIdentity(id);
            if (index < 0)
                throw new SimBoundaryException("Unknown stable identity.");
            return identities[index].Chunk;
        }
        public void MoveToChunk(EntityId entity, ulong chunk)
        {
            RequireAlive(entity);
            RequireStableWorld();
            identities[identitySlots[entity.Id]].Chunk = chunk;
        }
        internal void RequireStableWorld()
        {
            if (IdentityMode != IdentityMode.StableWorld)
                throw new SimBoundaryException("This operation requires StableWorld.");
        }
        private void RetireIdentity(EntityId entity)
        {
            if (IdentityMode == IdentityMode.StableWorld)
            {
                ref IdentityRecord record = ref identities[identitySlots[entity.Id]];
                record.Life = IdentityLife.Retired;
                record.RetiredAt = IdentityTick;
                record.RetiredSpawnSequence = spawnSequences.Storage.Values[entity.Id].Next;
                IdentityChanged();
                Gameplay?.Retired(record.Id);
            }
        }
        internal long IdentityTick { get; set; }
        internal ulong IdentityRevision { get; private set; }
        private void IdentityChanged()
        {
            IdentityRevision = unchecked(IdentityRevision + 1);
            activeDirty = true;
        }
        public void MakeCold(EntityId entity)
        {
            RequireStableWorld();
            RequireAlive(entity);
            if (Gameplay != null && Gameplay.Contains(StableOf(entity)))
                throw new SimBoundaryException("M0C_GAMEPLAY_COLD_UNSUPPORTED");
            if (generations[entity.Id] == ulong.MaxValue)
                throw new SimBoundaryException("Entity generation exhausted.");
            alive[entity.Id] = false;
            generations[entity.Id]++;
            ref IdentityRecord record = ref identities[identitySlots[entity.Id]];
            record.Life = IdentityLife.Cold;
            IdentityChanged();
            record.Address = new EntityRef(entity.Id, generations[entity.Id]);
        }
        public EntityRef Reactivate(StableId id)
        {
            RequireStableWorld();
            int index = FindIdentity(id);
            if (index < 0 || identities[index].Life != IdentityLife.Cold)
                throw new SimBoundaryException("Only a cold identity can be reactivated.");
            ref IdentityRecord record = ref identities[index];
            record.Life = IdentityLife.Active;
            IdentityChanged();
            alive[record.Address.Slot] = true;
            QueueActive(record.Address);
            return record.Address;
        }
        internal void ActivateColdChunk(ulong chunk)
        {
            if (identityCount == 0)
                return;
            OrderedIdentity(0);
            bool changed = false;
            for (int i = 0; i < identityCount; i++)
            {
                ref IdentityRecord record = ref identities[identityOrder[i]];
                if (record.Life != IdentityLife.Cold || record.Chunk != chunk)
                    continue;
                record.Life = IdentityLife.Active;
                alive[record.Address.Slot] = true;
                QueueActive(record.Address);
                changed = true;
            }
            if (changed)
                IdentityChanged();
        }
        public bool RetiredMayArchive(StableId id, long tick)
        {
            RequireStableWorld();
            int index = FindIdentity(id);
            if (index < 0 || identities[index].Life != IdentityLife.Retired || tick < identities[index].RetiredAt)
                return false;
            return RetiredRetentionTicks != long.MaxValue && tick-identities[index].RetiredAt >= RetiredRetentionTicks;
        }
        internal void ValidateLink(EntityLink link, bool strong, bool allowEmpty = true)
        {
            RequireStableWorld();
            if (!link.HasValue)
            {
                if (!allowEmpty)
                    throw new SimBoundaryException("A required stable reference is empty.");
                return;
            }
            int index = FindIdentity(link.Id);
            if (index < 0 && UsesRetainedProtocol && !strong)
                return;
            if (index < 0 || (strong && identities[index].Life == IdentityLife.Retired))
                throw new SimBoundaryException("A stable reference does not resolve in this world.");
        }

        // Fixed array elements: 96-byte identity plus its 4-byte order index; two 16-byte views,
        // one 24-byte addition and one 4-byte addition index per slot. Object headers are excluded.
        public long IdentityPayloadBytes => IdentityMode == IdentityMode.Legacy ? 0 :
            (long)stableIds.Length * 8 + (long)identitySlots.Length * 4 + (long)identityBuckets.Length * 4
            + (long)identities.Length * 100 + (long)Capacity * 60;
    }
}
