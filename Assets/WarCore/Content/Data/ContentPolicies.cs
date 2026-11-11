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

        public static bool IsResolver(string id) => id == RwrHit;
        public static bool IsSelector(string id) => id == NearestVisible || id == SightLimitedNearest;
        public static IHitResolver Resolver(string id) => id == null ? null : new RwrHitResolver();
        public static ITargetSelector Selector(string id, SimFix sight) => id == SightLimitedNearest
            ? new SightLimited(new NearestVisibleEnemy(), sight)
            : (ITargetSelector)new NearestVisibleEnemy();
    }
}
