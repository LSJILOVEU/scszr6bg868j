using System;
using System.Collections.Generic;

namespace WarCore.Sim
{
    public readonly struct EntityId : IStateHash, IEquatable<EntityId>
    {
        public int Id
        {
            get;
        }

        public ulong Generation
        {
            get;
        }

        public EntityId(int id, ulong generation)
        {
            Id = id;
            Generation = generation;
        }

        public bool Equals(EntityId other) => Id == other.Id && Generation == other.Generation;
        public override bool Equals(object obj) => obj is EntityId other && Equals(other);
        public override int GetHashCode() => SimInteger.Int32Bits(unchecked((uint)Id * 397u) ^ Generation);
        public void WriteState(StateHash hash)
        {
            hash.Add(Id);
            hash.Add(Generation);
        }
    }

    internal interface IComponents : IStateHash
    {
        string Name
        {
            get;
        }

        bool HasStableComponent(int slot);
        void WriteStableComponent(SimStateWriter writer, int slot);
        void ReadStableComponent(SimStateReader reader, int slot);
        void Resize(int size);
        void Clear(int index);
        void ClearRetiredReferences(EntityStore store, EntityLink retired, int retiredSlot);
        void CaptureWriteFence();
        void ValidateWriteFence(bool[] alive, int allocated);
        int SerializerId { get; }
        int SerializerVersion { get; }
        void SaveComponents(SimStateWriter writer, int allocated);
        void LoadComponents(SimStateReader reader, bool[] alive, int allocated);
    }

    internal sealed partial class Components<T> : IComponents where T : unmanaged, IStateHash
    {
        public T[] Values;
        public bool[] Present;
        public string Name
        {
            get;
        }

        public Components(string name, int size)
        {
            Name = name;
            Values = new T[size];
            Present = new bool[size];
            borrowed = new bool[size];
        }

        public void Resize(int size)
        {
            Array.Resize(ref Values, size);
            Array.Resize(ref Present, size);
            Array.Resize(ref borrowed, size);
            ResizeWriteFence(size);
            ResizeReferenceSlots(size);
        }

        public void Clear(int index)
        {
            UnmarkReferenceValue(index);
            Values[index] = default;
            Present[index] = false;
        }

        public void WriteState(StateHash hash)
        {
            int count = 0;
            foreach (bool present in Present)
                if (present)
                    count++;
            hash.Add(count);
            for (int i = 0; i < Values.Length; i++)
            {
                if (!Present[i])
                    continue;
                hash.Add(i);
                Values[i].WriteState(hash);
            }
        }
    }

    public readonly struct ComponentType<T>
        where T : unmanaged, IStateHash
    {
        internal EntityStore Owner
        {
            get;
        }

        internal Components<T> Storage
        {
            get;
        }

        internal ComponentType(EntityStore owner, Components<T> storage)
        {
            Owner = owner;
            Storage = storage;
        }
    }

    public sealed partial class EntityStore : IStateHash
    {
        private bool[] alive;
        private ulong[] generations;
        private int[] free;
        private int freeCount;
        private int allocated;
        private bool frozen;
        private readonly Dictionary<Type, IComponents> byType = new Dictionary<Type, IComponents>();
        private readonly List<IComponents> storages = new List<IComponents>();
        public int Capacity => alive.Length;
        public bool UsesRetainedProtocol { get; }
        internal EntityStore(int capacity = 4, IdentityMode identityMode = IdentityMode.Legacy, int identityCapacity = 0,
            long retiredRetentionTicks = DefaultRetiredRetentionTicks, bool retainedProtocol = false)
        {
            if (capacity < 1)
                throw new ArgumentOutOfRangeException(nameof(capacity));
            if (identityMode < IdentityMode.Legacy || identityMode > IdentityMode.StableWorld || retiredRetentionTicks < 0)
                throw new SimBoundaryException("Invalid identity configuration.");
            if (retainedProtocol && (identityMode != IdentityMode.StableWorld || retiredRetentionTicks == long.MaxValue))
                throw new SimBoundaryException("Retained protocol requires StableWorld with finite retention.");
            IdentityMode = identityMode;
            UsesRetainedProtocol = retainedProtocol;
            RetiredRetentionTicks = retiredRetentionTicks;
            InitializeIdentity(capacity, identityCapacity == 0 && identityMode == IdentityMode.StableWorld
                ? DefaultIdentityCapacity(capacity) : identityCapacity);
            alive = new bool[capacity];
            generations = new ulong[capacity];
            free = new int[capacity];
            pending = new bool[capacity];
            if (identityMode == IdentityMode.StableWorld)
                spawnSequences = Register<SpawnSequence>("warcore.spawn-sequence",new SpawnSequenceSerializer());
            if (retainedProtocol)
                spawnCaptures = Register<SpawnCapture>("warcore.spawn-capture",new SpawnCaptureSerializer());
        }

        public ComponentType<T> Register<T>(string stableId, IComponentSerializer<T> serializer = null)
            where T : unmanaged, IStateHash
        {
            if (frozen)
                throw new InvalidOperationException("Registration is closed after warmup.");
            if (string.IsNullOrEmpty(stableId))
                throw new ArgumentException("A component id is required.", nameof(stableId));
            if (byType.ContainsKey(typeof(T)))
                throw new InvalidOperationException("Component type is already registered.");
            int index = 0;
            while (index < storages.Count && StringComparer.Ordinal.Compare(storages[index].Name, stableId) < 0)
                index++;
            if (index < storages.Count && storages[index].Name == stableId)
                throw new InvalidOperationException("Component id is already registered.");
            if (serializer != null)
                ValidateSerializer(serializer.TypeId, serializer.Version);
            var storage = new Components<T>(stableId, Capacity);
            storage.Serializer = serializer;
            storage.InitializeReferenceSlots();
            byType.Add(typeof(T), storage);
            storages.Insert(index, storage);
            return new ComponentType<T>(this, storage);
        }

        // Resolve once during assembly; systems retain the handle to avoid type lookups per entity.
        public ComponentType<T> Resolve<T>()
            where T : unmanaged, IStateHash
        {
            if (!byType.TryGetValue(typeof(T), out IComponents storage))
                throw new InvalidOperationException("Component is not registered.");
            return new ComponentType<T>(this, (Components<T>)storage);
        }

        public void Reserve(int capacity)
        {
            if (capacity <= Capacity)
                return;
            if (frozen)
                throw new InvalidOperationException("Entity capacity exhausted after warmup.");
            if (IdentityMode == IdentityMode.StableWorld)
                throw new SimBoundaryException("StableWorld capacity must be reserved at construction.");
            Array.Resize(ref stableIds, capacity);
            Array.Resize(ref alive, capacity);
            Array.Resize(ref generations, capacity);
            Array.Resize(ref free, capacity);
            Array.Resize(ref pending, capacity);
            foreach (IComponents storage in storages)
                storage.Resize(capacity);
        }

        public void CompleteWarmup()
        {
            frozen = true;
        }

        public EntityId Create()
        {
            if (IdentityMode != IdentityMode.Legacy)
                throw new SimBoundaryException("StableWorld forbids default entity keys.");
            EntityId entity = AllocateEntity();
            stableIds[entity.Id] = new StableId((ulong)entity.Id);
            return entity;
        }

        private EntityId AllocateEntity()
        {
            int id;
            if (freeCount != 0)
            {
                id = free[0];
                int last = free[--freeCount];
                int index = 0;
                while (index < freeCount / 2)
                {
                    int child = index * 2 + 1;
                    if (child + 1 < freeCount && free[child + 1] < free[child])
                        child++;
                    if (last <= free[child])
                        break;
                    free[index] = free[child];
                    index = child;
                }

                free[index] = last;
            }
            else
            {
                if (allocated == Capacity)
                    Reserve(GrownCapacity(Capacity));
                id = allocated++;
                if (generations[id] == 0)
                    generations[id] = restoreGeneration;
            }

            alive[id] = true;
            return new EntityId(id, generations[id]);
        }

        public bool IsAlive(EntityId entity) => entity.Id >= 0 && entity.Id < allocated && alive[entity.Id] && generations[entity.Id] == entity.Generation;
        private void RequireAlive(EntityId entity)
        {
            if (!IsAlive(entity))
                throw new InvalidOperationException("Entity is stale or does not exist.");
        }

        private Components<T> Require<T>(ComponentType<T> type)
            where T : unmanaged, IStateHash
        {
            if (type.Owner != this)
                throw new InvalidOperationException("Component handle belongs to another store.");
            return type.Storage;
        }

        public void Destroy(EntityId entity)
        {
            RequireAlive(entity);
            if (generations[entity.Id] == ulong.MaxValue)
                throw new SimBoundaryException("Entity generation exhausted.");
            MarkPlacedDestroyed(entity);
            RetireIdentity(entity);
            ClearRetiredReferences(entity);
            alive[entity.Id] = false;
            generations[entity.Id]++;
            pending[entity.Id] = true;
            pendingCount++;
            foreach (IComponents storage in storages)
                storage.Clear(entity.Id);
        }

        public void Set<T>(ComponentType<T> type, EntityId entity, T value)
            where T : unmanaged, IStateHash
        {
            RequireAlive(entity);
            Components<T> storage = Require(type);
            RequireWritable(storage);
            if (IdentityMode == IdentityMode.StableWorld && storage.Serializer is IStableReferenceValidator<T> validator)
                validator.ValidateReferences(this,in value);
            storage.Values[entity.Id] = value;
            storage.Present[entity.Id] = true;
            storage.MarkReferenceValue(entity.Id);
        }

        public ref T Get<T>(ComponentType<T> type, EntityId entity)
            where T : unmanaged, IStateHash
        {
            RequireAlive(entity);
            Components<T> storage = Require(type);
            if (!storage.Present[entity.Id])
                throw new InvalidOperationException("Entity has no such component.");
            RequireWritable(storage);
            storage.MarkBorrowed(entity.Id);
            return ref storage.Values[entity.Id];
        }

        public bool TryGet<T>(ComponentType<T> type, EntityId entity, out T value)
            where T : unmanaged, IStateHash
        {
            Components<T> storage = Require(type);
            if (IsAlive(entity) && storage.Present[entity.Id])
            {
                value = storage.Values[entity.Id];
                return true;
            }

            value = default;
            return false;
        }

        public void Remove<T>(ComponentType<T> type, EntityId entity)
            where T : unmanaged, IStateHash
        {
            RequireAlive(entity);
            Components<T> storage = Require(type);
            RequireWritable(storage);
            storage.Clear(entity.Id);
        }

        // Copy APIs remain useful for setup and external readers; ref lifetimes end at resize/removal.
        public void Set<T>(EntityId entity, T value)
            where T : unmanaged, IStateHash => Set(Resolve<T>(), entity, value);
        public T Get<T>(EntityId entity)
            where T : unmanaged, IStateHash => Read(Resolve<T>(), entity);
        public bool TryGet<T>(EntityId entity, out T value)
            where T : unmanaged, IStateHash => TryGet(Resolve<T>(), entity, out value);
        public void Remove<T>(EntityId entity)
            where T : unmanaged, IStateHash => Remove(Resolve<T>(), entity);
        public EntityEnumerator GetEnumerator() => new EntityEnumerator(this);
        public struct EntityEnumerator
        {
            private readonly EntityStore owner;
            private int index;
            internal EntityEnumerator(EntityStore owner)
            {
                this.owner = owner;
                index = -1;
            }

            public EntityId Current => new EntityId(index, owner.generations[index]);
            public bool MoveNext()
            {
                // A finished value enumerator can be polled again; do not advance its terminal cursor.
                if (index >= owner.allocated - 1)
                    return false;
                while (++index < owner.allocated)
                    if (owner.alive[index])
                        return true;
                return false;
            }
        }

        // This allocating copy is for tests and initialization, never for simulation tick code.
        public IReadOnlyList<EntityId> SnapshotEntitiesForTests()
        {
            var result = new List<EntityId>();
            foreach (EntityId entity in this)
                result.Add(entity);
            return result.AsReadOnly();
        }

        public void WriteState(StateHash hash)
        {
            hash.Add(allocated);
            for (int i = 0; i < allocated; i++)
            {
                hash.Add(alive[i]);
                hash.Add(generations[i]);
            }

            hash.Add(storages.Count);
            foreach (IComponents storage in storages)
            {
                hash.Add(storage.Name);
                storage.WriteState(hash);
            }
        }
    }
}
