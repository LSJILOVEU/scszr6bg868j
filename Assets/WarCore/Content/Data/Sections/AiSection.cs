namespace WarCore.Content
{
    // AI tuning that a content pack may set. Every pack publishes one AiRules, written or not, so reading it never throws.
    public sealed class AiRules
    {
        // Ticks during which the AI skips a guard slot whose route failed; 0 turns the memory off.
        public int FailedSlotTicks { get; }
        // Ticks during which an AI unit remembers where it last saw its target; 0 turns the memory off.
        public int TargetMemoryTicks { get; }
        // Ticks an AI unit stands at its target's last known point before giving up on it; 0 turns the search off. Needs the memory.
        public int SearchTicks { get; }
        internal AiRules(int failedSlotTicks, int targetMemoryTicks = 0, int searchTicks = 0)
        { FailedSlotTicks = failedSlotTicks; TargetMemoryTicks = targetMemoryTicks; SearchTicks = searchTicks; }
    }

    internal sealed class AiSection : ObjectSection
    {
        internal const string SectionKey = "ai";
        internal const int MaxFailedSlotTicks = 1000000;
        internal const int MaxTargetMemoryTicks = 1000000;
        internal const int MaxSearchTicks = 1000000;
        internal AiSection() : base(SectionKey) { }

        public override void Build(SectionBuild b)
        {
            ContentEntry entry = b.Single(Key);
            int failed = 0, memory = 0, search = 0;
            if (entry != null)
            {
                var r = new EntryReader(b.Pack, entry);
                if (r.Has("failedSlotTicks")) failed = r.Int("failedSlotTicks", 0, MaxFailedSlotTicks);
                if (r.Has("targetMemoryTicks")) memory = r.Int("targetMemoryTicks", 0, MaxTargetMemoryTicks);
                if (r.Has("searchTicks")) search = r.Int("searchTicks", 0, MaxSearchTicks);
                r.Done();
            }
            b.Publish(new AiRules(failed, memory, search));
        }
    }
}
