using System;

namespace WarCore.Sim
{
    public enum IdentityMode { Legacy, StableWorld }
    public enum IdentityLife { Active, Cold, Retired }

    public readonly struct StableId : IEquatable<StableId>, IComparable<StableId>
    {
        public ulong Value { get; }
        public StableId(ulong value) => Value = value;
        public int CompareTo(StableId other) => Value.CompareTo(other.Value);
        public bool Equals(StableId other) => Value == other.Value;
        public override bool Equals(object other) => other is StableId id && Equals(id);
        public override int GetHashCode() => SimInteger.Int32Bits(Value ^ (Value >> 32));
        public override string ToString() => Value.ToString("X16");
    }

    // This address is local to one store and one restore epoch. It is never a persistence key.
    public readonly struct EntityRef : IEquatable<EntityRef>
    {
        public int Slot { get; }
        public ulong Generation { get; }
        public EntityRef(int slot, ulong generation)
        {
            Slot = slot;
            Generation = generation;
        }
        public bool Equals(EntityRef other) => Slot == other.Slot && Generation == other.Generation;
        public override bool Equals(object other) => other is EntityRef value && Equals(value);
        public override int GetHashCode() => Slot ^ SimInteger.Int32Bits(Generation);
        public static implicit operator EntityId(EntityRef value) => new EntityId(value.Slot, value.Generation);
        public static implicit operator EntityRef(EntityId value) => new EntityRef(value.Id, value.Generation);
    }

    // Capture at the reference's creation, so death and slot reuse cannot change its meaning.
    public readonly struct EntityLink : IEquatable<EntityLink>, IStateHash
    {
        public bool HasValue { get; }
        public StableId Id { get; }
        public EntityLink(StableId id)
        {
            HasValue = true;
            Id = id;
        }
        public bool Equals(EntityLink other) => HasValue == other.HasValue && (!HasValue || Id.Equals(other.Id));
        public override bool Equals(object other) => other is EntityLink value && Equals(value);
        public override int GetHashCode() => HasValue ? Id.GetHashCode() ^ 397 : 0;
        public void WriteState(StateHash hash)
        {
            hash.Add(HasValue);
            if (HasValue)
                hash.Add(Id.Value);
        }
    }

    public readonly struct StableEntityKey : IEquatable<StableEntityKey>
    {
        public const ulong ProtocolVersion = 1;
        public ulong Domain { get; }
        public ulong First { get; }
        public ulong Second { get; }
        public ulong Third { get; }
        public StableId Id => new StableId(Hash(Domain, First, Second, Third));
        internal StableEntityKey(ulong domain, ulong first, ulong second, ulong third = 0)
        {
            if (domain < 1 || domain > 3 || (domain == 1 && third != 0) || (domain == 3 && second > long.MaxValue))
                throw new SimBoundaryException("Invalid stable source key.");
            Domain = domain;
            First = first;
            Second = second;
            Third = third;
        }
        public static StableEntityKey Placed(ulong chunk, ulong placement) => new StableEntityKey(1, chunk, placement);
        internal static StableEntityKey Spawned(StableId owner, ulong counter, ulong kind) => new StableEntityKey(2, owner.Value, counter, kind);
        internal static StableEntityKey World(ulong system, long tick, ulong ordinal)
        {
            if (tick < 0)
                throw new SimBoundaryException("World spawn tick must be nonnegative.");
            return new StableEntityKey(3, system, (ulong)tick, ordinal);
        }
        private static ulong Hash(ulong domain, ulong a, ulong b, ulong c)
        {
            if (domain < 1 || domain > 3)
                throw new SimBoundaryException("A stable source key is required.");
            ulong hash = 14695981039346656037UL;
            Word(ref hash, ProtocolVersion);
            Word(ref hash, domain);
            Word(ref hash, domain == 1 ? 2UL : 3UL);
            Word(ref hash, a);
            Word(ref hash, b);
            if (domain != 1)
                Word(ref hash, c);
            return Mix(hash);
        }
        internal static void Word(ref ulong hash, ulong value)
        {
            for (int i = 0; i < 8; i++)
            {
                hash = unchecked((hash ^ (byte)value) * 1099511628211UL);
                value >>= 8;
            }
        }
        internal static ulong Mix(ulong value)
        {
            value = unchecked((value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL);
            value = unchecked((value ^ (value >> 27)) * 0x94D049BB133111EBUL);
            return value ^ (value >> 31);
        }
        public bool Equals(StableEntityKey other) => Domain == other.Domain && First == other.First && Second == other.Second && Third == other.Third;
        public override bool Equals(object other) => other is StableEntityKey key && Equals(key);
        public override int GetHashCode() => Id.GetHashCode();
        public override string ToString() => Domain + ":" + First + ":" + Second + ":" + Third;
    }

    public sealed class StableIdentityConflictException : SimBoundaryException
    {
        public StableId Identity { get; }
        public StableEntityKey Existing { get; }
        public StableEntityKey Requested { get; }
        internal StableIdentityConflictException(StableId id, StableEntityKey existing, StableEntityKey requested)
            : base("Stable identity conflict: " + id + "; existing=" + existing + "; requested=" + requested)
        {
            Identity = id;
            Existing = existing;
            Requested = requested;
        }
    }
}
