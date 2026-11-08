using System;
using System.Collections.Generic;
using WarCore.Combat;
using WarCore.Sim;

namespace WarCore.Battle
{
    // Remembers, per AI unit, the enemy it last saw and where: T = the target's stable identity, P = the target's point at
    // the moment it was seen (frozen: never follows the target afterwards), S = that tick. One entry per unit; seeing
    // any target again replaces the whole entry. The entry is readable for M ticks (S .. S+M-1) while T is alive.
    //
    // Recording happens inside the AI (12) on its thinking ticks, forgetting here (13) after it, and TryRecall applies the
    // window itself, so the result does not depend on which side of the AI this system runs. Behaviour packages only read
    // through TryRecall; nothing but Saw writes P.
    public sealed class TargetMemory : IRetainedGameplaySystem
    {
        private struct Entry { internal StableId Target; internal SimVec3 At; internal long Seen; }

        private readonly CombatSystem combat;
        private readonly int ticks;
        private readonly SortedDictionary<ulong, Entry> entries = new SortedDictionary<ulong, Entry>();
        private readonly List<ulong> keys = new List<ulong>(), drop = new List<ulong>();

        public TargetMemory(CombatSystem combat, int ticks)
        {
            if (ticks < 1) throw new ArgumentOutOfRangeException(nameof(ticks));
            this.combat = combat ?? throw new ArgumentNullException(nameof(combat));
            this.ticks = ticks;
        }

        public int Order => 13;
        public string StateTypeId => "battle.ai-contacts";
        public int StateVersion => 1;
        internal int Count => entries.Count;

        public void Step(SimContext c)
        {
            long tick = c.Clock.Tick;
            keys.Clear(); keys.AddRange(entries.Keys); drop.Clear();
            foreach (ulong key in keys)
            {
                Entry e = entries[key];
                if (!Alive(new StableId(key)) || !Alive(e.Target) || tick - e.Seen >= ticks) drop.Add(key);
            }
            foreach (ulong key in drop) entries.Remove(key);
        }

        // The observer's AI selected this target on this tick.
        internal void Saw(StableId observer, EntityId target, long tick) =>
            entries[observer.Value] = new Entry { Target = combat.Entities.StableOf(target), At = combat.Point(target), Seen = tick };

        internal void Forget(StableId observer) => entries.Remove(observer.Value);

        // True while the entry exists, 0 <= tick - S < M and the target is alive: gives (T, P, S). Otherwise false and defaults.
        public bool TryRecall(StableId observer, long tick, out StableId target, out SimVec3 lastKnown, out long seenTick)
        {
            if (entries.TryGetValue(observer.Value, out Entry e) && tick - e.Seen >= 0 && tick - e.Seen < ticks && Alive(e.Target))
            { target = e.Target; lastKnown = e.At; seenTick = e.Seen; return true; }
            target = default; lastKnown = default; seenTick = 0;
            return false;
        }

        private bool Alive(StableId id) => combat.Entities.TryResolve(id, out EntityRef entity) && !combat.IsDead(entity);

        public void SaveState(SimStateWriter writer)
        {
            writer.Int32(ticks);
            writer.Int32(entries.Count);
            foreach (KeyValuePair<ulong, Entry> pair in entries)
            {
                Entry e = pair.Value;
                writer.UInt64(pair.Key); writer.UInt64(e.Target.Value);
                writer.Int64(e.At.X.Raw); writer.Int64(e.At.Y.Raw); writer.Int64(e.At.Z.Raw); writer.Int64(e.Seen);
            }
        }

        public void LoadState(SimStateReader reader)
        {
            SimStateReader.Require(reader.Int32() == ticks, "AI target memory length differs.");
            int count = reader.Count(combat.Entities.Capacity);
            entries.Clear();
            ulong previous = 0;
            for (int i = 0; i < count; i++)
            {
                ulong key = reader.UInt64();
                SimStateReader.Require(i == 0 || key > previous, "AI target memory observers are not in strictly increasing order.");
                previous = key;
                var target = new StableId(reader.UInt64());
                var at = new SimVec3(SimFix.FromRaw(reader.Int64()), SimFix.FromRaw(reader.Int64()), SimFix.FromRaw(reader.Int64()));
                long seen = reader.Int64();
                SimStateReader.Require(seen >= 0, "AI target memory tick is negative.");
                entries[key] = new Entry { Target = target, At = at, Seen = seen };
            }
        }

        public void WriteState(StateHash hash)
        {
            hash.Add(StateTypeId); hash.Add(StateVersion); hash.Add(ticks); hash.Add(entries.Count);
            foreach (KeyValuePair<ulong, Entry> pair in entries)
            {
                Entry e = pair.Value;
                hash.Add(pair.Key); hash.Add(e.Target.Value); hash.Add(e.At.X.Raw); hash.Add(e.At.Y.Raw); hash.Add(e.At.Z.Raw); hash.Add(e.Seen);
            }
        }
    }
}
