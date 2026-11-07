using System;
using System.Collections.Generic;
using WarCore.Sim;

namespace WarCore.Battle
{
    // Remembers, per unit, the last guard slot whose route failed, so the AI skips that slot for a while instead of running
    // at a slot it cannot reach again and again. One entry per unit: O = the slot of the AI's latest own order (canonical
    // slot number, -1 when none), F = the latest failed slot (-1 when none), T = the tick F was recorded.
    //
    // Order 9 puts it before the AI (12): a failure made while ordering (inside the AI) and one made while walking (inside
    // locomotion, 20) are both recorded on the next tick, before the AI chooses again. After the AI (13) the second kind
    // would be lost whenever that next tick is a thinking tick: the AI would order the same slot first, which clears the flag.
    public sealed class FailedSlotMemory : IRetainedGameplaySystem
    {
        private struct Entry { internal int Ordered, Failed; internal long At; }

        private readonly EntityStore entities;
        private readonly IMoveOrders moves;
        private readonly int ticks;
        // True when the unit receives a squad move command to this squad number (same test as the orders system).
        private readonly Func<StableId, int, bool> inSquad;
        private readonly SortedDictionary<ulong, Entry> entries = new SortedDictionary<ulong, Entry>();
        private readonly List<ulong> keys = new List<ulong>(), drop = new List<ulong>();

        public FailedSlotMemory(EntityStore entities, IMoveOrders moves, int ticks, Func<StableId, int, bool> inSquad)
        {
            if (ticks < 1) throw new ArgumentOutOfRangeException(nameof(ticks));
            this.entities = entities ?? throw new ArgumentNullException(nameof(entities));
            this.moves = moves ?? throw new ArgumentNullException(nameof(moves));
            this.inSquad = inSquad ?? throw new ArgumentNullException(nameof(inSquad));
            this.ticks = ticks;
        }

        public int Order => 9;
        public string StateTypeId => "battle.ai-memory";
        public int StateVersion => 1;

        public void Step(SimContext c)
        {
            long tick = c.Clock.Tick;
            keys.Clear(); keys.AddRange(entries.Keys); drop.Clear();
            foreach (ulong key in keys)
            {
                var id = new StableId(key);
                if (!entities.TryResolve(id, out _)) { drop.Add(key); continue; }
                Entry e = entries[key];
                if (e.Ordered >= 0 && moves.HasFailed(id)) { e.Failed = e.Ordered; e.At = tick; e.Ordered = -1; entries[key] = e; }
                if (e.Ordered < 0 && (e.Failed < 0 || tick - e.At > ticks)) drop.Add(key);
            }
            foreach (ulong key in drop) entries.Remove(key);
        }

        // The AI ordered the unit to this canonical slot.
        internal void Ordered(StableId id, int slot)
        {
            Entry e = entries.TryGetValue(id.Value, out Entry found) ? found : new Entry { Failed = -1 };
            e.Ordered = slot; entries[id.Value] = e;
        }

        // The unit's movement is no longer the AI's own order (stopped, skipped, or taken over by a squad command).
        internal void Forget(StableId id)
        {
            if (entries.TryGetValue(id.Value, out Entry e) && e.Ordered >= 0) { e.Ordered = -1; entries[id.Value] = e; }
        }

        // A squad move command this tick that reaches the unit replaces the AI's order, whether or not it succeeds.
        internal void Observe(StableId id, in InputFrame input)
        {
            if (input.Squad.HasValue && input.Squad.Value.Kind == SquadCommandKind.MoveTo && inSquad(id, input.Squad.Value.Squad)) Forget(id);
        }

        internal bool Skips(StableId id, int canonical, long tick) =>
            entries.TryGetValue(id.Value, out Entry e) && e.Failed >= 0 && e.Failed == canonical && tick - e.At < ticks;

        // Test query: (O, F, T) of a unit, (-1, -1, 0) without an entry.
        internal (int ordered, int failed, long at) Query(StableId id) =>
            entries.TryGetValue(id.Value, out Entry e) ? (e.Ordered, e.Failed, e.At) : (-1, -1, 0L);

        public void SaveState(SimStateWriter writer)
        {
            writer.Int32(ticks);
            writer.Int32(entries.Count);
            foreach (KeyValuePair<ulong, Entry> pair in entries)
            { writer.UInt64(pair.Key); writer.Int32(pair.Value.Ordered); writer.Int32(pair.Value.Failed); writer.Int64(pair.Value.At); }
        }

        public void LoadState(SimStateReader reader)
        {
            SimStateReader.Require(reader.Int32() == ticks, "AI failed-slot memory length differs.");
            int count = reader.Count(int.MaxValue);
            entries.Clear();
            for (int i = 0; i < count; i++)
            {
                ulong key = reader.UInt64();
                entries[key] = new Entry { Ordered = reader.Int32(), Failed = reader.Int32(), At = reader.Int64() };
            }
        }

        public void WriteState(StateHash hash)
        {
            hash.Add(StateTypeId); hash.Add(StateVersion); hash.Add(ticks); hash.Add(entries.Count);
            foreach (KeyValuePair<ulong, Entry> pair in entries)
            { hash.Add(pair.Key); hash.Add(pair.Value.Ordered); hash.Add(pair.Value.Failed); hash.Add(pair.Value.At); }
        }
    }
}
