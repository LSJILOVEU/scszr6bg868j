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
        // Guard-slot lattice spacing in millimetres; a property of the scheduler, valid under every part.
        public const int DefaultSlotSpacingMm = 1000;
        public int SlotSpacingMm { get; }
        // Placement "scatter": half the side of the square around a point's centre that each unit's spot falls in (millimetres).
        public int ScatterMm { get; }
        // Width in millimetres of the ring of guard slots outside each capture circle; a scheduler property like the spacing. 0 = none.
        public int SlotRingMm { get; }
        // Builds the load error for slotRingMm (pack, layer, entry, field); null when the field is not written.
        internal System.Func<string, ContentException> SlotRingError { get; set; }
        // Engage part id (ContentPolicies.EngageIds): what a unit that sees an enemy does.
        public string Engage { get; }
        // Engage "bound": ticks of the move and fire windows (whole multiples of the battle's thinkInterval), share in
        // thousandths of the units that advance at all, and the horizontal enemy distance (millimetres) below which none advances.
        public int BoundMoveTicks { get; }
        public int BoundFireTicks { get; }
        public int BoundSharePermille { get; }
        public int BoundMinRangeMm { get; }
        // Builds the load error for a written field (pack, layer, entry, field); null when the section is not written.
        internal System.Func<string, string, ContentException> FieldError { get; set; }
        internal AiRules(int failedSlotTicks, int targetMemoryTicks = 0, int searchTicks = 0, int reactionTicks = 0, int senseTicks = 1,
            int burstShots = 0, int burstPauseTicks = 0, int suppressTicks = 0, int meleeChaseRange = 0, bool fairDecisions = false, bool burstPauseFollowsShooter = false,
            int holdSharePermille = 0, int attackSpreadPoints = 1,
            string dispatch = ContentPolicies.NearestDispatch, string placement = ContentPolicies.NearestSlot,
            int slotSpacingMm = DefaultSlotSpacingMm, int scatterMm = 0, int slotRingMm = 0,
            string engage = ContentPolicies.HoldEngage, int boundMoveTicks = 0, int boundFireTicks = 0, int boundSharePermille = 0, int boundMinRangeMm = 0)
        {
            FailedSlotTicks = failedSlotTicks; TargetMemoryTicks = targetMemoryTicks; SearchTicks = searchTicks; ReactionTicks = reactionTicks;
            SenseTicks = senseTicks; BurstShots = burstShots; BurstPauseTicks = burstPauseTicks; SuppressTicks = suppressTicks;
            MeleeChaseRange = meleeChaseRange;
            FairDecisions = fairDecisions;
            BurstPauseFollowsShooter = burstPauseFollowsShooter;
            HoldSharePermille = holdSharePermille;
            AttackSpreadPoints = attackSpreadPoints;
            Dispatch = dispatch; Placement = placement;
            SlotSpacingMm = slotSpacingMm; ScatterMm = scatterMm; SlotRingMm = slotRingMm;
            Engage = engage; BoundMoveTicks = boundMoveTicks; BoundFireTicks = boundFireTicks;
            BoundSharePermille = boundSharePermille; BoundMinRangeMm = boundMinRangeMm;
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
        internal const int MinSlotSpacingMm = 500, MaxSlotSpacingMm = 5000;
        internal const int MaxScatterMm = 50000;
        internal const int MaxSlotRingMm = 50000;
        internal const int MaxBoundTicks = 100000;
        internal const int MaxBoundMinRangeMm = 512000; // PLACEHOLDER: maximum map side length.
        // The kinds of part a parameter can belong to.
        internal enum PartKind { Dispatch, Placement, Engage }
        // Which part each part parameter belongs to. A parameter written while its part is not selected is an error.
        // New parts register their parameters here.
        internal static readonly (string field, PartKind kind, string part)[] Owners =
        {
            ("holdSharePermille", PartKind.Dispatch, ContentPolicies.NearestDispatch),
            ("attackSpreadPoints", PartKind.Dispatch, ContentPolicies.NearestDispatch),
            ("scatterMm", PartKind.Placement, ContentPolicies.Scatter),
            ("boundMoveTicks", PartKind.Engage, ContentPolicies.BoundEngage),
            ("boundFireTicks", PartKind.Engage, ContentPolicies.BoundEngage),
            ("boundSharePermille", PartKind.Engage, ContentPolicies.BoundEngage),
            ("boundMinRangeMm", PartKind.Engage, ContentPolicies.BoundEngage),
        };
        private static string Selected(PartKind kind, string dispatch, string placement, string engage) =>
            kind == PartKind.Dispatch ? dispatch : kind == PartKind.Placement ? placement : engage;
        // Index in Owners of the first parameter written for a part that is not selected; -1 when there is none.
        internal static int Misplaced(System.Func<string, bool> has, string dispatch, string placement, string engage = ContentPolicies.HoldEngage)
        {
            for (int i = 0; i < Owners.Length; i++)
                if (has(Owners[i].field) && Selected(Owners[i].kind, dispatch, placement, engage) != Owners[i].part) return i;
            return -1;
        }
        internal AiSection() : base(SectionKey) { }

        public override void Build(SectionBuild b)
        {
            ContentEntry entry = b.Single(Key);
            int failed = 0, memory = 0, search = 0, reaction = 0, sense = 1, burst = 0, pause = 0, suppress = 0, chase = 0, hold = 0, spread = 1;
            int spacing = AiRules.DefaultSlotSpacingMm, scatter = 0, ring = 0;
            System.Func<string, ContentException> ringError = null;
            bool follows = false;
            bool fair = false;
            string dispatch = ContentPolicies.NearestDispatch, placement = ContentPolicies.NearestSlot, engage = ContentPolicies.HoldEngage;
            int boundMove = 0, boundFire = 0, boundShare = 0, boundMinRange = 0;
            System.Func<string, string, ContentException> fieldError = null;
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
                if (r.Has("engage"))
                {
                    engage = r.Str("engage");
                    if (!ContentPolicies.IsEngage(engage))
                        throw r.Bad(r.FieldOf("engage"), "unknown policy '" + engage + "', allowed: " + ContentPolicies.EngageIds);
                }
                if (r.Has("slotSpacingMm")) spacing = r.Int("slotSpacingMm", MinSlotSpacingMm, MaxSlotSpacingMm);
                if (r.Has("slotRingMm"))
                {
                    ring = r.Int("slotRingMm", 0, MaxSlotRingMm);
                    ContentField field = r.FieldOf("slotRingMm");
                    ringError = why => r.Bad(field, why);
                }
                int misplaced = Misplaced(r.Has, dispatch, placement, engage);
                if (misplaced >= 0)
                {
                    var (field, partKind, part) = Owners[misplaced];
                    string kind = partKind == PartKind.Dispatch ? "dispatch" : partKind == PartKind.Placement ? "placement" : "engage";
                    throw r.Bad(r.FieldOf(field), "belongs to " + kind + " '" + part + "', but " + kind + " is '" + Selected(partKind, dispatch, placement, engage) + "'");
                }
                // Required when scatter is selected: a missing field throws "is missing field 'scatterMm'".
                if (placement == ContentPolicies.Scatter) scatter = r.Int("scatterMm", 1, MaxScatterMm);
                // Required when bound is selected, all four. Whole multiples of thinkInterval are checked with the battle.
                if (engage == ContentPolicies.BoundEngage)
                {
                    boundMove = r.Int("boundMoveTicks", 1, MaxBoundTicks);
                    boundFire = r.Int("boundFireTicks", 1, MaxBoundTicks);
                    boundShare = r.Int("boundSharePermille", 0, 1000);
                    boundMinRange = r.Int("boundMinRangeMm", 0, MaxBoundMinRangeMm);
                }
                fieldError = (field, why) => r.Bad(r.FieldOf(field), why);
                r.Done();
            }
            b.Publish(new AiRules(failed, memory, search, reaction, sense, burst, pause, suppress, chase, fair, follows, hold, spread, dispatch, placement, spacing, scatter, ring,
                engage, boundMove, boundFire, boundShare, boundMinRange) { SlotRingError = ringError, FieldError = fieldError });
        }
    }
}
