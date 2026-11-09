using System;
using System.Collections.Generic;
using WarCore.Combat;
using WarCore.Sim;

namespace WarCore.Battle
{
    // Suppressive fire at a target an AI unit remembers but cannot see (TargetMemory): from the thinking tick on which the
    // AI saw nothing while it still remembered a target, the unit submits an aimed fire intent at the last known point P
    // on every tick for D ticks, as long as the memory still names that sighting, P is within the weapon's range, in the
    // wall line of sight, and not under the unit's feet. One entry per unit: the sighting (S) it belongs to and the tick
    // the suppression started. The entry goes when the memory no longer gives that sighting (expired, target dead, seen
    // again or replaced); a sighting is suppressed once, so an expired entry stays until its memory goes.
    //
    // Order 18, after the memory (13) and the AI fire state (14): the intent replaces the automatic decision for the tick.
    public sealed class TargetSuppress : IRetainedGameplaySystem
    {
        private struct Entry { internal long Seen, Since; }

        private readonly CombatSystem combat;
        private readonly TargetMemory contacts;
        private readonly IMoveOrders moves;
        private readonly int ticks;
        private readonly SortedDictionary<ulong, Entry> entries = new SortedDictionary<ulong, Entry>();
        private readonly List<ulong> keys = new List<ulong>(), drop = new List<ulong>();

        public TargetSuppress(CombatSystem combat, TargetMemory contacts, IMoveOrders moves, int ticks)
        {
            if (ticks < 1) throw new ArgumentOutOfRangeException(nameof(ticks));
            this.combat = combat ?? throw new ArgumentNullException(nameof(combat));
            this.contacts = contacts ?? throw new ArgumentNullException(nameof(contacts));
            this.moves = moves ?? throw new ArgumentNullException(nameof(moves));
            this.ticks = ticks;
        }

        public int Order => 18;
        public string StateTypeId => "battle.ai-suppress";
        public int StateVersion => 1;
        internal int Count => entries.Count;

        // Called by the AI on a thinking tick when it sees no target: starts suppressing the remembered sighting once.
        internal void Decide(StableId observer, long tick)
        {
            if (!contacts.TryRecall(observer, tick, out _, out _, out long seen)) return;
            if (entries.TryGetValue(observer.Value, out Entry e) && e.Seen == seen) return;
            entries[observer.Value] = new Entry { Seen = seen, Since = tick };
        }

        public void Step(SimContext c)
        {
            long tick = c.Clock.Tick;
            keys.Clear(); keys.AddRange(entries.Keys); drop.Clear();
            foreach (ulong key in keys)
            {
                Entry e = entries[key];
                var observer = new StableId(key);
                if (!contacts.TryRecall(observer, tick, out _, out SimVec3 lastKnown, out long seen) || seen != e.Seen) { drop.Add(key); continue; }
                if (tick - e.Since >= ticks || moves.IsCommanded(observer) || !combat.Entities.TryResolve(observer, out EntityRef found)) continue;
                var shooter = (EntityId)found;
                Combatant unit = combat.Unit(shooter);
                if (unit.Weapon < 0) continue;
                SimVec3 here = combat.Point(shooter);
                SimFix range = combat.Pack.Weapons[unit.Weapon].FalloffEnd;
                if (SimpleAiSystem.Arrived(here, lastKnown)
                    || CombatSystem.Horizontal(lastKnown - here).LengthSquared > new SimVec3(range, default, default).LengthSquared
                    || !combat.Visible(shooter, lastKnown)) continue;
                combat.Submit(c, FireIntent.Suppress(observer, lastKnown));
            }
            foreach (ulong key in drop) entries.Remove(key);
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
            SimStateReader.Require(reader.Int32() == ticks, "AI suppression length differs.");
            int count = reader.Count(combat.Entities.Capacity);
            entries.Clear();
            ulong previous = 0;
            for (int i = 0; i < count; i++)
            {
                ulong key = reader.UInt64();
                SimStateReader.Require(i == 0 || key > previous, "AI suppression observers are not in strictly increasing order.");
                previous = key;
                long seen = reader.Int64(), since = reader.Int64();
                SimStateReader.Require(seen >= 0, "AI suppression seen tick is negative.");
                SimStateReader.Require(since >= seen, "AI suppression start precedes the sighting.");
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
