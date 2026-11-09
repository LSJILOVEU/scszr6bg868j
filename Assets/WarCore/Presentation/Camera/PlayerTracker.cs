namespace WarCore.Presentation
{
    // Remembers whether the followed unit was lost since the host started. One Advance can span both the death tick and the
    // takeover tick: the old corpse is then still in Current while the newly controlled unit is already alive.
    public sealed class PlayerTracker
    {
        private ulong? lastKey;
        public bool LostPlayer { get; private set; }
        // Whether the key given to the last Update is in that frame and alive.
        public bool Alive { get; private set; }

        public PlayerTracker(ulong? initialKey) { lastKey = initialKey; }

        public bool Update(FrameView current, ulong? key)
        {
            Alive = key.HasValue && current.TryGet(key.Value, out ViewEntity controlled) && controlled.Alive;
            if (!Alive || (lastKey.HasValue && key != lastKey
                && current.TryGet(lastKey.Value, out ViewEntity old) && !old.Alive))
                LostPlayer = true;
            lastKey = key;
            return LostPlayer;
        }
    }
}
