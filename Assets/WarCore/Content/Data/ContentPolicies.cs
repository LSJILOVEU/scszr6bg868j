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
        public const string RwrHit = "rwr.hit", NearestVisible = "nearest-visible", SightLimitedNearest = "sight-limited";
        public const string ResolverIds = RwrHit;
        public const string SelectorIds = NearestVisible + ", " + SightLimitedNearest;
        // AI parts (ai section): dispatch picks the point a unit goes for, placement where at it the unit wants to stand.
        public const string NearestDispatch = "nearest", NearestSlot = "nearest-slot", Scatter = "scatter";
        public const string DispatchIds = NearestDispatch;
        public const string PlacementIds = NearestSlot + ", " + Scatter;

        public static bool IsResolver(string id) => id == RwrHit;
        public static bool IsSelector(string id) => id == NearestVisible || id == SightLimitedNearest;
        public static bool IsDispatch(string id) => id == NearestDispatch;
        public static bool IsPlacement(string id) => id == NearestSlot || id == Scatter;
        public static IHitResolver Resolver(string id) => id == null ? null : new RwrHitResolver();
        public static ITargetSelector Selector(string id, SimFix sight) => id == SightLimitedNearest
            ? new SightLimited(new NearestVisibleEnemy(), sight)
            : (ITargetSelector)new NearestVisibleEnemy();
        // A recipe without AI rules runs the default parts with their parameters off. Slot spacing and ring: millimetres to
        // raw units by SimFix.Ratio(mm, 1000) (1000 mm is exactly GuardSlots.SpacingRaw). With a ring, the slot zones
        // (radius + ring) of two points must stay at least one spacing apart, or loading the battle fails.
        public static AiParts Parts(AiRules ai, ReadOnlySpan<ControlPointDef> points = default)
        {
            long spacing = SimFix.Ratio(ai?.SlotSpacingMm ?? AiRules.DefaultSlotSpacingMm, 1000).Raw;
            long ring = SimFix.Ratio(ai?.SlotRingMm ?? 0, 1000).Raw;
            if (ring > 0) CheckZones(ai, points, spacing, ring);
            return new AiParts(
                new NearestDispatch(ai?.HoldSharePermille ?? 0, ai?.AttackSpreadPoints ?? 1),
                ai?.Placement == Scatter ? new ScatterPlacement(ai.ScatterMm) : (IAiPlacement)new NearestSlotPlacement(),
                spacing, ring);
        }

        // Zones touch when the horizontal centre distance is below (radius_i + ring) + (radius_j + ring) + spacing.
        private static void CheckZones(AiRules ai, ReadOnlySpan<ControlPointDef> points, long spacing, long ring)
        {
            for (int i = 0; i < points.Length; i++)
                for (int j = i + 1; j < points.Length; j++)
                {
                    SimFix gap = points[i].Radius + points[j].Radius + SimFix.FromRaw(2 * ring + spacing);
                    SimWide distance = CombatSystem.Horizontal(points[i].Center - points[j].Center).LengthSquared;
                    if (distance < new SimVec3(gap, default, default).LengthSquared)
                        throw ai.SlotRingError("slot zones of points " + points[i].Id + " and " + points[j].Id + " are less than one slot spacing apart");
                }
        }
    }
}
