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
        // Rounds an AI shooter fires at its target before it pauses; 0 turns the burst off (continuous fire).
        public int BurstShots { get; }
        // Ticks an AI shooter holds its fire after a burst, counted from the burst's last shot; ignored when the burst is off.
        public int BurstPauseTicks { get; }
        public bool BurstPauseFollowsShooter { get; }
        // Ticks an AI unit keeps firing at its target's last known point after it lost sight of it; 0 turns the suppression off. Needs the memory.
        public int SuppressTicks { get; }
        public int MeleeChaseRange { get; }
        public bool FairDecisions { get; }
        // Share, in thousandths, of each team's AI units that hold the team's front-line point instead of attacking; 0 turns it off.
        public int HoldSharePermille { get; }
        // Number of nearest points an attacker spreads over (each takes the k-th nearest point its team does not own); 1 sends all to the nearest.
        public int AttackSpreadPoints { get; }
        // Ids of the AI parts (ContentPolicies.DispatchIds, PlacementIds): which point a unit goes for, and where at it the unit wants to stand.
        public string Dispatch { get; }
        public string Placement { get; }
        // Parameters of the assault-roles placement (set only when it is chosen; the shares add up to 1000).
        public int FrontPermille { get; }
        public int FlankPermille { get; }
        public int RearPermille { get; }
        public int FrontArcMm { get; }
        public int FlankSideMm { get; }
        public int RearOutMm { get; }
        public int FlankMinMm { get; }
        public int AnchorMm { get; }
        public int ArriveMm { get; }
        internal AiRules(int failedSlotTicks, int targetMemoryTicks = 0, int searchTicks = 0, int reactionTicks = 0, int senseTicks = 1,
            int burstShots = 0, int burstPauseTicks = 0, int suppressTicks = 0, int meleeChaseRange = 0, bool fairDecisions = false, bool burstPauseFollowsShooter = false,
            int holdSharePermille = 0, int attackSpreadPoints = 1,
            string dispatch = ContentPolicies.NearestDispatch, string placement = ContentPolicies.NearestSlot, int[] assault = null)
        {
            FailedSlotTicks = failedSlotTicks; TargetMemoryTicks = targetMemoryTicks; SearchTicks = searchTicks; ReactionTicks = reactionTicks;
            SenseTicks = senseTicks; BurstShots = burstShots; BurstPauseTicks = burstPauseTicks; SuppressTicks = suppressTicks;
            MeleeChaseRange = meleeChaseRange;
            FairDecisions = fairDecisions;
            BurstPauseFollowsShooter = burstPauseFollowsShooter;
            HoldSharePermille = holdSharePermille;
            AttackSpreadPoints = attackSpreadPoints;
            Dispatch = dispatch; Placement = placement;
            if (assault != null)
            {
                FrontPermille = assault[0]; FlankPermille = assault[1]; RearPermille = assault[2]; FrontArcMm = assault[3]; FlankSideMm = assault[4];
                RearOutMm = assault[5]; FlankMinMm = assault[6]; AnchorMm = assault[7]; ArriveMm = assault[8];
            }
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
        internal const int MaxBurstShots = 1000000;
        internal const int MaxBurstPauseTicks = 1000000;
        internal const int MaxSuppressTicks = 1000000;
        internal const int MaxMeleeChaseRange = 512; // PLACEHOLDER: maximum map side length in metres.
        internal const int MaxHoldSharePermille = 1000;
        internal const int MaxAttackSpreadPoints = 8;
        // assault-roles parameters, in AssaultRolesPlacement's constructor order, with their ranges (PLACEHOLDER bounds).
        internal static readonly string[] AssaultFields =
            { "frontPermille", "flankPermille", "rearPermille", "frontArcMm", "flankSideMm", "rearOutMm", "flankMinMm", "anchorMm", "arriveMm" };
        private static readonly int[] AssaultMin = { 0, 0, 0, 0, 1000, 1000, 0, 1000, 250 };
        private static readonly int[] AssaultMax = { 1000, 1000, 1000, 50000, 100000, 100000, 512000, 50000, 20000 };
        internal AiSection() : base(SectionKey) { }

        public override void Build(SectionBuild b)
        {
            ContentEntry entry = b.Single(Key);
            int failed = 0, memory = 0, search = 0, reaction = 0, sense = 1, burst = 0, pause = 0, suppress = 0, chase = 0, hold = 0, spread = 1;
            bool follows = false;
            bool fair = false;
            string dispatch = ContentPolicies.NearestDispatch, placement = ContentPolicies.NearestSlot;
            int[] assault = null;
            if (entry != null)
            {
                var r = new EntryReader(b.Pack, entry);
                if (r.Has("failedSlotTicks")) failed = r.Int("failedSlotTicks", 0, MaxFailedSlotTicks);
                if (r.Has("targetMemoryTicks")) memory = r.Int("targetMemoryTicks", 0, MaxTargetMemoryTicks);
                if (r.Has("searchTicks")) search = r.Int("searchTicks", 0, MaxSearchTicks);
                if (r.Has("reactionTicks")) reaction = r.Int("reactionTicks", 0, MaxReactionTicks);
                if (r.Has("senseTicks")) sense = r.Int("senseTicks", 1, MaxSenseTicks);
                if (r.Has("burstShots")) burst = r.Int("burstShots", 0, MaxBurstShots);
                if (r.Has("burstPauseTicks")) pause = r.Int("burstPauseTicks", 0, MaxBurstPauseTicks);
                if (r.Has("burstPauseFollowsShooter")) follows = r.Bool("burstPauseFollowsShooter");
                if (r.Has("suppressTicks")) suppress = r.Int("suppressTicks", 0, MaxSuppressTicks);
                if (r.Has("meleeChaseRange")) chase = r.Int("meleeChaseRange", 0, MaxMeleeChaseRange);
                if (r.Has("fairDecisions")) fair = r.Bool("fairDecisions");
                if (r.Has("holdSharePermille")) hold = r.Int("holdSharePermille", 0, MaxHoldSharePermille);
                if (r.Has("attackSpreadPoints")) spread = r.Int("attackSpreadPoints", 1, MaxAttackSpreadPoints);
                if (r.Has("dispatch"))
                {
                    dispatch = r.Str("dispatch");
                    if (!ContentPolicies.IsDispatch(dispatch))
                        throw r.Bad(r.FieldOf("dispatch"), "unknown policy '" + dispatch + "', allowed: " + ContentPolicies.DispatchIds);
                }
                if (r.Has("placement"))
                {
                    placement = r.Str("placement");
                    if (!ContentPolicies.IsPlacement(placement))
                        throw r.Bad(r.FieldOf("placement"), "unknown policy '" + placement + "', allowed: " + ContentPolicies.PlacementIds);
                }
                if (placement == ContentPolicies.AssaultRoles)
                {
                    assault = new int[AssaultFields.Length];
                    for (int i = 0; i < assault.Length; i++)
                    {
                        if (!r.Has(AssaultFields[i])) throw r.Bad(r.FieldOf("placement"), "'" + placement + "' needs field '" + AssaultFields[i] + "'");
                        assault[i] = r.Int(AssaultFields[i], AssaultMin[i], AssaultMax[i]);
                    }
                    if (assault[0] + assault[1] + assault[2] != 1000)
                        throw r.Bad(r.FieldOf("rearPermille"), "frontPermille + flankPermille + rearPermille must be 1000, got " + (assault[0] + assault[1] + assault[2]));
                }
                else
                    foreach (string field in AssaultFields)
                        if (r.Has(field)) throw r.Bad(r.FieldOf(field), "belongs to placement '" + ContentPolicies.AssaultRoles + "', not '" + placement + "'");
                r.Done();
            }
            b.Publish(new AiRules(failed, memory, search, reaction, sense, burst, pause, suppress, chase, fair, follows, hold, spread, dispatch, placement, assault));
        }
    }
}
