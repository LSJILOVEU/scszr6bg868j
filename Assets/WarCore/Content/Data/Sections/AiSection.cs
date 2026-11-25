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
        // Spread, in thousandths, of each AI shooter's own reaction time, burst length and pause around the values above
        // (each scaled by 1000 + d over 1000, d in [-spread, +spread], from the shooter's identity); 0 = all alike.
        public int TraitSpreadPermille { get; }
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
        // Placement "assault-roles" (all required when it is chosen; the three shares add up to 1000).
        public int FrontPermille { get; }
        public int FlankPermille { get; }
        public int RearPermille { get; }
        public int FrontArcMm { get; }
        public int FlankSideMm { get; }
        public int RearOutMm { get; }
        public int FlankMinMm { get; }
        public int AnchorMm { get; }
        public int ArriveMm { get; }
        // Engage part id (ContentPolicies.EngageIds): what a unit that sees an enemy does.
        public string Engage { get; }
        // Engage "bound": ticks of the move and fire windows (whole multiples of the battle's thinkInterval), share in
        // thousandths of the units that advance at all, and the horizontal enemy distance (millimetres) below which none advances.
        public int BoundMoveTicks { get; }
        public int BoundFireTicks { get; }
        public int BoundSharePermille { get; }
        public int BoundMinRangeMm { get; }
        // The pressure system's numbers (the ten pressure* fields, all or none); null leaves the system out of the battle.
        public WarCore.Battle.PressureRules? Pressure { get; internal set; }
        // Cover (all three or none, only with engage "bound" and the pressure fields), in millimetres: how far a pinned unit
        // looks for a spot behind a wall, the spacing of the spots it tries, and how close in front of the spot the wall
        // must stand. 0 = no cover.
        public int CoverRadiusMm { get; internal set; }
        // AI-M1 (meleeNerve): a melee unit that is pressed asks the engage part before it chases. meleeNerveFightMm (only
        // with meleeNerve on): a unit the part tells to take cover still goes for an enemy within this distance. 0, the
        // default, leaves only an enemy on the unit's own spot within it.
        public bool MeleeNerve { get; internal set; }
        public int MeleeNerveFightMm { get; internal set; }
        public int CoverStepMm { get; internal set; }
        public int CoverDepthMm { get; internal set; }
        // The squad system's numbers (the seven squad* fields, all or none); null leaves squads out of the battle.
        public WarCore.Battle.SquadRules? Squads { get; internal set; }
        // RC1-K3 (standingOrders): the units carry the standing-order component, so a squad holds or follows on command.
        public bool StandingOrders { get; internal set; }
        // Builds the load error for a written field (pack, layer, entry, field); null when the section is not written.
        internal System.Func<string, string, ContentException> FieldError { get; set; }
        internal AiRules(int failedSlotTicks, int targetMemoryTicks = 0, int searchTicks = 0, int reactionTicks = 0, int senseTicks = 1,
            int burstShots = 0, int burstPauseTicks = 0, int suppressTicks = 0, int meleeChaseRange = 0, bool fairDecisions = false, bool burstPauseFollowsShooter = false,
            int holdSharePermille = 0, int attackSpreadPoints = 1,
            string dispatch = ContentPolicies.NearestDispatch, string placement = ContentPolicies.NearestSlot,
            int slotSpacingMm = DefaultSlotSpacingMm, int scatterMm = 0, int slotRingMm = 0,
            string engage = ContentPolicies.HoldEngage, int boundMoveTicks = 0, int boundFireTicks = 0, int boundSharePermille = 0, int boundMinRangeMm = 0,
            int[] assault = null, int traitSpreadPermille = 0)
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
            if (assault != null)
            {
                FrontPermille = assault[0]; FlankPermille = assault[1]; RearPermille = assault[2]; FrontArcMm = assault[3]; FlankSideMm = assault[4];
                RearOutMm = assault[5]; FlankMinMm = assault[6]; AnchorMm = assault[7]; ArriveMm = assault[8];
            }
            TraitSpreadPermille = traitSpreadPermille;
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
        internal const int MaxTraitSpreadPermille = 500;
        internal const int MaxSuppressTicks = 1000000;
        internal const int MaxMeleeChaseRange = WcMapValidation.MaxSide; // the map side limit (WcMapValidation.MaxSide), metres.
        internal const int MaxHoldSharePermille = 1000;
        internal const int MaxAttackSpreadPoints = 8;
        internal const int MinSlotSpacingMm = 500, MaxSlotSpacingMm = 5000;
        internal const int MaxScatterMm = 50000;
        internal const int MaxSlotRingMm = 50000;
        internal const int MaxBoundTicks = 100000;
        internal const int MaxBoundMinRangeMm = WcMapValidation.MaxSideMm; // the map side limit (WcMapValidation.MaxSide).
        internal const int MaxPressureRadiusMm = WcMapValidation.MaxSideMm; // the map side limit (WcMapValidation.MaxSide).
        internal const int MinCoverRadiusMm = 1000, MaxCoverRadiusMm = 30000, MinCoverStepMm = 250, MaxCoverStepMm = 5000, MinCoverDepthMm = 250, MaxCoverDepthMm = 10000; // PLACEHOLDER bounds.
        // What counts as a teammate brought down (pressureFall, optional, only with the ten fields): "hit" (the default) any
        // enemy shot with damage, right for rules where a hit either kills or does nothing; "kill" only a unit whose damage
        // this tick reaches its health, for rules where hits wound first.
        internal const string FallOnHit = "hit", FallOnKill = "kill";
        // The pressure system's fields: written all ten or none.
        internal static readonly string[] PressureFields =
        {
            "pressureDeathRadiusMm", "pressureDeathShock", "pressureNearRadiusMm", "pressureNearShock", "pressureDecayPermille",
            "pressureBrokenAt", "pressureRecoverAt", "pressurePinnedAt", "pressureSpreadPermille", "pressureBreakSharePermille",
        };
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
            ("frontPermille", PartKind.Placement, ContentPolicies.AssaultRoles), ("flankPermille", PartKind.Placement, ContentPolicies.AssaultRoles),
            ("rearPermille", PartKind.Placement, ContentPolicies.AssaultRoles), ("frontArcMm", PartKind.Placement, ContentPolicies.AssaultRoles),
            ("flankSideMm", PartKind.Placement, ContentPolicies.AssaultRoles), ("rearOutMm", PartKind.Placement, ContentPolicies.AssaultRoles),
            ("flankMinMm", PartKind.Placement, ContentPolicies.AssaultRoles), ("anchorMm", PartKind.Placement, ContentPolicies.AssaultRoles),
            ("arriveMm", PartKind.Placement, ContentPolicies.AssaultRoles),
            ("coverRadiusMm", PartKind.Engage, ContentPolicies.BoundEngage), ("coverStepMm", PartKind.Engage, ContentPolicies.BoundEngage),
            ("coverDepthMm", PartKind.Engage, ContentPolicies.BoundEngage),
        };
        // assault-roles parameters in AssaultRolesPlacement's constructor order, with their ranges (PLACEHOLDER bounds).
        // The squad fields: written all seven or none. PLACEHOLDER bounds.
        internal static readonly string[] SquadFields =
            { "squadMinSize", "squadMaxSize", "squadSkirmisherPermille", "squadRecruitRadiusMm", "squadFormTicks", "squadSpreadMinMm", "squadSpreadMaxMm" };
        internal const int MaxSquadSize = 64, MaxSquadRadiusMm = WcMapValidation.MaxSideMm, MaxSquadFormTicks = 100000, MaxSquadSpreadMm = 50000;
        // Squad tempers (all four or none) and regrouping (all three or none); both need the seven squad fields.
        // PLACEHOLDER bounds.
        internal static readonly string[] TemperFields = { "squadRashPermille", "squadSteadyPermille", "squadShakenPermille", "squadRashMinRangeMm" };
        internal static readonly string[] RegroupFields = { "squadRegroupBelowPermille", "squadRegroupUntilPermille", "squadRegroupMaxTicks" };
        internal const int MaxSquadRashRangeMm = WcMapValidation.MaxSideMm, MaxSquadRegroupTicks = 100000;
        // The first field of the group that the entry has written, in the group's order; null when it has written none.
        private static string FirstWritten(EntryReader r, string[] fields)
        {
            foreach (string field in fields) if (r.Has(field)) return field;
            return null;
        }
        internal static readonly string[] AssaultFields =
            { "frontPermille", "flankPermille", "rearPermille", "frontArcMm", "flankSideMm", "rearOutMm", "flankMinMm", "anchorMm", "arriveMm" };
        private static readonly int[] AssaultMin = { 0, 0, 0, 0, 1000, 1000, 0, 1000, 250 };
        private static readonly int[] AssaultMax = { 1000, 1000, 1000, 50000, 100000, 100000, WcMapValidation.MaxSideMm, 50000, 20000 };
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
            bool meleeNerve = false; int meleeFight = 0;
            int spacing = AiRules.DefaultSlotSpacingMm, scatter = 0, ring = 0;
            System.Func<string, ContentException> ringError = null;
            bool follows = false;
            int traits = 0;
            bool fair = false, standing = false;
            string dispatch = ContentPolicies.NearestDispatch, placement = ContentPolicies.NearestSlot, engage = ContentPolicies.HoldEngage;
            int boundMove = 0, boundFire = 0, boundShare = 0, boundMinRange = 0;
            System.Func<string, string, ContentException> fieldError = null;
            int[] assault = null;
            WarCore.Battle.PressureRules? pressure = null;
            int coverRadius = 0, coverStep = 0, coverDepth = 0;
            WarCore.Battle.SquadRules? squads = null;
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
                // A scheduler-side field like the burst: valid under every part.
                if (r.Has("traitSpreadPermille")) traits = r.Int("traitSpreadPermille", 0, MaxTraitSpreadPermille);
                if (r.Has("suppressTicks")) suppress = r.Int("suppressTicks", 0, MaxSuppressTicks);
                if (r.Has("meleeChaseRange")) chase = r.Int("meleeChaseRange", 0, MaxMeleeChaseRange);
                if (r.Has("fairDecisions")) fair = r.Bool("fairDecisions");
                // A switch of the world, valid under every part. False is the same as not written.
                if (r.Has("standingOrders")) standing = r.Bool("standingOrders");
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
                // Required when assault-roles is selected, like scatterMm above.
                if (placement == ContentPolicies.AssaultRoles)
                {
                    assault = new int[AssaultFields.Length];
                    for (int i = 0; i < assault.Length; i++) assault[i] = r.Int(AssaultFields[i], AssaultMin[i], AssaultMax[i]);
                    if (assault[0] + assault[1] + assault[2] != 1000)
                        throw r.Bad(r.FieldOf("rearPermille"), "frontPermille + flankPermille + rearPermille must be 1000, got " + (assault[0] + assault[1] + assault[2]));
                }
                bool pressed = false;
                foreach (string field in PressureFields) pressed |= r.Has(field);
                // Any of the ten written makes all ten required: a missing one throws "is missing field".
                if (pressed)
                {
                    int max = WarCore.Battle.Pressure.Max;
                    int deathRadius = r.Int("pressureDeathRadiusMm", 0, MaxPressureRadiusMm), deathShock = r.Int("pressureDeathShock", 0, max);
                    int nearRadius = r.Int("pressureNearRadiusMm", 0, MaxPressureRadiusMm), nearShock = r.Int("pressureNearShock", 0, max);
                    int decay = r.Int("pressureDecayPermille", 1, 1000), brokenAt = r.Int("pressureBrokenAt", 1, max);
                    int recoverAt = r.Int("pressureRecoverAt", 0, max), pinnedAt = r.Int("pressurePinnedAt", 1, max);
                    int courage = r.Int("pressureSpreadPermille", 0, 1000), breakShare = r.Int("pressureBreakSharePermille", 0, 1000);
                    if (recoverAt >= brokenAt) throw r.Bad(r.FieldOf("pressureRecoverAt"), "must be below pressureBrokenAt (" + brokenAt + ")");
                    string fall = r.StrOrNull("pressureFall", out ContentField fallField) ?? FallOnHit;
                    if (fall != FallOnHit && fall != FallOnKill) throw r.Bad(fallField, "unknown value '" + fall + "', allowed: " + FallOnHit + ", " + FallOnKill);
                    // AI-M1 (pressureStrikes, optional): melee presses too. False is the same as not written.
                    bool strikes = r.Has("pressureStrikes") && r.Bool("pressureStrikes");
                    pressure = new WarCore.Battle.PressureRules(deathRadius, deathShock, nearRadius, nearShock, decay, brokenAt, recoverAt, pinnedAt, courage, breakShare,
                        fall == FallOnKill, strikes);
                }
                else if (r.Has("pressureFall")) throw r.Bad(r.FieldOf("pressureFall"), "needs the pressure fields (pressureDeathRadiusMm ...)");
                else if (r.Has("pressureStrikes")) throw r.Bad(r.FieldOf("pressureStrikes"), "needs the pressure fields (pressureDeathRadiusMm ...)");
                // AI-M1 (meleeNerve, optional): only a pressed unit is asked, and only one that chases. False is the same as not written.
                if (r.Has("meleeNerve"))
                {
                    meleeNerve = r.Bool("meleeNerve");
                    if (!pressed) throw r.Bad(r.FieldOf("meleeNerve"), "needs the pressure fields (pressureDeathRadiusMm ...)");
                    if (chase <= 0) throw r.Bad(r.FieldOf("meleeNerve"), "needs meleeChaseRange");
                }
                if (r.Has("meleeNerveFightMm"))
                {
                    meleeFight = r.Int("meleeNerveFightMm", 0, MaxPressureRadiusMm);
                    if (!meleeNerve) throw r.Bad(r.FieldOf("meleeNerveFightMm"), "needs meleeNerve");
                }
                // Any of the three written makes all three required. Only a pressed unit takes cover, so the pressure fields must be there.
                if (r.Has("coverRadiusMm") || r.Has("coverStepMm") || r.Has("coverDepthMm"))
                {
                    coverRadius = r.Int("coverRadiusMm", MinCoverRadiusMm, MaxCoverRadiusMm);
                    coverStep = r.Int("coverStepMm", MinCoverStepMm, MaxCoverStepMm);
                    coverDepth = r.Int("coverDepthMm", MinCoverDepthMm, MaxCoverDepthMm);
                    if (!pressed) throw r.Bad(r.FieldOf("coverRadiusMm"), "needs the pressure fields (pressureDeathRadiusMm ...)");
                }
                bool banded = false;
                foreach (string field in SquadFields) banded |= r.Has(field);
                // Any of the seven written makes all seven required: a missing one throws "is missing field".
                if (banded)
                {
                    int min = r.Int("squadMinSize", 1, MaxSquadSize), max = r.Int("squadMaxSize", 1, MaxSquadSize);
                    int skirmishers = r.Int("squadSkirmisherPermille", 0, 1000), radius = r.Int("squadRecruitRadiusMm", 1, MaxSquadRadiusMm);
                    int form = r.Int("squadFormTicks", 1, MaxSquadFormTicks);
                    int spreadMin = r.Int("squadSpreadMinMm", 0, MaxSquadSpreadMm), spreadMax = r.Int("squadSpreadMaxMm", 0, MaxSquadSpreadMm);
                    if (max < min) throw r.Bad(r.FieldOf("squadMaxSize"), "must be at least squadMinSize (" + min + ")");
                    if (spreadMax < spreadMin) throw r.Bad(r.FieldOf("squadSpreadMaxMm"), "must be at least squadSpreadMinMm (" + spreadMin + ")");
                    WarCore.Battle.SquadTemper? temper = null;
                    WarCore.Battle.SquadRegroup? regroup = null;
                    // Any of the four written makes all four required, and any of the three all three.
                    if (FirstWritten(r, TemperFields) != null)
                    {
                        int rash = r.Int("squadRashPermille", 0, 1000), steady = r.Int("squadSteadyPermille", 0, 1000);
                        int shaken = r.Int("squadShakenPermille", 0, 1000), range = r.Int("squadRashMinRangeMm", 0, MaxSquadRashRangeMm);
                        if (rash + steady > 1000)
                            throw r.Bad(r.FieldOf("squadSteadyPermille"), "squadRashPermille + squadSteadyPermille must be at most 1000, got " + (rash + steady));
                        temper = new WarCore.Battle.SquadTemper(rash, steady, shaken, range);
                    }
                    if (FirstWritten(r, RegroupFields) != null)
                    {
                        int below = r.Int("squadRegroupBelowPermille", 1, 1000), until = r.Int("squadRegroupUntilPermille", 1, 1000);
                        int ticks = r.Int("squadRegroupMaxTicks", 1, MaxSquadRegroupTicks);
                        if (until < below) throw r.Bad(r.FieldOf("squadRegroupUntilPermille"), "must be at least squadRegroupBelowPermille (" + below + ")");
                        regroup = new WarCore.Battle.SquadRegroup(below, until, ticks);
                    }
                    squads = new WarCore.Battle.SquadRules(min, max, skirmishers, radius, form, spreadMin, spreadMax, temper, regroup);
                }
                else
                {
                    string alone = FirstWritten(r, TemperFields) ?? FirstWritten(r, RegroupFields);
                    if (alone != null) throw r.Bad(r.FieldOf(alone), "needs the squad fields (squadMinSize ...)");
                }
                fieldError = (field, why) => r.Bad(r.FieldOf(field), why);
                r.Done();
            }
            b.Publish(new AiRules(failed, memory, search, reaction, sense, burst, pause, suppress, chase, fair, follows, hold, spread, dispatch, placement, spacing, scatter, ring,
                engage, boundMove, boundFire, boundShare, boundMinRange, assault, traits) { StandingOrders = standing, SlotRingError = ringError, FieldError = fieldError, Pressure = pressure, MeleeNerve = meleeNerve, MeleeNerveFightMm = meleeFight, CoverRadiusMm = coverRadius, CoverStepMm = coverStep, CoverDepthMm = coverDepth, Squads = squads });
        }
    }
}
