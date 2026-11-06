using WarCore.Sim;

namespace WarCore.Content
{
    internal sealed class CombatSection : ObjectSection
    {
        internal const string SectionKey = "combat";
        internal CombatSection() : base(SectionKey) { }

        public override void Build(SectionBuild b)
        {
            ContentEntry combat = b.Single(Key);
            if (combat == null) throw new ContentException("content pack '" + b.Pack + "' has no combat section");
            var cr = new EntryReader(b.Pack, combat);
            SimFix maxHealth = cr.Fix("maxHealth");
            if (maxHealth.Raw <= 0) throw cr.Bad(cr.FieldOf("maxHealth"), "must be greater than 0");
            string resolver = cr.Str("resolver"), selector = cr.Str("selector");
            if (!ContentPolicies.IsResolver(resolver))
                throw cr.Bad(cr.FieldOf("resolver"), "unknown policy '" + resolver + "', allowed: " + ContentPolicies.ResolverIds);
            if (!ContentPolicies.IsSelector(selector))
                throw cr.Bad(cr.FieldOf("selector"), "unknown policy '" + selector + "', allowed: " + ContentPolicies.SelectorIds);
            bool friendlyFire = cr.Bool("friendlyFire"), friendlyFireLocked = cr.Bool("friendlyFireLocked");
            SimFix missRate = cr.Fix("allyCheckMissRate");
            if (missRate.Raw < 0 || missRate.Raw > SimFix.Scale) throw cr.Bad(cr.FieldOf("allyCheckMissRate"), "must be between 0 and 1");
            long holdMilli = cr.Long("allyHoldMilli", 1000, SectionLimits.MaxMilli);
            cr.Done();
            b.MaxHealthRaw = maxHealth.Raw; b.ResolverId = resolver; b.SelectorId = selector;
            b.FriendlyFire = friendlyFire; b.FriendlyFireLocked = friendlyFireLocked; b.AllyCheckMissRaw = (int)missRate.Raw; b.AllyHoldMilli = holdMilli;
        }
    }
}
