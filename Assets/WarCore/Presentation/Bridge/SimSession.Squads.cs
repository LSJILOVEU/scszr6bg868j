namespace WarCore.Presentation
{
    // AI squads: what the frame tells about a unit's squad. In its own file because SimSession.cs is at the review limit.
    public sealed partial class SimSession
    {
        // The spawn key of the unit with this stable identity; 0 for 0 (no squad) and for an identity that is no unit's.
        private ulong BandKey(ulong leader)
        {
            if (leader == 0) return 0;
            if (spawnKeys == null)
            {
                spawnKeys = new System.Collections.Generic.Dictionary<ulong, ulong>(units.Length);
                for (int i = 0; i < units.Length; i++) spawnKeys[units[i].Value] = spawns[i].Key;
            }
            return spawnKeys.TryGetValue(leader, out ulong key) ? key : 0;
        }

        // RC2-1. The session's public members are a locked list, so these four stay internal: ShowcaseActions (the keys)
        // and ShowcaseHud (the status line) are the public way in.
        // True in a world whose content pack has the led squad's rules and that has a controlled unit.
        internal bool LedOrders => led != null;
        // What the squad the controlled unit leads is doing: 0 there is none, 1 it follows its leader, 2 it holds a place.
        internal int LedOrder => led == null ? 0 : (int)led.Mode;
        internal int LedCount => led == null ? 0 : led.MemberCount;

        // The first tick from the given one on that no command is queued for. The session keeps one command a tick and
        // Enqueue drops a second without a word, so an order given from a key asks here for its tick.
        internal long FreeTick(long from)
        {
            if (from <= Tick) throw new System.ArgumentOutOfRangeException(nameof(from), "A command must target a future tick.");
            for (int i = 0; i < pending.Count && pending[i].AtTick <= from; i++)
                if (pending[i].AtTick == from) from++;
            return from;
        }
    }
}
