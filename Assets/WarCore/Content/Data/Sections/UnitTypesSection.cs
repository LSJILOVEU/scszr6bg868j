using System;

namespace WarCore.Content
{
    // Unit types build after the weapon, melee and block sections they reference (table order).
    internal sealed class UnitTypesSection : ArraySection
    {
        internal const string SectionKey = "unitTypes";
        internal UnitTypesSection() : base(SectionKey, "unit type") { }

        public override void Build(SectionBuild b)
        {
            var entries = b.Entries(Key);
            var unitIds = new string[entries.Count];
            var unitWeapons = new int[entries.Count];
            var unitMelee = new int[entries.Count];
            var unitBlock = new int[entries.Count];
            var unitArchetypes = new int[entries.Count];
            var unitSpeeds = new long?[entries.Count];
            for (int i = 0; i < unitIds.Length; i++)
            {
                ContentEntry e = entries[i];
                var r = new EntryReader(b.Pack, e);
                unitIds[i] = e.Id;
                string weaponId = r.Str("weapon");
                ContentField field = r.FieldOf("weapon");
                string meleeId = r.StrOrNull("melee", out ContentField meleeField);
                string blockId = r.StrOrNull("block", out ContentField blockField);
                unitSpeeds[i] = r.Has("speed") ? r.Long("speed", 1, ContentPack.MaxUnitSpeedMmPerSec) : (long?)null;
                unitArchetypes[i] = r.Has("archetype") ? r.Int("archetype") : 0;
                r.Done();
                // An empty weapon id means no gun; such a unit type must carry a melee spec.
                int index = weaponId.Length == 0 ? -1 : Array.IndexOf(b.WeaponIds, weaponId);
                if (index < 0 && weaponId.Length != 0) throw r.Bad(field, "referenced weapon '" + weaponId + "' does not exist");
                int swing = -1;
                if (!string.IsNullOrEmpty(meleeId))
                {
                    swing = Array.IndexOf(b.MeleeIds, meleeId);
                    if (swing < 0) throw r.Bad(meleeField, "referenced melee '" + meleeId + "' does not exist");
                }
                if (index < 0 && swing < 0) throw r.Bad(field, "a unit type without a weapon needs a melee");
                int block = blockId == null ? -1 : Array.IndexOf(b.BlockIds, blockId);
                if (blockId != null && block < 0) throw r.Bad(blockField, "referenced block '" + blockId + "' does not exist");
                unitBlock[i] = block;
                unitWeapons[i] = index; unitMelee[i] = swing;
            }
            b.UnitIds = unitIds; b.UnitWeapons = unitWeapons; b.UnitMelee = unitMelee;
            b.UnitBlock = unitBlock; b.UnitArchetypes = unitArchetypes; b.UnitSpeeds = unitSpeeds;
        }
    }
}
