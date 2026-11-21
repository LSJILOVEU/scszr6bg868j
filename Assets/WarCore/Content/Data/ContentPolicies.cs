using System;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Content.Rwr;
using WarCore.Sim;

namespace WarCore.Content
{
    // Data names a policy by id; this closed list maps ids to the C# objects. Unknown ids are an error, and the
    // objects keep their own PolicyKey (the id string is never hashed).
    public static class ContentPolicies
    {
        public const string Damage = "damage", RwrHit = "rwr.hit", NearestVisible = "nearest-visible", SightLimitedNearest = "sight-limited";
        public const string ResolverIds = Damage + ", " + RwrHit;
        public const string SelectorIds = NearestVisible + ", " + SightLimitedNearest;
        // AI parts (ai section): dispatch picks the point a unit goes for, placement where at it the unit wants to stand.
        public const string NearestDispatch = "nearest", NearestSlot = "nearest-slot", Scatter = "scatter", AssaultRoles = "assault-roles";
        public const string DispatchIds = NearestDispatch;
        public const string PlacementIds = NearestSlot + ", " + Scatter + ", " + AssaultRoles;
        // Engage: what a unit that sees an enemy does (stand and fire, or go on).
        public const string HoldEngage = "hold", BoundEngage = "bound";
        public const string EngageIds = HoldEngage + ", " + BoundEngage;

        public static bool IsResolver(string id) => id == Damage || id == RwrHit;
        public static bool IsSelector(string id) => id == NearestVisible || id == SightLimitedNearest;
        public static bool IsDispatch(string id) => id == NearestDispatch;
        public static bool IsPlacement(string id) => id == NearestSlot || id == Scatter || id == AssaultRoles;
        public static bool IsEngage(string id) => id == HoldEngage || id == BoundEngage;
        public static IHitResolver Resolver(string id) => id == null ? null : id == Damage ? new DamageResolver() : (IHitResolver)new RwrHitResolver();
        public static ITargetSelector Selector(string id, SimFix sight) => id == SightLimitedNearest
            ? new SightLimited(new NearestVisibleEnemy(), sight)
            : (ITargetSelector)new NearestVisibleEnemy();
        // A recipe without AI rules runs the default parts with their parameters off. Slot spacing and ring: millimetres to
        // raw units by SimFix.Ratio(mm, 1000) (1000 mm is exactly GuardSlots.SpacingRaw). With a ring, the slot zones
        // (radius + ring) of two points must stay at least one spacing apart, or loading the battle fails.
        // thinkInterval is the battle's (MatchPack.ThinkInterval): the scheduler asks the engage part only on think ticks,
        // so bound's move and fire windows must be whole multiples of it, or loading the battle fails. The default 1 checks nothing.
        // flat: the battle runs on a flat map. A cover spot takes the height of the unit that looks for it, so cover rules
        // on a terrain map fail the load.
        // plates: the map has floor plates (ML-11a). The parts that still measure on the plane are refused there.
        // squads: the battle's squad system, or null. With it the dispatch and the placement are wrapped (Squads.Wrap).
        public static AiParts Parts(AiRules ai, ReadOnlySpan<ControlPointDef> points = default, int thinkInterval = 1, bool flat = true, bool plates = false, Squads squads = null)
        {
            if (!flat && ai != null && ai.CoverRadiusMm > 0) throw ai.FieldError("coverRadiusMm", "is not supported on a terrain map");
            if (plates && ai != null) CheckPlates(ai);
            long spacing = SimFix.Ratio(ai?.SlotSpacingMm ?? AiRules.DefaultSlotSpacingMm, 1000).Raw;
            long ring = SimFix.Ratio(ai?.SlotRingMm ?? 0, 1000).Raw;
            if (ring > 0) CheckZones(ai, points, spacing, ring);
            IAiEngage engage = new HoldEngage();
            if (ai?.Engage == BoundEngage)
            {
                CheckThinks(ai, "boundMoveTicks", ai.BoundMoveTicks, thinkInterval);
                CheckThinks(ai, "boundFireTicks", ai.BoundFireTicks, thinkInterval);
                engage = new BoundEngage(ai.BoundMoveTicks, ai.BoundFireTicks, ai.BoundSharePermille, ai.BoundMinRangeMm);
            }
            IAiDispatch dispatch = new NearestDispatch(ai?.HoldSharePermille ?? 0, ai?.AttackSpreadPoints ?? 1);
            IAiPlacement placement = Placement(ai);
            // Squads wrap the battle's own dispatch and placement: a member asks them as its leader.
            if (squads != null) { dispatch = squads.Wrap(dispatch); placement = squads.Wrap(placement); }
            return new AiParts(
                dispatch, placement, spacing, ring, engage,
                SimFix.Ratio(ai?.CoverRadiusMm ?? 0, 1000).Raw, SimFix.Ratio(ai?.CoverStepMm ?? 0, 1000).Raw, SimFix.Ratio(ai?.CoverDepthMm ?? 0, 1000).Raw);
        }

        private const string NoPlates = "is not supported on a map with floor plates";
        private static void CheckPlates(AiRules ai)
        {
            if (ai.Pressure != null) throw Refused(ai, "pressureNearRadiusMm");
            if (ai.Engage == BoundEngage) throw Refused(ai, "engage");
            if (ai.Placement == Scatter || ai.Placement == AssaultRoles) throw Refused(ai, "placement");
            if (ai.Squads != null) throw Refused(ai, "squadMinSize");
        }

        // Rules built in code have no place in a pack to point at.
        private static Exception Refused(AiRules ai, string field) => ai.FieldError != null
            ? ai.FieldError(field, NoPlates) : new NotSupportedException("ai." + field + " " + NoPlates + ".");

        private static void CheckThinks(AiRules ai, string field, int ticks, int thinkInterval)
        {
            if (thinkInterval < 1) throw new ArgumentOutOfRangeException(nameof(thinkInterval));
            if (ticks % thinkInterval != 0)
                throw ai.FieldError(field, "must be a whole multiple of the battle's thinkInterval (" + thinkInterval + ")");
        }
        private static IAiPlacement Placement(AiRules ai) =>
            ai?.Placement == Scatter ? new ScatterPlacement(ai.ScatterMm)
            : ai?.Placement == AssaultRoles ? new AssaultRolesPlacement(ai.FrontPermille, ai.FlankPermille, ai.RearPermille,
                ai.FrontArcMm, ai.FlankSideMm, ai.RearOutMm, ai.FlankMinMm, ai.AnchorMm, ai.ArriveMm)
            : (IAiPlacement)new NearestSlotPlacement();

        // Zones touch when the horizontal centre distance is below (radius_i + ring) + (radius_j + ring) + spacing. ML-9b: two
        // points whose centres are on different layers never share slots, so they are not compared.
        private static void CheckZones(AiRules ai, ReadOnlySpan<ControlPointDef> points, long spacing, long ring)
        {
            for (int i = 0; i < points.Length; i++)
                for (int j = i + 1; j < points.Length; j++)
                {
                    if (points[i].Layer != points[j].Layer) continue;
                    SimFix gap = points[i].Radius + points[j].Radius + SimFix.FromRaw(2 * ring + spacing);
                    SimWide distance = CombatSystem.Horizontal(points[i].Center - points[j].Center).LengthSquared;
                    if (distance < new SimVec3(gap, default, default).LengthSquared)
                        throw ai.SlotRingError("slot zones of points " + points[i].Id + " and " + points[j].Id + " are less than one slot spacing apart");
                }
        }
    }
}
