using System;
using System.Collections.Generic;

namespace WarCore.Sim
{
    public readonly struct EventTypeId : IStateHash, IEquatable<EventTypeId>
    {
        public int Value
        {
            get;
        }

        public EventTypeId(int value)
        {
            if (value <= 0)
                throw new ArgumentOutOfRangeException(nameof(value));
            Value = value;
        }

        public bool Equals(EventTypeId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is EventTypeId other && Equals(other);
        public override int GetHashCode() => Value;
        public void WriteState(StateHash hash)
        {
            hash.Add(Value);
        }
    }

    public sealed class EventTypeRegistry : IStateHash
    {
        public const int EventsDropped = 1;
        public const int WorldChange = 2;
        private readonly SortedDictionary<string, EventTypeId> names = new SortedDictionary<string, EventTypeId>(StringComparer.Ordinal);
        private readonly Dictionary<int, string> ids = new Dictionary<int, string>();
        public EventTypeId Register(string name)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("A stable event name is required.", nameof(name));
            if (names.TryGetValue(name, out EventTypeId existing))
                return existing;
            var hash = new StateHash();
            hash.Add(name);
            int id = name == "events-dropped" ? EventsDropped : name == "world-change" ? WorldChange : (int)(hash.Value % 2147483645UL) + 3;
            // Fail rather than silently alias two names: a hidden collision would change event meaning.
            if (ids.TryGetValue(id, out string collision))
                throw new InvalidOperationException("Event id collision: " + collision + " / " + name);
            var result = new EventTypeId(id);
            names.Add(name, result);
            ids.Add(id, name);
            return result;
        }

        public void WriteState(StateHash hash)
        {
            hash.Add(names.Count);
            foreach (var pair in names)
            {
                hash.Add(pair.Key);
                pair.Value.WriteState(hash);
            }
        }
    }

    public readonly struct SimEvent : IStateHash
    {
        public EventTypeId Type
        {
            get;
        }

        public long Tick
        {
            get;
        }

        public long Sequence
        {
            get;
        }

        public EntityId Source
        {
            get;
        }

        public EntityId Target
        {
            get;
        }

        public SimVec3 Position
        {
            get;
        }

        public SimVec3 Aux
        {
            get;
        }

        public SimFix Magnitude
        {
            get;
        }

        public SimFix Radius
        {
            get;
        }

        public int Tag
        {
            get;
        }

        public SimEvent(EventTypeId type, long tick = 0, EntityId source = default, EntityId target = default, SimVec3 position = default, SimVec3 aux = default, SimFix magnitude = default, SimFix radius = default, int tag = 0, long sequence = 0)
        {
            if (type.Value <= 0 || tick < 0 || sequence < 0)
                throw new ArgumentOutOfRangeException(nameof(type));
            Type = type;
            Tick = tick;
            Sequence = sequence;
            Source = source;
            Target = target;
            Position = position;
            Aux = aux;
            Magnitude = magnitude;
            Radius = radius;
            Tag = tag;
        }

        public SimEvent At(long tick, long sequence = 0) => new SimEvent(Type, tick, Source, Target, Position, Aux, Magnitude, Radius, Tag, sequence);
        // Centre plus half extents avoids overflowing endpoint arithmetic in region consumers.
        // Tag is the reason id; this is a regular event type, not a second event channel.
        public static SimEvent WorldChangeEvent(long tick, SimVec3 center, SimVec3 halfExtents, int reason)
        {
            center.ValidateWorld();
            if (halfExtents.X.Raw < 0 || halfExtents.Y.Raw < 0 || halfExtents.Z.Raw < 0)
                throw new ArgumentOutOfRangeException(nameof(halfExtents));
            return new SimEvent(new EventTypeId(EventTypeRegistry.WorldChange), tick, position: center, aux: halfExtents, tag: reason);
        }

        public void WriteState(StateHash hash)
        {
            Type.WriteState(hash);
            hash.Add(Tick);
            hash.Add(Sequence);
            Source.WriteState(hash);
            Target.WriteState(hash);
            Position.WriteState(hash);
            Aux.WriteState(hash);
            Magnitude.WriteState(hash);
            Radius.WriteState(hash);
            hash.Add(Tag);
        }
    }

    public interface ISimEventReader
    {
        ReadOnlySpan<SimEvent> Read(long tick);
        long OldestAvailableSequence
        {
            get;
        }

        long NextSequence
        {
            get;
        }

        long EventsDroppedThisTick
        {
            get;
        }

        long EventsDroppedTotal
        {
            get;
        }
    }

    public sealed partial class SimEventBuffer : ISimEventReader, IStateHash
    {
        private readonly SimEvent[][] slots;
        private readonly long[] ticks;
        private readonly int[] counts;
        private readonly int hardLimit;
        private long currentTick;
        private bool active;
        private bool frozen;
        public long NextSequence
        {
            get;
            private set;
        }

        = 1;
        public long EventsDroppedThisTick
        {
            get;
            private set;
        }

        public long EventsDroppedTotal
        {
            get;
            private set;
        }

        public int RetainedTicks => slots.Length;
        public int HardLimit => hardLimit;
        public int InitialCapacity { get; }
        public SimEventBuffer(int retainedTicks = 8, int initialCapacity = 4096, int hardLimit = SimConfig.DefaultEventHardLimit)
        {
            if (retainedTicks < 1 || initialCapacity < 1 || hardLimit < initialCapacity || hardLimit == int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(hardLimit));
            InitialCapacity = initialCapacity;
            this.hardLimit = hardLimit;
            slots = new SimEvent[retainedTicks][];
            ticks = new long[retainedTicks];
            counts = new int[retainedTicks];
            for (int i = 0; i < slots.Length; i++)
                slots[i] = new SimEvent[initialCapacity + 1];
        }

        public void CompleteWarmup()
        {
            if (frozen)
                return;
            for (int i = 0; i < slots.Length; i++)
                Array.Resize(ref slots[i], hardLimit + 1);
            frozen = true;
        }

        public void BeginTick(long tick)
        {
            if (active || tick != SimChecked.Add(currentTick, 1))
                throw new InvalidOperationException("Event ticks must be consecutive and closed.");
            currentTick = tick;
            int index = (int)(tick % slots.Length);
            ticks[index] = tick;
            counts[index] = 0;
            EventsDroppedThisTick = 0;
            active = true;
        }

        public bool Publish(SimEvent item)
        {
            if (stableLinks != null && !publishingStable)
                throw new SimBoundaryException("StableWorld requires stable event references.");
            if (!active || item.Type.Value == EventTypeRegistry.EventsDropped)
                throw new InvalidOperationException("Invalid event publication.");
            long sequence = NextSequence;
            long nextSequence = SimChecked.Add(sequence, 1);
            int index = (int)(currentTick % slots.Length);
            if (counts[index] == hardLimit)
            {
                long droppedTick = SimChecked.Add(EventsDroppedThisTick, 1);
                long droppedTotal = SimChecked.Add(EventsDroppedTotal, 1);
                EventsDroppedThisTick = droppedTick;
                EventsDroppedTotal = droppedTotal;
                NextSequence = nextSequence;
                return false;
            }

            if (counts[index] == slots[index].Length - 1)
            {
                if (frozen)
                    throw new InvalidOperationException("Event capacity was not reserved before warmup ended.");
                Array.Resize(ref slots[index], (int)SimLimits.Min(hardLimit, (long)(slots[index].Length - 1) * 2) + 1);
            }

            slots[index][counts[index]++] = item.At(currentTick, sequence);
            NextSequence = nextSequence;
            return true;
        }

        public void EndTick()
        {
            if (!active)
                throw new InvalidOperationException("No event tick is active.");
            if (EventsDroppedThisTick != 0)
            {
                int index = (int)(currentTick % slots.Length);
                long nextSequence = SimChecked.Add(NextSequence, 1);
                SimFix dropped = SimFix.FromInt(EventsDroppedThisTick);
                // Validate all counters before publishing the reserved summary slot.
                if (stableLinks != null)
                    stableLinks[index][counts[index]] = default;
                slots[index][counts[index]++] = new SimEvent(new EventTypeId(EventTypeRegistry.EventsDropped), currentTick, magnitude: dropped, sequence: NextSequence);
                NextSequence = nextSequence;
            }

            active = false;
        }

        public ReadOnlySpan<SimEvent> Read(long tick)
        {
            if (tick <= 0 || tick > currentTick || currentTick - tick >= slots.Length)
                return ReadOnlySpan<SimEvent>.Empty;
            int index = (int)(tick % slots.Length);
            return ticks[index] == tick ? new ReadOnlySpan<SimEvent>(slots[index], 0, counts[index]) : ReadOnlySpan<SimEvent>.Empty;
        }

        // A span is valid only until the next Step/BeginTick; retaining it is not a snapshot.
        public long OldestAvailableSequence
        {
            get
            {
                int retained = (int)SimLimits.Min(currentTick, slots.Length);
                long first = currentTick - retained + 1;
                for (int offset = 0; offset < retained; offset++)
                {
                    long tick = first + offset;
                    ReadOnlySpan<SimEvent> items = Read(tick);
                    if (items.Length != 0)
                        return items[0].Sequence;
                }

                return NextSequence;
            }
        }

        public void WriteState(StateHash hash)
        {
            hash.Add(slots.Length);
            hash.Add(hardLimit);
            hash.Add(currentTick);
            hash.Add(NextSequence);
            hash.Add(EventsDroppedThisTick);
            hash.Add(EventsDroppedTotal);
            int retained = (int)SimLimits.Min(currentTick, slots.Length);
            long first = currentTick - retained + 1;
            for (int offset = 0; offset < retained; offset++)
            {
                long tick = first + offset;
                ReadOnlySpan<SimEvent> items = Read(tick);
                hash.Add(tick);
                hash.Add(items.Length);
                foreach (SimEvent item in items)
                    item.WriteState(hash);
            }
        }
    }

    internal static class SimLimits
    {
        internal static long Min(long a, long b) => a < b ? a : b;
        internal static long Max(long a, long b) => a > b ? a : b;
        internal static long Clamp(long value, long min, long max) => value < min ? min : value > max ? max : value;
    }
}
