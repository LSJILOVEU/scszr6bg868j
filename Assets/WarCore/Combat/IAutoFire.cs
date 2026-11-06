using WarCore.Sim;

namespace WarCore.Combat
{
    public enum AutoFireAction : byte
    {
        // Do not fire this tick.
        Pass = 0,
        // Fire along the returned direction; the shared spread, wall and teammate handling still applies.
        Fire,
        // Do not fire, and keep the weapon unready for the pack's ally-hold time.
        HoldFire
    }

    // The fire decision for a unit that has no explicit fire intent and is not the player. CombatSystem calls it from Step;
    // an implementation must not allocate, and must take every random number from a stream the combat system registered.
    public interface IAutoFire
    {
        // Called once per tick for every living, uncontrolled unit with a Combatant and Position and no fire intent this tick.
        // It runs before the melee step and the cooldown guard, so units that are swinging, cooling down or unarmed receive it too.
        void Sense(CombatSystem combat, EntityId shooter, in Combatant unit, in Position position, long tick);

        // Called only for a unit that has a ready weapon, is not on a melee swing, has no fire intent and is not the player.
        // direction is only read when the result is Fire.
        AutoFireAction TryAim(CombatSystem combat, EntityId shooter, in Combatant unit, in Position position,
            in WeaponDef weapon, long tick, out SimVec3 direction);
    }
}
