using System;
using System.Globalization;
using WarCore.Combat;
using WarCore.Sim;

namespace WarCore.Content
{
    internal sealed class CombatSection : ObjectSection
    {
        internal const string SectionKey = "combat";
        internal CombatSection() : base(SectionKey) { }
        protected override string NestedObject => "body";
        private static readonly string[] Arrays = { BlastsField };
        protected override string[] NestedArrays => Arrays;
        internal const string BlastsField = "blasts";

        // The unit collider radius r (MovementFeature and the deployer place every unit with it).
        internal const long UnitRadiusRaw = SimFix.Scale / 4;

        // ML-3 body { height, eye, muzzle } in metres, all three required (a child layer's body replaces the parent's whole).
        // Gate D6: 2r <= height <= the 2 m headroom (a doorway fits the whole body); r <= eye <= height - r (the viewpoint
        // inside the capsule's cylinder); 0 <= muzzle <= height. Every bound is inclusive.
        private static CombatBody Body(string pack, ContentEntry entry)
        {
            var r = new EntryReader(pack, entry);
            SimFix height = r.Fix("height"), eye = r.Fix("eye"), muzzle = r.Fix("muzzle");
            r.Done();
            Within(r, "height", height.Raw, 2 * UnitRadiusRaw, WallSpan.ClearanceRaw);
            Within(r, "eye", eye.Raw, UnitRadiusRaw, height.Raw - UnitRadiusRaw);
            Within(r, "muzzle", muzzle.Raw, 0, height.Raw);
            return new CombatBody(height, eye, muzzle);
        }

        // BLAST-1 "blasts": [ { id, innerMm, outerMm, damage, friendly, walls } ], 1 to 32 kinds with distinct ids (a child layer's
        // array replaces the parent's whole), plus "blastPerTick" (1..64, default 4) and "blastQueue" (1..4096, default 64), which
        // are refused without "blasts". damage 1..32767 health units; outerMm 1..100000; innerMm 0..outerMm - 1.
        private static BlastRules Blasts(string pack, EntryReader cr)
        {
            ContentField field = cr.FieldOf(BlastsField);
            if (field == null)
            {
                foreach (string name in new[] { "blastPerTick", "blastQueue" })
                    if (cr.Has(name)) throw cr.Bad(cr.FieldOf(name), "needs the combat section's " + BlastsField);
                return null;
            }
            field.Used = true;
            if (field.Node.Kind != JsonKind.Array) throw cr.Bad(field, "must be an array");
            int count = field.Node.Items.Count;
            if (count < 1 || count > BlastRules.MaxKinds) throw cr.Bad(field, "has " + count + " entries, allowed 1 to " + BlastRules.MaxKinds);
            var entries = new System.Collections.Generic.List<ContentEntry>();
            EntryMerge.Array(entries, field.Node, "combat." + BlastsField, field.Layer);
            var kinds = new BlastSpec[entries.Count];
            for (int i = 0; i < kinds.Length; i++)
            {
                var r = new EntryReader(pack, entries[i]);
                int outer = r.Int("outerMm", 1, BlastSpec.MaxOuterMm);
                int inner = r.Int("innerMm", 0, BlastSpec.MaxOuterMm - 1);
                if (inner >= outer) throw r.Bad(r.FieldOf("innerMm"), "must be less than outerMm (" + outer + ")");
                int damage = r.Int("damage", 1, BlastSpec.MaxDamage);
                bool friendly = r.Bool("friendly"), walls = r.Bool("walls");
                r.Done();
                kinds[i] = new BlastSpec(entries[i].Id, inner, outer, damage, friendly, walls);
            }
            int perTick = cr.Has("blastPerTick") ? cr.Int("blastPerTick", 1, BlastRules.MaxPerTick) : BlastRules.DefaultPerTick;
            int queue = cr.Has("blastQueue") ? cr.Int("blastQueue", 1, BlastRules.MaxQueue) : BlastRules.DefaultQueue;
            return new BlastRules(kinds, perTick, queue);
        }

        private static void Within(EntryReader r, string name, long raw, long min, long max)
        {
            if (raw >= min && raw <= max) return;
            ContentField f = r.FieldOf(name);
            throw r.Bad(f, "is " + f.Node.Text + " m (" + raw + " raw), allowed " + Metres(min) + " to " + Metres(max) + " m (" + min + " to " + max + " raw)");
        }

        // Raw units as metres with up to four decimals (rounded half away from zero), integer arithmetic only.
        internal static string Metres(long raw)
        {
            long tenThousandths = (Math.Abs(raw) * 10000 + SimFix.Scale / 2) / SimFix.Scale;
            string text = (tenThousandths / 10000).ToString(CultureInfo.InvariantCulture);
            long frac = tenThousandths % 10000;
            if (frac != 0) text += "." + frac.ToString("D4", CultureInfo.InvariantCulture).TrimEnd('0');
            return raw < 0 ? "-" + text : text;
        }

        public override void Build(SectionBuild b)
        {
            ContentEntry combat = b.Single(Key);
            if (combat == null) throw new ContentException("content pack '" + b.Pack + "' has no combat section");
            var cr = new EntryReader(b.Pack, combat);
            SimFix maxHealth = cr.Fix("maxHealth");
            if (maxHealth.Raw <= 0) throw cr.Bad(cr.FieldOf("maxHealth"), "must be greater than 0");
            string resolver = b.Weapons.Length == 0 ? cr.StrOrNull("resolver", out _) : cr.Str("resolver"), selector = cr.Str("selector");
            if (resolver != null && !ContentPolicies.IsResolver(resolver))
                throw cr.Bad(cr.FieldOf("resolver"), "unknown policy '" + resolver + "', allowed: " + ContentPolicies.ResolverIds);
            if (!ContentPolicies.IsSelector(selector))
                throw cr.Bad(cr.FieldOf("selector"), "unknown policy '" + selector + "', allowed: " + ContentPolicies.SelectorIds);
            bool friendlyFire = cr.Bool("friendlyFire"), friendlyFireLocked = cr.Bool("friendlyFireLocked");
            SimFix missRate = cr.Fix("allyCheckMissRate");
            if (missRate.Raw < 0 || missRate.Raw > SimFix.Scale) throw cr.Bad(cr.FieldOf("allyCheckMissRate"), "must be between 0 and 1");
            long holdMilli = cr.Long("allyHoldMilli", 1000, SectionLimits.MaxMilli);
            ContentEntry body = cr.ObjectOrNull("body");
            BlastRules blasts = Blasts(b.Pack, cr);
            cr.Done();
            if (body != null) b.Body = Body(b.Pack, body);
            // PROJ-1: a projectile starts at the muzzle, so a pack with a projectile weapon must have the body.
            for (int i = 0; i < b.ProjectileSpeeds.Length && body == null; i++)
                if (b.ProjectileSpeeds[i] != 0)
                    throw new ContentException("content pack '" + b.Pack + "' weapon '" + b.WeaponIds[i] + "' field '" + WeaponsSection.ProjectileField
                        + "': a projectile weapon needs the combat section's body");
            if (blasts != null) b.Publish(blasts);
            b.MaxHealthRaw = maxHealth.Raw; b.ResolverId = resolver; b.SelectorId = selector;
            b.FriendlyFire = friendlyFire; b.FriendlyFireLocked = friendlyFireLocked; b.AllyCheckMissRaw = (int)missRate.Raw; b.AllyHoldMilli = holdMilli;
        }
    }
}
