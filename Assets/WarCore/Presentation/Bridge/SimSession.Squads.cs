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
    }
}
