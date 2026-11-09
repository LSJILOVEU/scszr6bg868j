using System;
using WarCore.Sim;

namespace WarCore.Combat
{
    // A rule outside combat that can shield a unit from combat damage for a while (for example spawn protection).
    public interface IDamageGuard
    {
        bool Shields(StableId victim, long tick);
    }

    public sealed partial class CombatSystem
    {
        private IDamageGuard guard;

        // Bound once while the world is assembled. A shielded victim of a ranged hit or a melee strike takes zero damage; the roll, the
        // resolver call, the damage command and the records still happen. Combat keeps no state for the guard: the guard owns and saves
        // its own state, and a restored world binds it again when it is assembled. Shields must be a pure, deterministic read.
        public void SetDamageGuard(IDamageGuard value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (guard != null) throw new InvalidOperationException("A damage guard is already set.");
            guard = value;
        }

        private bool Shielded(StableId victim, long tick) => guard != null && guard.Shields(victim, tick);
    }
}
