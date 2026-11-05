using System;
using System.Collections.Generic;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Sim;

namespace WarCore.Content
{
    public readonly struct MatchRules
    {
        public readonly int CaptureAdvantageNum, CaptureAdvantageDen, CaptureTicks, DecayPerTick, IntervalTicks, ThinkInterval, MaxTicks, GridColumns, GridRows;
        public readonly bool CaptureStrict;
        public readonly InterruptMode Interrupt;
        public readonly SimFix Sight, GridSpacing;

        public MatchRules(int num, int den, bool strict, int captureTicks, InterruptMode interrupt, int decayPerTick,
            int intervalTicks, int thinkInterval, int maxTicks, SimFix sight, int gridColumns, int gridRows, SimFix gridSpacing)
        {
            CaptureAdvantageNum = num; CaptureAdvantageDen = den; CaptureStrict = strict; CaptureTicks = captureTicks;
            Interrupt = interrupt; DecayPerTick = decayPerTick; IntervalTicks = intervalTicks; ThinkInterval = thinkInterval;
            MaxTicks = maxTicks; Sight = sight; GridColumns = gridColumns; GridRows = gridRows; GridSpacing = gridSpacing;
        }
    }

    // A fully resolved content pack: inheritance applied, every value converted to its Sim type once.
    // Weapons keep their merged order because CombatPack hashes the array in order and units refer to it by index.
    public sealed class ContentPack
    {
        private readonly string[] weaponIds;
        private readonly WeaponDef[] weapons;
        private readonly string[] unitTypeIds;
        private readonly int[] unitTypeWeapons;
        private readonly string resolverId, selectorId;
        private readonly long maxHealthRaw;
        private readonly MatchRules? match;
        private readonly bool friendlyFire, friendlyFireLocked;
        private readonly int allyCheckMissRaw;
        private readonly long allyHoldMilli;
        private readonly string[] meleeIds;
        private readonly MeleeSpec[] melee;
        private readonly int[] unitTypeMelee;

        public string Id { get; }
        // Optional terrain rules (WP-22); null when the pack has none. Not part of any PolicyDigest.
        public TerrainRules Terrain { get; }

        internal ContentPack(string id, string[] weaponIds, WeaponDef[] weapons, string[] unitTypeIds, int[] unitTypeWeapons,
            long maxHealthRaw, string resolverId, string selectorId, MatchRules? match,
            bool friendlyFire, bool friendlyFireLocked, int allyCheckMissRaw, long allyHoldMilli,
            string[] meleeIds = null, MeleeSpec[] melee = null, int[] unitTypeMelee = null, TerrainRules terrain = null)
        {
            Terrain = terrain;
            this.meleeIds = meleeIds ?? Array.Empty<string>(); this.melee = melee ?? Array.Empty<MeleeSpec>();
            this.unitTypeMelee = unitTypeMelee ?? NoMelee(unitTypeIds.Length);
            Id = id; this.weaponIds = weaponIds; this.weapons = weapons; this.unitTypeIds = unitTypeIds;
            this.unitTypeWeapons = unitTypeWeapons; this.maxHealthRaw = maxHealthRaw;
            this.resolverId = resolverId; this.selectorId = selectorId; this.match = match;
            this.friendlyFire = friendlyFire; this.friendlyFireLocked = friendlyFireLocked;
            this.allyCheckMissRaw = allyCheckMissRaw; this.allyHoldMilli = allyHoldMilli;
        }

        private static int[] NoMelee(int count)
        { var none = new int[count]; for (int i = 0; i < count; i++) none[i] = -1; return none; }

        public int WeaponCount => weapons.Length;
        public int MeleeCount => melee.Length;
        public string MeleeId(int index) => meleeIds[index];
        public int UnitTypeCount => unitTypeIds.Length;
        public string UnitTypeId(int index) => unitTypeIds[index];
        public string WeaponId(int index) => weaponIds[index];
        public MatchRules Match => match ?? throw new ContentException("content pack '" + Id + "' has no match section");

        public int WeaponIndex(string weaponId)
        {
            int index = Array.IndexOf(weaponIds, weaponId);
            if (index < 0) throw new ContentException("content pack '" + Id + "' has no weapon '" + weaponId + "'");
            return index;
        }

        // Index into CombatPack.Weapons for the weapon a unit type carries.
        public int WeaponOfUnitType(string unitTypeId)
        {
            int index = Array.IndexOf(unitTypeIds, unitTypeId);
            if (index < 0) throw new ContentException("content pack '" + Id + "' has no unit type '" + unitTypeId + "'");
            return unitTypeWeapons[index];
        }

        // Index into CombatPack.Melee for the swing a unit type carries, or -1 when it has none.
        public int MeleeOfUnitType(string unitTypeId)
        {
            int index = Array.IndexOf(unitTypeIds, unitTypeId);
            if (index < 0) throw new ContentException("content pack '" + Id + "' has no unit type '" + unitTypeId + "'");
            return unitTypeMelee[index];
        }

        // The one place a player's friendly-fire request meets the pack's lock: a locked pack ignores the request.
        // The returned CombatPack is immutable, so the setting cannot change during a match.
        public CombatPack BuildCombat(bool? requestedFriendlyFire = null)
        {
            SimFix sight = selectorId == ContentPolicies.SightLimitedNearest ? Match.Sight : default;
            bool effective = friendlyFireLocked ? friendlyFire : requestedFriendlyFire ?? friendlyFire;
            return new CombatPack(weapons, ContentPolicies.Resolver(resolverId), ContentPolicies.Selector(selectorId, sight), maxHealthRaw,
                effective, friendlyFireLocked, allyCheckMissRaw, allyHoldMilli, melee.Length == 0 ? null : melee);
        }

        // Control points and spawn anchors are map data and stay with the caller.
        public MatchPack BuildMatch(ControlPointDef[] points, SpawnAnchor[] bases)
        {
            MatchRules m = Match;
            try
            {
                return new MatchPack(points, bases, m.CaptureAdvantageNum, m.CaptureAdvantageDen, m.CaptureStrict, m.CaptureTicks,
                    m.Interrupt, m.DecayPerTick, m.IntervalTicks, m.ThinkInterval, m.MaxTicks, m.Sight, m.GridColumns, m.GridRows, m.GridSpacing);
            }
            catch (ArgumentException e)
            {
                throw new ContentException("content pack '" + Id + "' match section is invalid: " + e.Message);
            }
        }
    }
}
