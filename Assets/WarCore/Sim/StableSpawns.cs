using System;

namespace WarCore.Sim
{
    public readonly struct StableRequestKey : IComparable<StableRequestKey>, IEquatable<StableRequestKey>
    {
        public ulong System { get; }
        public ulong OriginDomain { get; }
        public StableId Origin { get; }
        public ulong Callsite { get; }
        public ulong Ordinal { get; }
        public ulong Kind { get; }
        public StableRequestKey(ulong system, ulong originDomain, StableId origin, ulong callsite, ulong ordinal, ulong kind)
        {
            if (originDomain > 1)
                throw new SimBoundaryException("Invalid stable request origin domain.");
            System = system;
            OriginDomain = originDomain;
            Origin = origin;
            Callsite = callsite;
            Ordinal = ordinal;
            Kind = kind;
        }
        public int CompareTo(StableRequestKey other)
        {
            int c = System.CompareTo(other.System);
            if (c != 0)
                return c;
            c = OriginDomain.CompareTo(other.OriginDomain);
            if (c != 0)
                return c;
            c = Origin.CompareTo(other.Origin);
            if (c != 0)
                return c;
            c = Callsite.CompareTo(other.Callsite);
            if (c != 0)
                return c;
            c = Ordinal.CompareTo(other.Ordinal);
            return c != 0 ? c : Kind.CompareTo(other.Kind);
        }
        public bool Equals(StableRequestKey other) => CompareTo(other) == 0;
        public override bool Equals(object other) => other is StableRequestKey value && Equals(value);
        public override int GetHashCode()
        {
            ulong value = 14695981039346656037UL;
            StableEntityKey.Word(ref value,System);
            StableEntityKey.Word(ref value,OriginDomain);
            StableEntityKey.Word(ref value,Origin.Value);
            StableEntityKey.Word(ref value,Callsite);
            StableEntityKey.Word(ref value,Ordinal);
            StableEntityKey.Word(ref value,Kind);
            return SimInteger.Int32Bits(StableEntityKey.Mix(value));
        }
        internal void Save(SimStateWriter writer)
        {
            writer.UInt64(System);
            writer.UInt64(OriginDomain);
            writer.UInt64(Origin.Value);
            writer.UInt64(Callsite);
            writer.UInt64(Ordinal);
            writer.UInt64(Kind);
        }
        internal static StableRequestKey Read(SimStateReader reader) => new StableRequestKey(reader.UInt64(),reader.UInt64(),
            new StableId(reader.UInt64()),reader.UInt64(),reader.UInt64(),reader.UInt64());
    }

    public readonly struct SpawnSequence : IStateHash
    {
        public ulong Next { get; }
        internal SpawnSequence(ulong next) => Next = next;
        public void WriteState(StateHash hash) => hash.Add(Next);
    }
    internal sealed class SpawnSequenceSerializer : IStableComponentSerializer<SpawnSequence>
    {
        public int TypeId => 1001;
        public int Version => 1;
        public void Write(SimStateWriter writer, in SpawnSequence value) => writer.UInt64(value.Next);
        public SpawnSequence Read(SimStateReader reader) => new SpawnSequence(reader.UInt64());
    }

    public sealed class StableSpawnService
    {
        private struct Request : IComparable<Request>
        {
            internal StableRequestKey Key;
            internal EntityLink Spawner;
            internal long Tick;
            internal ulong Counter;
            internal ulong Chunk;
            internal SpawnValues Values;
            public int CompareTo(Request other) => Key.CompareTo(other.Key);
        }
        private struct WorldCursor : IComparable<WorldCursor>
        {
            internal ulong System;
            internal long Tick;
            internal ulong Next;
            public int CompareTo(WorldCursor other) => System.CompareTo(other.System);
        }
        private readonly struct Origin : IEquatable<Origin>
        {
            internal readonly bool Entity;
            internal readonly ulong Id;
            internal Origin(Request request)
            {
                Entity = request.Spawner.HasValue;
                Id = Entity ? request.Spawner.Id.Value : request.Key.System;
            }
            public bool Equals(Origin other) => Entity == other.Entity && Id == other.Id;
            public override int GetHashCode() => SimInteger.Int32Bits(StableEntityKey.Mix(Id) ^ (Entity ? 1UL : 0UL));
        }
        private struct PlanCursor
        {
            internal ulong Next;
            internal long Tick;
        }
        private readonly EntityStore entities;
        private readonly Request[] requests;
        private readonly StableEntityKey[] planned;
        private readonly WorldCursor[] worlds;
        private readonly PlanCursor[] planCursors;
        private readonly BoundedLookup<StableRequestKey> requestKeys;
        private readonly BoundedLookup<StableId> plannedIds;
        private readonly BoundedLookup<Origin> planOrigins;
        private readonly BoundedLookup<ulong> worldLookup;
        private int count;
        private int worldCount;
        internal StableSpawnService(EntityStore entities, int capacity, int worldCapacity)
        {
            this.entities = entities;
            requests = new Request[capacity];
            planned = new StableEntityKey[capacity];
            planCursors = new PlanCursor[capacity];
            worlds = new WorldCursor[worldCapacity];
            requestKeys = new BoundedLookup<StableRequestKey>(capacity);
            plannedIds = new BoundedLookup<StableId>(capacity);
            planOrigins = new BoundedLookup<Origin>(capacity);
            worldLookup = new BoundedLookup<ulong>(worldCapacity);
        }
        public int PendingCount => count;
        public ulong Counter(EntityLink spawner) => entities.Read(entities.spawnSequences,entities.Resolve(spawner)).Next;
        public void Enqueue(EntityLink spawner, StableRequestKey key)
        {
            EntityId source = entities.Resolve(spawner);
            Enqueue(spawner,key,entities.CaptureSpawnValues(source));
        }
        public void Enqueue(EntityLink spawner, StableRequestKey key, SpawnValues values)
        {
            EntityId source = entities.Resolve(spawner);
            Add(new Request { Key=key, Spawner=spawner, Tick=entities.IdentityTick,
                Counter=entities.Read(entities.spawnSequences,source).Next, Chunk=entities.ChunkOf(spawner.Id), Values=values });
        }
        public void EnqueueWorld(long tick, StableRequestKey key) => EnqueueWorld(tick,key,default);
        public void EnqueueWorld(long tick, StableRequestKey key, SpawnValues values)
        {
            if (tick < 0)
                throw new SimBoundaryException("World spawn tick must be nonnegative.");
            Add(new Request { Key=key, Tick=tick, Values=values });
        }
        private void Add(Request request)
        {
            if (count == requests.Length)
                throw new SimBoundaryException("Spawn request capacity exhausted.");
            if (requestKeys.Find(request.Key) >= 0)
                throw new SimBoundaryException("Duplicate spawn request key.");
            requestKeys.Add(request.Key,count);
            requests[count++] = request;
        }
        private int WorldIndex(ulong system) => worldLookup.Find(system);
        private void Sort() => ValueOrder.Sort(requests.AsSpan(0,count));
        public void Commit()
        {
            if (count == 0)
                return;
            Sort();
            entities.RequireCreateBatch(count);
            plannedIds.Clear();
            planOrigins.Clear();
            int groups = 0;
            int newWorlds = 0;
            for (int i = 0; i < count; i++)
            {
                Request request = requests[i];
                var origin = new Origin(request);
                int group = planOrigins.Find(origin);
                if (group < 0)
                {
                    group = groups++;
                    planOrigins.Add(origin,group);
                    ulong next;
                    if (request.Spawner.HasValue)
                        next = entities.CapturedSpawnCounter(request.Spawner,request.Counter);
                    else
                    {
                        int cursor = WorldIndex(request.Key.System);
                        if (cursor >= 0 && request.Tick < worlds[cursor].Tick)
                            throw new SimBoundaryException("World spawn ticks cannot move backwards.");
                        next = cursor < 0 || worlds[cursor].Tick != request.Tick ? 0 : worlds[cursor].Next;
                        if (cursor < 0)
                            newWorlds++;
                    }
                    planCursors[group] = new PlanCursor { Next=next, Tick=request.Tick };
                }
                ref PlanCursor plan = ref planCursors[group];
                if (!request.Spawner.HasValue && plan.Tick != request.Tick)
                    throw new SimBoundaryException("A world spawn batch must share one tick per system.");
                ulong ordinal = plan.Next;
                if (request.Spawner.HasValue)
                    entities.RequirePlacedCursor(request.Spawner.Id,ordinal,false);
                if (ordinal == ulong.MaxValue)
                    throw new SimBoundaryException("Spawn counter exhausted.");
                plan.Next = ordinal + 1;
                planned[i] = request.Spawner.HasValue ? StableEntityKey.Spawned(request.Spawner.Id,ordinal,request.Key.Kind)
                    : StableEntityKey.World(request.Key.System,request.Tick,ordinal);
                entities.ValidateNewIdentity(in planned[i],planned[i].Id);
                int conflict = plannedIds.Find(planned[i].Id);
                if (conflict >= 0)
                    throw new StableIdentityConflictException(planned[i].Id,planned[conflict],planned[i]);
                plannedIds.Add(planned[i].Id,i);
            }
            if (newWorlds > worlds.Length-worldCount)
                throw new SimBoundaryException("World spawn cursor capacity exhausted.");
            // All fallible validation precedes creation and cursor consumption.
            for (int i = 0; i < count; i++)
            {
                Request request = requests[i];
                EntityId created = entities.Create(planned[i]);
                if (request.Spawner.HasValue)
                {
                    entities.ConsumeSpawnCounter(request.Spawner,planned[i].Second+1);
                    entities.MoveToChunk(created,request.Chunk);
                }
                else
                {
                    int cursor = WorldIndex(request.Key.System);
                    if (cursor < 0)
                    {
                        cursor = worldCount++;
                        worldLookup.Add(request.Key.System,cursor);
                    }
                    worlds[cursor] = new WorldCursor { System=request.Key.System, Tick=request.Tick, Next=planned[i].Third+1 };
                }
                if (entities.UsesRetainedProtocol)
                    entities.Set(entities.spawnCaptures,created,new SpawnCapture(request.Spawner,request.Values,request.Key.Kind,request.Chunk));
            }
            count = 0;
            requestKeys.Clear();
        }
        internal void Save(SimStateWriter writer)
        {
            Sort();
            writer.Int32(count);
            for (int i = 0; i < count; i++)
            {
                requests[i].Key.Save(writer);
                writer.Link(requests[i].Spawner);
                writer.Int64(requests[i].Tick);
                writer.UInt64(requests[i].Counter);
                writer.UInt64(requests[i].Chunk);
                requests[i].Values.Save(writer);
            }
            ValueOrder.Sort(worlds.AsSpan(0,worldCount));
            worldLookup.Clear();
            writer.Int32(worldCount);
            for (int i = 0; i < worldCount; i++)
            {
                worldLookup.Add(worlds[i].System,i);
                writer.UInt64(worlds[i].System);
                writer.Int64(worlds[i].Tick);
                writer.UInt64(worlds[i].Next);
            }
        }
        internal void Load(SimStateReader reader)
        {
            count = 0;
            requestKeys.Clear();
            int pending = reader.Count(requests.Length);
            for (int i = 0; i < pending; i++)
            {
                StableRequestKey key = StableRequestKey.Read(reader);
                EntityLink spawner = reader.Link();
                long tick = reader.Int64();
                SimStateReader.Require(tick >= 0 && (i == 0 || requests[i-1].Key.CompareTo(key) < 0),"Invalid spawn request order.");
                var request = new Request { Key=key, Spawner=spawner, Tick=tick };
                request.Counter = reader.UInt64();
                request.Chunk = reader.UInt64();
                request.Values = SpawnValues.Read(reader);
                Add(request);
            }
            worldCount = reader.Count(worlds.Length);
            worldLookup.Clear();
            for (int i = 0; i < worldCount; i++)
            {
                worlds[i] = new WorldCursor { System=reader.UInt64(), Tick=reader.Int64(), Next=reader.UInt64() };
                SimStateReader.Require(worlds[i].Tick >= 0 && (i == 0 || worlds[i-1].System < worlds[i].System),"Invalid world spawn cursor.");
                worldLookup.Add(worlds[i].System,i);
            }
        }
    }

    public sealed partial class EntityStore
    {
        internal ComponentType<SpawnSequence> spawnSequences;
        internal void RequireCreateBatch(int count)
        {
            if (count < 0 || count > identities.Length-identityCount || count > (long)freeCount+Capacity-allocated)
                throw new SimBoundaryException("Spawn batch exceeds reserved capacity.");
        }
    }
}
