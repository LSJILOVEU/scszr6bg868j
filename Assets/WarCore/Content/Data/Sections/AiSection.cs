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
        // Ticks an AI shooter waits after it first can select a target before it fires at it; 0 turns the delay off.
        public int ReactionTicks { get; }
        // Ticks between two looks of an AI shooter at the battlefield; between them it keeps firing at the target it has. 1 looks every tick.
        public int SenseTicks { get; }
        internal AiRules(int failedSlotTicks, int targetMemoryTicks = 0, int searchTicks = 0, int reactionTicks = 0, int senseTicks = 1)
        {
            FailedSlotTicks = failedSlotTicks; TargetMemoryTicks = targetMemoryTicks; SearchTicks = searchTicks; ReactionTicks = reactionTicks;
            SenseTicks = senseTicks;
        }
    }

    internal sealed class AiSection : ObjectSection
    {
        internal const string SectionKey = "ai";
        internal const int MaxFailedSlotTicks = 1000000;
        internal const int MaxTargetMemoryTicks = 1000000;
        internal const int MaxSearchTicks = 1000000;
        internal const int MaxReactionTicks = 1000000;
        internal const int MaxSenseTicks = 1000000;
        internal AiSection() : base(SectionKey) { }

        public override void Build(SectionBuild b)
        {
            ContentEntry entry = b.Single(Key);
            int failed = 0, memory = 0, search = 0, reaction = 0, sense = 1;
            if (entry != null)
            {
                var r = new EntryReader(b.Pack, entry);
                if (r.Has("failedSlotTicks")) failed = r.Int("failedSlotTicks", 0, MaxFailedSlotTicks);
                if (r.Has("targetMemoryTicks")) memory = r.Int("targetMemoryTicks", 0, MaxTargetMemoryTicks);
                if (r.Has("searchTicks")) search = r.Int("searchTicks", 0, MaxSearchTicks);
                if (r.Has("reactionTicks")) reaction = r.Int("reactionTicks", 0, MaxReactionTicks);
                if (r.Has("senseTicks")) sense = r.Int("senseTicks", 1, MaxSenseTicks);
                r.Done();
            }
            b.Publish(new AiRules(failed, memory, search, reaction, sense));
        }
    }
}
