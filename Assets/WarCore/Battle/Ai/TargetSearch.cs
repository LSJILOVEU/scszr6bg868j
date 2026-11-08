using System;
using System.Collections.Generic;
using WarCore.Sim;

namespace WarCore.Battle
{
    // What an AI unit does about a target it remembers but cannot see (TargetMemory): walk to the last known point P,
    // stand there N ticks, then give up on that memory. One entry per unit, made only on arrival: the memory's seen tick
    // S it belongs to and the arrival tick. While walking nothing is stored; a walk that ends short of P (blocked, no
    // route) is simply ordered again on the next thinking tick, until the memory runs out.
    //
    // Order 17, after the memory (13): an entry goes when its memory is no longer readable or has been refreshed.
    public sealed class TargetSearch : IRetainedGameplaySystem
    {
        internal enum Hunt { None, Go, Hold }
        private struct Entry { internal long Seen, Since; }

        private readonly EntityStore entities;
        private readonly TargetMemory contacts;
        private readonly int ticks;
        private readonly SortedDictionary<ulong, Entry> entries = new SortedDictionary<ulong, Entry>();
        private readonly List<ulong> keys = new List<ulong>(), drop = new List<ulong>();

        public TargetSearch(EntityStore entities, TargetMemory contacts, int ticks)
        {
            if (ticks < 1) throw new ArgumentOutOfRangeException(nameof(ticks));
            this.entities = entities ?? throw new ArgumentNullException(nameof(entities));
            this.contacts = contacts ?? throw new ArgumentNullException(nameof(contacts));
            this.ticks = ticks;
        }

        public int Order => 17;
        public string StateTypeId => "battle.ai-search";
        public int StateVersion => 1;
        internal int Count => entries.Count;

        public void Step(SimContext c)
        {
            long tick = c.Clock.Tick;
            keys.Clear(); keys.AddRange(entries.Keys); drop.Clear();
            foreach (ulong key in keys)
                if (!contacts.TryRecall(new StableId(key), tick, out _, out _, out long seen) || seen != entries[key].Seen) drop.Add(key);
            foreach (ulong key in drop) entries.Remove(key);
        }

        // Called by the AI on a thinking tick when it sees no target. here is the unit's point before this tick's move.
        internal Hunt Decide(StableId observer, SimVec3 here, long tick, out SimVec3 lastKnown)
        {
            if (!contacts.TryRecall(observer, tick, out _, out lastKnown, out long seen)) return Hunt.None;
            if (entries.TryGetValue(observer.Value, out Entry e) && e.Seen == seen) return tick - e.Since < ticks ? Hunt.Hold : Hunt.None;
            if (SimpleAiSystem.Arrived(here, lastKnown)) { entries[observer.Value] = new Entry { Seen = seen, Since = tick }; return Hunt.Hold; }
            return Hunt.Go;
        }

        public void SaveState(SimStateWriter writer)
        {
            writer.Int32(ticks);
            writer.Int32(entries.Count);
            foreach (KeyValuePair<ulong, Entry> pair in entries)
            { writer.UInt64(pair.Key); writer.Int64(pair.Value.Seen); writer.Int64(pair.Value.Since); }
        }

        public void LoadState(SimStateReader reader)
        {
            SimStateReader.Require(reader.Int32() == ticks, "AI search length differs.");
            int count = reader.Count(entities.Capacity);
            entries.Clear();
            ulong previous = 0;
            for (int i = 0; i < count; i++)
            {
                ulong key = reader.UInt64();
                SimStateReader.Require(i == 0 || key > previous, "AI search observers are not in strictly increasing order.");
                previous = key;
                long seen = reader.Int64(), since = reader.Int64();
                SimStateReader.Require(seen >= 0, "AI search seen tick is negative.");
                SimStateReader.Require(since >= seen, "AI search arrival precedes the sighting.");
                entries[key] = new Entry { Seen = seen, Since = since };
            }
        }

        public void WriteState(StateHash hash)
        {
            hash.Add(StateTypeId); hash.Add(StateVersion); hash.Add(ticks); hash.Add(entries.Count);
            foreach (KeyValuePair<ulong, Entry> pair in entries) { hash.Add(pair.Key); hash.Add(pair.Value.Seen); hash.Add(pair.Value.Since); }
        }
    }
}
