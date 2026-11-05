using WarCore.Combat;
using WarCore.Sim;

namespace WarCore.Content.Rwr
{
    public sealed class RwrHitResolver : IHitResolver
    {
        public ulong PolicyKey => 0x5257524849540001UL;
        public long DamageRaw(in WeaponDef weapon, long falloffRaw, int rollRaw, long maxHealthRaw) =>
            rollRaw < weapon.EffectRaw * falloffRaw / SimFix.Scale ? maxHealthRaw : 0;
    }

    // The RWR numbers live in Assets/WarCore/Content/Rwr/Data/*.json (values, PF references and PLACEHOLDER notes:
    // docs/content-data.md). Unit types: soldier-1 carries weapon-1 and soldier-2 weapon-2 (what teams 1 and 2 used to index).
    public static class RwrPack
    {
        public const string M5Layer = "rwr", M6Layer = "rwr-m6", SoldierTeam1 = "rwr.soldier-1", SoldierTeam2 = "rwr.soldier-2";
        public static ContentPack Load(string layerId) => ContentLoader.Load(FileContentSource.Rwr(), layerId);
        public static CombatPack Create() => Load(M5Layer).BuildCombat();
        public static CombatPack CreateM6() => Load(M6Layer).BuildCombat();
    }
}
