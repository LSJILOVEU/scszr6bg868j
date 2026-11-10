using System;
using System.Collections.Generic;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Sim;

namespace WarCore.Content
{
    // The state sections build into, one step after another. The values of the seven shipped sections have fields here;
    // a section added later publishes its own object with Publish and the pack hands it out through ContentPack.Section.
    internal sealed class SectionBuild
    {
        private readonly MergedLayers merged;
        private readonly List<object> published = new List<object>();
        internal readonly string Pack;

        internal string[] WeaponIds = Array.Empty<string>(), MeleeIds = Array.Empty<string>(), UnitIds = Array.Empty<string>();
        internal WeaponDef[] Weapons = Array.Empty<WeaponDef>();
        internal MeleeSpec[] Melee = Array.Empty<MeleeSpec>();
        internal string[] BlockIds = Array.Empty<string>();
        internal BlockSpec[] Block = Array.Empty<BlockSpec>();
        internal int[] UnitBlock = Array.Empty<int>(), UnitArchetypes = Array.Empty<int>();
        internal long?[] UnitSpeeds = Array.Empty<long?>();
        internal int[] UnitWeapons = Array.Empty<int>(), UnitMelee = Array.Empty<int>();
        internal long MaxHealthRaw, AllyHoldMilli;
        internal string ResolverId, SelectorId;
        internal bool FriendlyFire, FriendlyFireLocked;
        internal int AllyCheckMissRaw;
        internal MatchRules? Rules;
        internal string[] RuleIds;
        internal int SlopeNumerator = HeightField.MaxSlopeNumerator, SlopeDenominator = HeightField.MaxSlopeDenominator;
        internal TerrainRules Terrain;

        internal SectionBuild(string pack, MergedLayers merged) { Pack = pack; this.merged = merged; }

        internal List<ContentEntry> Entries(string key) => merged.Of(key);

        // The one entry of an object section, or null when no layer wrote it.
        internal ContentEntry Single(string key)
        {
            List<ContentEntry> entries = merged.Of(key);
            return entries.Count == 0 ? null : entries[0];
        }

        internal void Publish(object value) => published.Add(value);

        internal ContentPack ToPack() => new ContentPack(Pack, WeaponIds, Weapons, UnitIds, UnitWeapons, MaxHealthRaw, ResolverId, SelectorId, Rules,
            FriendlyFire, FriendlyFireLocked, AllyCheckMissRaw, AllyHoldMilli, Block, UnitBlock, UnitArchetypes, UnitSpeeds,
            MeleeIds, Melee, UnitMelee, Terrain, published.ToArray(), RuleIds);
    }
}
