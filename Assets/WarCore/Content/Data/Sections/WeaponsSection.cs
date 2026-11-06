using System;
using WarCore.Combat;
using WarCore.Sim;

namespace WarCore.Content
{
    internal sealed class WeaponsSection : ArraySection
    {
        internal const string SectionKey = "weapons";
        internal WeaponsSection() : base(SectionKey, "weapon") { }

        public override void Build(SectionBuild b)
        {
            var entries = b.Entries(Key);
            var weaponIds = new string[entries.Count];
            var weapons = new WeaponDef[entries.Count];
            for (int i = 0; i < weapons.Length; i++)
            {
                ContentEntry e = entries[i];
                var r = new EntryReader(b.Pack, e);
                weaponIds[i] = e.Id;
                long interval = r.Long("fireIntervalMilli", 1, SectionLimits.MaxMilli), reload = r.Long("reloadMilli", 1, SectionLimits.MaxMilli), effect = r.Long("effectRaw", 0, SectionLimits.MaxEffectRaw);
                int magazine = r.Int("magazine");
                SimFix start = r.Fix("falloffStart"), end = r.Fix("falloffEnd");
                // Spread is in thousandths of a degree; all four zero means the weapon has no spread.
                int spreadBase = r.Int("spreadBase", 0, SpreadSpec.MaxLegalMilliDegrees), spreadGrowth = r.Int("spreadGrowth", 0, SpreadSpec.MaxLegalMilliDegrees);
                int spreadRecover = r.Int("spreadRecoverPerSec", 0, SpreadSpec.MaxLegalMilliDegrees), spreadMax = r.Int("spreadMax", 0, SpreadSpec.MaxLegalMilliDegrees);
                if (spreadMax < spreadBase) throw r.Bad(r.FieldOf("spreadMax"), "must be at least spreadBase");
                r.Done();
                try { weapons[i] = new WeaponDef(interval, magazine, reload, start, end, effect, new SpreadSpec(spreadBase, spreadGrowth, spreadRecover, spreadMax)); }
                catch (ArgumentOutOfRangeException ex)
                {
                    throw new ContentException("content pack '" + b.Pack + "' layer '" + (r.FieldOf(ex.ParamName)?.Layer ?? e.FirstLayer) + "' " + e.Label + " field '" + ex.ParamName + "' is out of its legal range");
                }
            }
            b.WeaponIds = weaponIds; b.Weapons = weapons;
        }
    }
}
