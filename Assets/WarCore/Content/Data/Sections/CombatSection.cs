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
        // AIM-1: combat.moveSpread is an object too (bands and optional per-weapon tables), kept whole across layers.
        internal const string MoveSpreadField = "moveSpread", ManualReloadField = "manualReload";
        protected override bool IsNested(string field) => base.IsNested(field) || field == MoveSpreadField;

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
            ContentField moveField = cr.FieldOf(MoveSpreadField);
            if (moveField != null) moveField.Used = true;
            bool manualReload = cr.Has(ManualReloadField) && cr.Bool(ManualReloadField);
            cr.Done();
            MoveSpread moveSpread = moveField == null ? null : CombatAimReader.MoveSpread(b.Pack, moveField, b.WeaponIds);
            if (moveSpread != null || manualReload) b.Publish(new CombatAim(moveSpread, manualReload));
            if (body != null) b.Body = Body(b.Pack, body);
            // PROJ-1: a projectile starts at the muzzle, so a pack with a projectile weapon must have the body.
            for (int i = 0; i < b.ProjectileSpeeds.Length && body == null; i++)
                if (b.ProjectileSpeeds[i] != 0)
                    throw new ContentException("content pack '" + b.Pack + "' weapon '" + b.WeaponIds[i] + "' field '" + WeaponsSection.ProjectileField
                        + "': a projectile weapon needs the combat section's body");
            b.MaxHealthRaw = maxHealth.Raw; b.ResolverId = resolver; b.SelectorId = selector;
            b.FriendlyFire = friendlyFire; b.FriendlyFireLocked = friendlyFireLocked; b.AllyCheckMissRaw = (int)missRate.Raw; b.AllyHoldMilli = holdMilli;
        }
    }
}
