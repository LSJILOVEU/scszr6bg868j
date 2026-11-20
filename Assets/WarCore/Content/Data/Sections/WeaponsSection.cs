using System;
using WarCore.Combat;
using WarCore.Sim;

namespace WarCore.Content
{
    internal sealed class WeaponsSection : ArraySection
    {
        internal const string SectionKey = "weapons";
        internal const string ProjectileField = "projectileSpeed";
        internal WeaponsSection() : base(SectionKey, "weapon") { }

        public override void Build(SectionBuild b)
        {
            var entries = b.Entries(Key);
            var weaponIds = new string[entries.Count];
            var weapons = new WeaponDef[entries.Count];
            var speeds = new int[entries.Count];
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
                // PROJ-1: optional, mm/s; absent or 0 = the shot lands on the tick it is fired.
                int speed = r.Has(ProjectileField) ? r.Int(ProjectileField, 0, ProjectileSpec.MaxSpeed) : 0;
                if (speed != 0 && speed < ProjectileSpec.MinSpeed)
                    throw r.Bad(r.FieldOf(ProjectileField), "must be 0 or between " + ProjectileSpec.MinSpeed + " and " + ProjectileSpec.MaxSpeed);
                if (speed != 0 && end.Raw > 0 && ProjectileSpec.FlightsOf(end, ProjectileSpec.StepOf(speed)) > ProjectileSpec.MaxFlights)
                    throw r.Bad(r.FieldOf(ProjectileField), "takes " + ProjectileSpec.FlightsOf(end, ProjectileSpec.StepOf(speed))
                        + " ticks to fly falloffEnd, at most " + ProjectileSpec.MaxFlights + " allowed");
                speeds[i] = speed;
                r.Done();
                try { weapons[i] = new WeaponDef(interval, magazine, reload, start, end, effect, new SpreadSpec(spreadBase, spreadGrowth, spreadRecover, spreadMax)); }
                catch (ArgumentOutOfRangeException ex)
                {
                    throw new ContentException("content pack '" + b.Pack + "' layer '" + (r.FieldOf(ex.ParamName)?.Layer ?? e.FirstLayer) + "' " + e.Label + " field '" + ex.ParamName + "' is out of its legal range");
                }
            }
            b.WeaponIds = weaponIds; b.Weapons = weapons; b.ProjectileSpeeds = speeds;
        }
    }
}
