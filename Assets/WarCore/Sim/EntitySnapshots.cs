using System;

namespace WarCore.Sim
{
    internal sealed partial class Components<T>
    {
        internal IComponentSerializer<T> Serializer;
        public int SerializerId => Serializer == null ? 0 : Serializer.TypeId;
        public int SerializerVersion => Serializer == null ? 0 : Serializer.Version;

        public void SaveComponents(SimStateWriter writer, int allocated)
        {
            for (int i = 0; i < allocated; i++)
            {
                writer.Bool(Present[i]);
                if (Present[i])
                    Serializer.Write(writer, in Values[i]);
            }
        }

        public void LoadComponents(SimStateReader reader, bool[] alive, int allocated)
        {
            ResetReferenceSlots();
            Array.Clear(Values, 0, Values.Length);
            Array.Clear(Present, 0, Present.Length);
            for (int i = 0; i < allocated; i++)
            {
                Present[i] = reader.Bool();
                SimStateReader.Require(!Present[i] || alive[i], "A dead entity cannot own a component.");
                if (Present[i])
                {
                    Values[i] = Serializer.Read(reader);
                    MarkReferenceValue(i);
                }
            }
        }
    }

    public sealed partial class EntityStore
    {
        private void ValidateSerializer(int id, int version)
        {
            if (id <= 0 || version <= 0)
                throw new ArgumentOutOfRangeException(nameof(id), "Component serializer ids and versions must be positive.");
            foreach (IComponents storage in storages)
                if (storage.SerializerId == id)
                    throw new InvalidOperationException("Duplicate component serializer id: " + id);
        }

        public void RegisterSerializer<T>(ComponentType<T> type, IComponentSerializer<T> serializer)
            where T : unmanaged, IStateHash
        {
            if (frozen)
                throw new InvalidOperationException("Serializer registration is closed after warmup.");
            if (serializer == null)
                throw new ArgumentNullException(nameof(serializer));
            Components<T> storage = Require(type);
            if (storage.Serializer != null)
                throw new InvalidOperationException("Component serializer is already registered: " + storage.Name);
            ValidateSerializer(serializer.TypeId, serializer.Version);
            storage.Serializer = serializer;
            storage.InitializeReferenceSlots();
        }

        internal void SaveManifest(SimStateWriter writer)
        {
            writer.Int32(storages.Count);
            foreach (IComponents storage in storages)
            {
                if (storage.SerializerId <= 0 || storage.SerializerVersion <= 0)
                    throw new SimSnapshotException("Missing component serializer: " + storage.Name);
                writer.String(storage.Name);
                writer.Int32(storage.SerializerId);
                writer.Int32(storage.SerializerVersion);
            }
        }

        internal void CheckManifest(SimStateReader reader)
        {
            SimStateReader.Require(reader.Int32() == storages.Count, "Component registration manifest count mismatch.");
            foreach (IComponents storage in storages)
            {
                string name = reader.String();
                int id = reader.Int32();
                int version = reader.Int32();
                SimStateReader.Require(name == storage.Name && id > 0 && id == storage.SerializerId
                    && version > 0 && version == storage.SerializerVersion,
                    "Component registration manifest mismatch: snapshot=" + name + " id=" + id + " v" + version
                    + "; registered=" + storage.Name + " id=" + storage.SerializerId + " v" + storage.SerializerVersion);
            }
        }

        internal void SaveEntities(SimStateWriter writer)
        {
            if (pendingCount != 0)
                throw new SimSnapshotException("Recycle destroyed entities at the simulation boundary before saving.");
            writer.Int32(Capacity);
            writer.Bool(frozen);
            writer.Int32(allocated);
            for (int i = 0; i < allocated; i++)
            {
                writer.Bool(alive[i]);
                writer.UInt64(generations[i]);
            }

            writer.Int32(freeCount);
            for (int i = 0; i < freeCount; i++)
                writer.Int32(free[i]);
            foreach (IComponents storage in storages)
            {
                var section = new SimStateWriter();
                storage.SaveComponents(section, allocated);
                writer.Bytes(section.ToArray());
            }
        }

        internal void LoadEntities(SimStateReader reader)
        {
            int capacity = reader.Int32();
            SimStateReader.Require(capacity > 0, "Invalid entity capacity.");
            bool savedFrozen = reader.Bool();
            int savedAllocated = reader.Count(capacity);
            SimStateReader.Require(savedAllocated <= reader.Remaining / 9, "Truncated entity slots.");
            var savedAlive = new bool[capacity];
            var savedGenerations = new ulong[capacity];
            var savedFree = new int[capacity];
            int dead = 0;
            for (int i = 0; i < savedAllocated; i++)
            {
                savedAlive[i] = reader.Bool();
                savedGenerations[i] = reader.UInt64();
                SimStateReader.Require(savedGenerations[i] != 0, "Invalid entity generation.");
                if (!savedAlive[i])
                    dead++;
            }

            int savedFreeCount = reader.Count(savedAllocated);
            SimStateReader.Require(savedFreeCount == dead, "Entity free-slot count mismatch.");
            var seen = new bool[savedAllocated];
            for (int i = 0; i < savedFreeCount; i++)
            {
                int id = reader.Int32();
                SimStateReader.Require(id >= 0 && id < savedAllocated && !savedAlive[id] && !seen[id], "Invalid free entity slot.");
                SimStateReader.Require(i == 0 || savedFree[(i - 1) / 2] <= id, "Invalid free entity heap.");
                savedFree[i] = id;
                seen[id] = true;
            }

            pending = new bool[capacity];
            pendingCount = 0;
            spatialWritesLocked = false;
            stableIds = new StableId[capacity];
            for (int i = 0; i < capacity; i++)
                stableIds[i] = new StableId((ulong)i);
            alive = savedAlive;
            generations = savedGenerations;
            free = savedFree;
            allocated = savedAllocated;
            freeCount = savedFreeCount;
            frozen = savedFrozen;
            foreach (IComponents storage in storages)
            {
                storage.Resize(capacity);
                var section = new SimStateReader(reader.Bytes());
                storage.LoadComponents(section, alive, allocated);
                section.End();
            }
        }
    }
}
