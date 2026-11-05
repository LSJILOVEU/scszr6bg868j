using System;

namespace WarCore.Sim
{
    // Seat entities allow any number of seats on an ordinary carrier entity.
    public readonly struct Seat : IStateHash
    {
        public EntityId Carrier { get; }
        public EntityLink CarrierLink { get; }
        public EntityLink OccupantLink { get; }
        internal bool StableLinks { get; }
        public int Index { get; }
        public EntityId Occupant { get; }
        public Seat(EntityId carrier, int index, EntityId occupant = default) : this()
        {
            if (index < 0)
                throw new ArgumentOutOfRangeException(nameof(index));
            Carrier = carrier;
            Index = index;
            Occupant = occupant;
        }

        public Seat(EntityLink carrier, int index, EntityLink occupant = default) : this(default(EntityId),index)
        {
            CarrierLink = carrier;
            OccupantLink = occupant;
            StableLinks = true;
        }

        public void WriteState(StateHash hash)
        {
            Carrier.WriteState(hash);
            hash.Add(Index);
            Occupant.WriteState(hash);
        }
    }

    public readonly struct Occupant : IStateHash
    {
        public EntityId Seat { get; }
        public EntityLink SeatLink { get; }
        internal bool StableLinks { get; }
        public Occupant(EntityId seat) : this()
        {
            Seat = seat;
        }

        public Occupant(EntityLink seat) : this()
        {
            SeatLink = seat;
            StableLinks = true;
        }

        public void WriteState(StateHash hash) => Seat.WriteState(hash);
    }

    // Inventory rules and item identities belong to content; the engine only retains slot counts.
    public readonly struct Container : IStateHash
    {
        public int Capacity { get; }
        public int Count { get; }
        public Container(int capacity, int count = 0)
        {
            if (capacity < 0 || count < 0 || count > capacity)
                throw new ArgumentOutOfRangeException(nameof(count));
            Capacity = capacity;
            Count = count;
        }

        public void WriteState(StateHash hash)
        {
            hash.Add(Capacity);
            hash.Add(Count);
        }
    }

    public readonly struct MassClass : IStateHash
    {
        public int Value { get; }
        public MassClass(int value)
        {
            if (value < 0)
                throw new ArgumentOutOfRangeException(nameof(value));
            Value = value;
        }

        public void WriteState(StateHash hash) => hash.Add(Value);
    }

    public interface IVisibilityProvider
    {
        bool IsVisible(int observerFaction, EntityId target, long tick);
    }

    public readonly struct ObservedIntel
    {
        public EntityId Entity { get; }
        public SimVec3 Position { get; }
        public long ObservedTick { get; }
        public SimFix Confidence { get; }
        public ObservedIntel(EntityId entity, SimVec3 position, long observedTick, SimFix confidence)
        {
            position.ValidateWorld();
            if (observedTick < 0 || confidence.Raw < 0 || confidence.Raw > SimFix.Scale)
                throw new ArgumentOutOfRangeException(nameof(confidence));
            Entity = entity;
            Position = position;
            ObservedTick = observedTick;
            Confidence = confidence;
        }
    }

    public interface IFactionIntel
    {
        // Providers apply content-defined observation delay and decay before returning intelligence.
        bool TryGet(int observerFaction, EntityId target, long readTick, out ObservedIntel observation);
    }

    // This contract has no Sim serializer: campaign bytes have a separate lifetime and save file.
    public interface ICampaignLayer
    {
        byte[] SaveCampaign();
        void LoadCampaign(byte[] data);
    }

    public readonly struct SoundEvent
    {
        public SimVec3 Position { get; }
        public SimFix LoudnessRadius { get; }
        public int SourceFaction { get; }
        public int TypeTag { get; }
        public EntityId Source { get; }
        public EntityLink SourceLink { get; }
        public bool UsesStableIdentity { get; }
        public SoundEvent(SimVec3 position, SimFix loudnessRadius, int sourceFaction, int typeTag, EntityId source = default)
        {
            position.ValidateWorld();
            SpatialRequest.ValidateDistance(loudnessRadius);
            if (sourceFaction < 0)
                throw new ArgumentOutOfRangeException(nameof(sourceFaction));
            Position = position;
            LoudnessRadius = loudnessRadius;
            SourceFaction = sourceFaction;
            TypeTag = typeTag;
            Source = source;
            SourceLink = default;
            UsesStableIdentity = false;
        }

        private SoundEvent(SimVec3 position, SimFix radius, int faction, int tag, EntityLink source)
            : this(position,radius,faction,tag)
        {
            SourceLink = source;
            UsesStableIdentity = true;
        }
        public static SoundEvent Stable(SimVec3 position, SimFix radius, int faction, int tag, EntityLink source = default)
            => new SoundEvent(position,radius,faction,tag,source);
        public StableSimEvent ToStableEvent(EventTypeId type)
        {
            if (!UsesStableIdentity)
                throw new SimBoundaryException("Stable sounds require a captured source identity.");
            return new StableSimEvent(type,SourceLink,position:Position,
                aux:new SimVec3(SimFix.FromRaw(SourceFaction),default,default),radius:LoudnessRadius,tag:TypeTag);
        }
        public static SoundEvent FromStableEvent(StableSimEvent item, EventTypeId type)
        {
            SoundEvent value = FromEvent(item.Payload,type);
            return Stable(value.Position,value.LoudnessRadius,value.SourceFaction,value.TypeTag,item.Source);
        }

        // Aux.X stores the integer faction in raw units; Tag remains the content sound kind.
        // An explicit registered type preserves the existing fixed event layout and hash protocol.
        public SimEvent ToEvent(EventTypeId type)
        {
            if (UsesStableIdentity)
                throw new SimBoundaryException("Stable sound conversion requires ToStableEvent.");
            return new SimEvent(type, source: Source, position: Position,
                aux: new SimVec3(SimFix.FromRaw(SourceFaction), default, default), radius: LoudnessRadius, tag: TypeTag);
        }

        public static SoundEvent FromEvent(SimEvent item, EventTypeId type)
        {
            if (!item.Type.Equals(type) || item.Aux.X.Raw < 0 || item.Aux.X.Raw > int.MaxValue
                || item.Aux.Y.Raw != 0 || item.Aux.Z.Raw != 0)
                throw new ArgumentException("Invalid sound event payload.");
            return new SoundEvent(item.Position, item.Radius, (int)item.Aux.X.Raw, item.Tag, item.Source);
        }
    }
}
