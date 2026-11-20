namespace WarCore.Combat
{
    // Content id "damage": a hit takes EffectRaw of health, scaled by falloff (EffectRaw * falloffRaw / Scale, toward
    // zero), with no roll. EffectRaw is in health's raw units, so 34 health is 34 * Scale. For rules where hits wound and
    // several hits kill; the shot itself still has to land (spread, walls and friends decide that).
    public sealed class DamageResolver : IHitResolver
    {
        public ulong PolicyKey => 0x44414D4147450001UL;
        public long DamageRaw(in WeaponDef weapon, long falloffRaw, int rollRaw, long maxHealthRaw) =>
            weapon.EffectRaw * falloffRaw / WarCore.Sim.SimFix.Scale;
    }
}
