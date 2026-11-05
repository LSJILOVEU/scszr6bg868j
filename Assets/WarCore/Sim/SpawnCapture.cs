namespace WarCore.Sim
{
    // Content owns the meaning of ownership and facing; the request owns their captured values.
    public readonly struct SpawnValues
    {
        public SimVec3 Position { get; }
        public SimVec3 Facing { get; }
        public ulong Ownership { get; }
        public SpawnValues(SimVec3 position, SimVec3 facing = default, ulong ownership = 0)
        {
            position.ValidateWorld();
            facing.ValidateWorld();
            Position = position;
            Facing = facing;
            Ownership = ownership;
        }
        internal void Save(SimStateWriter writer)
        {
            SnapshotValues.Vector(writer,Position);
            SnapshotValues.Vector(writer,Facing);
            writer.UInt64(Ownership);
        }
        internal static SpawnValues Read(SimStateReader reader)
            => new SpawnValues(SnapshotValues.Vector(reader),SnapshotValues.Vector(reader),reader.UInt64());
    }

    public readonly struct SpawnCapture : IStateHash
    {
        public EntityLink Spawner { get; }
        public SpawnValues Values { get; }
        public ulong Kind { get; }
        public ulong Chunk { get; }
        internal SpawnCapture(EntityLink spawner, SpawnValues values, ulong kind, ulong chunk)
        {
            Spawner = spawner;
            Values = values;
            Kind = kind;
            Chunk = chunk;
        }
        public void WriteState(StateHash hash)
        {
            hash.Add(Spawner.HasValue);
            hash.Add(Spawner.Id.Value);
            Values.Position.WriteState(hash);
            Values.Facing.WriteState(hash);
            hash.Add(Values.Ownership);
            hash.Add(Kind);
            hash.Add(Chunk);
        }
    }
    internal sealed class SpawnCaptureSerializer : IStableComponentSerializer<SpawnCapture>
    {
        public int TypeId => 1002;
        public int Version => 1;
        public void Write(SimStateWriter writer, in SpawnCapture value)
        {
            writer.Link(value.Spawner);
            value.Values.Save(writer);
            writer.UInt64(value.Kind);
            writer.UInt64(value.Chunk);
        }
        public SpawnCapture Read(SimStateReader reader)
            => new SpawnCapture(reader.Link(),SpawnValues.Read(reader),reader.UInt64(),reader.UInt64());
    }

    public sealed partial class EntityStore
    {
        internal ComponentType<SpawnCapture> spawnCaptures;
        internal SpawnValues CaptureSpawnValues(EntityId entity)
        {
            SimVec3 position = default;
            if (byType.TryGetValue(typeof(Position),out IComponents storage))
            {
                var positions = (Components<Position>)storage;
                if (positions.Present[entity.Id])
                    position = positions.Values[entity.Id].Value;
            }
            return new SpawnValues(position);
        }
        internal ulong CapturedSpawnCounter(EntityLink source, ulong fallback)
        {
            int index = FindIdentity(source.Id);
            if (index < 0)
                return fallback;
            IdentityRecord record = identities[index];
            return record.Life == IdentityLife.Retired ? record.RetiredSpawnSequence
                : spawnSequences.Storage.Values[record.Address.Slot].Next;
        }
        internal void ConsumeSpawnCounter(EntityLink source, ulong next)
        {
            int index = FindIdentity(source.Id);
            if (index < 0)
                return;
            if (identities[index].Life == IdentityLife.Retired)
                identities[index].RetiredSpawnSequence = next;
            else
                spawnSequences.Storage.Values[identities[index].Address.Slot] = new SpawnSequence(next);
        }
    }
}
