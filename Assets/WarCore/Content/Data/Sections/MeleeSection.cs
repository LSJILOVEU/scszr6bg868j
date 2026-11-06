using System;
using WarCore.Combat;
using WarCore.Sim;

namespace WarCore.Content
{
    // Melee swings (WP-20): a separate table, so a pack without any keeps every weapon and digest it had.
    internal sealed class MeleeSection : ArraySection
    {
        internal const string SectionKey = "melee";
        internal MeleeSection() : base(SectionKey, "melee") { }

        public override void Build(SectionBuild b)
        {
            var entries = b.Entries(Key);
            var meleeIds = new string[entries.Count];
            var melee = new MeleeSpec[entries.Count];
            for (int i = 0; i < melee.Length; i++)
            {
                ContentEntry e = entries[i];
                var r = new EntryReader(b.Pack, e);
                meleeIds[i] = e.Id;
                int windup = r.Int("windupTicks"), active = r.Int("activeTicks"), recovery = r.Int("recoveryTicks");
                SimFix radius = r.Fix("radius"), halfHeight = r.Fix("halfHeight"), damage = r.Fix("damage"), engage = r.Fix("engageRange");
                int cosine = r.Int("cosHalfAnglePermille"), targets = r.Int("maxTargets");
                // Optional: a thrust (a straight narrow hit instead of the sector). Absent = a sector, exactly as before.
                SimFix lineHalfWidth = r.Has("lineHalfWidth") ? r.Fix("lineHalfWidth") : default;
                r.Done();
                try { melee[i] = new MeleeSpec(windup, active, recovery, radius, cosine, halfHeight, damage, targets, engage, lineHalfWidth); }
                catch (ArgumentOutOfRangeException ex)
                {
                    // The constructor parameters are named exactly like the JSON fields.
                    string name = ex.ParamName;
                    throw new ContentException("content pack '" + b.Pack + "' layer '" + (r.FieldOf(name)?.Layer ?? e.FirstLayer) + "' " + e.Label + " field '" + name + "' is out of its legal range");
                }
            }
            b.MeleeIds = meleeIds; b.Melee = melee;
        }
    }
}
