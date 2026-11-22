using System;
using System.Collections.Generic;
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
        // DOWN-1: combat.downed is one value as well (CombatDownedReader).
        protected override bool IsNested(string field) => base.IsNested(field) || field == MoveSpreadField || field == CombatDownedReader.Field;
        // STANCE-1 combat.stances and BLAST-1 combat.blasts are arrays, each kept whole across layers.
        private static readonly string[] Arrays = { StancesField, BlastsField };
        protected override string[] NestedArrays => Arrays;
        internal const string BlastsField = "blasts";
        internal const string StancesField = "stances";

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

        // STANCE-1 combat.stances: 1..7 objects, highest first, each { id, height, eye, muzzle, speedPermille, spreadPermille, ticks,
        // deny? }. Heights in metres with the body's checks (2r <= height <= 2 m, r <= eye <= height - r, 0 <= muzzle <= height),
        // each height strictly under the one above (the body's for the first). deny: action names (UnitAction, lower case), not
        // move or stance, no repeats; absent means ["dash", "traverse"].
        private static StanceDef[] Stances(string pack, ContentEntry combat, ContentField field, CombatBody? body)
        {
            var r = new EntryReader(pack, combat);
            if (body == null) throw r.Bad(field, "stances need the combat section's body");
            List<JsonNode> items = field.Node.Items;
            if (items.Count < 1 || items.Count > CombatPack.MaxStances)
                throw r.Bad(field, "has " + items.Count + " stances, allowed 1 to " + CombatPack.MaxStances);
            var result = new StanceDef[items.Count];
            var ids = new List<string>();
            long above = body.Value.Height.Raw;
            for (int i = 0; i < items.Count; i++)
            {
                JsonNode item = items[i];
                if (item.Kind != JsonKind.Object) throw r.Bad(field, "entry " + i + " must be an object (line " + item.Line + ")");
                JsonNode idNode = item.Find("id");
                string label = idNode != null && idNode.Kind == JsonKind.String ? idNode.Text : i.ToString(CultureInfo.InvariantCulture);
                var entry = new ContentEntry(SectionKey + "." + StancesField, label, field.Layer);
                foreach (var member in item.Members) entry.Fields.Add(new ContentField(member.Name, member.Value, field.Layer));
                var e = new EntryReader(pack, entry);
                string id = e.Str("id");
                if (id.Length == 0) throw e.Bad(e.FieldOf("id"), "must not be empty");
                if (ids.Contains(id)) throw e.Bad(e.FieldOf("id"), "'" + id + "' is repeated");
                ids.Add(id);
                SimFix height = e.Fix("height"), eye = e.Fix("eye"), muzzle = e.Fix("muzzle");
                int speed = e.Int("speedPermille", StanceDef.MinSpeedPermille, StanceDef.MaxSpeedPermille);
                int spread = e.Int("spreadPermille", StanceDef.MinSpreadPermille, StanceDef.MaxSpreadPermille);
                int ticks = e.Int("ticks", StanceDef.MinTicks, StanceDef.MaxTicks);
                UnitAction deny = e.Has("deny") ? Deny(e) : StanceDef.DefaultDeny;
                e.Done();
                Within(e, "height", height.Raw, 2 * UnitRadiusRaw, WallSpan.ClearanceRaw);
                if (height.Raw >= above)
                    throw e.Bad(e.FieldOf("height"), "is " + Metres(height.Raw) + " m, must be lower than the stance above it (" + Metres(above) + " m)");
                Within(e, "eye", eye.Raw, UnitRadiusRaw, height.Raw - UnitRadiusRaw);
                Within(e, "muzzle", muzzle.Raw, 0, height.Raw);
                above = height.Raw;
                result[i] = new StanceDef(id, new CombatBody(height, eye, muzzle), speed, spread, ticks, deny);
            }
            return result;
        }

        private static readonly string[] ActionNames =
            { "move", "dash", "traverse", "fire", "melee", "block", "throw", "reload", "stance", "interact", "capture" };

        private static UnitAction Deny(EntryReader e)
        {
            ContentField f = e.FieldOf("deny");
            f.Used = true;
            if (f.Node.Kind != JsonKind.Array) throw e.Bad(f, "must be an array of action names");
            UnitAction deny = UnitAction.None;
            foreach (JsonNode name in f.Node.Items)
            {
                int bit = name.Kind == JsonKind.String ? Array.IndexOf(ActionNames, name.Text) : -1;
                if (bit < 0) throw e.Bad(f, "unknown action " + (name.Kind == JsonKind.String ? "'" + name.Text + "'" : "(not a string)") + ", allowed: " + string.Join(", ", ActionNames));
                var action = (UnitAction)(1 << bit);
                if ((action & StanceDef.Deniable) == UnitAction.None) throw e.Bad(f, "may not deny '" + name.Text + "' (a unit must be able to move and change stance)");
                if ((deny & action) != UnitAction.None) throw e.Bad(f, "'" + name.Text + "' is repeated");
                deny |= action;
            }
            return deny;
        }

        // BLAST-1 "blasts": [ { id, innerMm, outerMm, damage, friendly, walls } ], 1 to 32 kinds with distinct ids (a child layer's
        // array replaces the parent's whole), plus "blastPerTick" (1..64, default 4) and "blastQueue" (1..4096, default 64), which
        // are refused without "blasts". damage 1..32767 health units; outerMm 1..100000; innerMm 0..outerMm - 1; DOWN-1 stunTicks
        // 0..600, default 0 (a unit hurt but not downed is stunned that long).
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
                int stun = r.Has("stunTicks") ? r.Int("stunTicks", 0, BlastSpec.MaxStunTicks) : 0;
                r.Done();
                kinds[i] = new BlastSpec(entries[i].Id, inner, outer, damage, friendly, walls, stun);
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
            ContentField moveField = cr.FieldOf(MoveSpreadField);
            if (moveField != null) moveField.Used = true;
            bool manualReload = cr.Has(ManualReloadField) && cr.Bool(ManualReloadField);
            BlastRules blasts = Blasts(b.Pack, cr);
            ContentField stances = cr.FieldOf(StancesField);
            if (stances != null)
            {
                stances.Used = true;
                if (stances.Node.Kind != JsonKind.Array) throw cr.Bad(stances, "must be an array");
            }
            ContentField downed = cr.FieldOf(CombatDownedReader.Field);
            if (downed != null) downed.Used = true;
            cr.Done();
            MoveSpread moveSpread = moveField == null ? null : CombatAimReader.MoveSpread(b.Pack, moveField, b.WeaponIds);
            if (moveSpread != null || manualReload) b.Publish(new CombatAim(moveSpread, manualReload));
            if (body != null) b.Body = Body(b.Pack, body);
            if (stances != null) b.Stances = Stances(b.Pack, combat, stances, b.Body);
            // PROJ-1: a projectile starts at the muzzle, so a pack with a projectile weapon must have the body.
            for (int i = 0; i < b.ProjectileSpeeds.Length && body == null; i++)
                if (b.ProjectileSpeeds[i] != 0)
                    throw new ContentException("content pack '" + b.Pack + "' weapon '" + b.WeaponIds[i] + "' field '" + WeaponsSection.ProjectileField
                        + "': a projectile weapon needs the combat section's body");
            if (blasts != null) b.Publish(blasts);
            if (downed != null) b.Publish(CombatDownedReader.Read(b.Pack, downed, b.Stances));
            b.MaxHealthRaw = maxHealth.Raw; b.ResolverId = resolver; b.SelectorId = selector;
            b.FriendlyFire = friendlyFire; b.FriendlyFireLocked = friendlyFireLocked; b.AllyCheckMissRaw = (int)missRate.Raw; b.AllyHoldMilli = holdMilli;
        }
    }
}
