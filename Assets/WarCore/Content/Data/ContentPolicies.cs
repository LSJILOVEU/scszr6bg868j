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
        public const string NearestDispatch = "nearest", NearestSlot = "nearest-slot";
        public const string DispatchIds = NearestDispatch;
        public const string PlacementIds = NearestSlot;

        public static bool IsResolver(string id) => id == RwrHit;
        public static bool IsSelector(string id) => id == NearestVisible || id == SightLimitedNearest;
        public static bool IsDispatch(string id) => id == NearestDispatch;
        public static bool IsPlacement(string id) => id == NearestSlot;
        public static IHitResolver Resolver(string id) => id == null ? null : new RwrHitResolver();
        public static ITargetSelector Selector(string id, SimFix sight) => id == SightLimitedNearest
            ? new SightLimited(new NearestVisibleEnemy(), sight)
            : (ITargetSelector)new NearestVisibleEnemy();
        // A recipe without AI rules runs the default parts with their parameters off.
        public static AiParts Parts(AiRules ai) => new AiParts(
            new NearestDispatch(ai?.HoldSharePermille ?? 0, ai?.AttackSpreadPoints ?? 1), new NearestSlotPlacement());
    }
}
