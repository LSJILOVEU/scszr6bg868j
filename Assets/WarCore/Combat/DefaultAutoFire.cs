using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Combat
{
    // The built-in decision: shoot the pack selector's target, unless a teammate is first on the line and the miss roll does not allow it.
    // Stateless after construction. It stays in this assembly: the combat tests build a CombatSystem directly, and Combat cannot reference Battle.
    public sealed class DefaultAutoFire : IAutoFire
    {
        private readonly Simulation sim;
        private readonly RandomStreamId allyStream;

        internal DefaultAutoFire(Simulation sim, RandomStreamId allyStream) { this.sim = sim; this.allyStream = allyStream; }

        public void Sense(CombatSystem combat, EntityId shooter, in Combatant unit, in Position position, long tick) { }

        public AutoFireAction TryAim(CombatSystem combat, EntityId shooter, in Combatant unit, in Position position,
            in WeaponDef weapon, long tick, out SimVec3 direction)
        {
            if (combat.Pack.Selector.TrySelect(combat, shooter, weapon.FalloffEnd, out EntityId target))
                return AimAt(combat, shooter, in position, target, out direction);
            direction = default;
            return AutoFireAction.Pass;
        }

        // The decision for a target already chosen: the heading to it, then the ally check with its miss roll.
        public AutoFireAction AimAt(CombatSystem combat, EntityId shooter, in Position position, EntityId target, out SimVec3 direction)
        {
            SimVec3 origin = position.Value;
            direction = CombatSystem.Horizontal(combat.Point(target) - origin);
            // Zero-direction rule: a target standing on the shooter gives no heading, so the shot goes along +X
            // and takes the same ally check, spread, wall and teammate handling as any other ray.
            // PLACEHOLDER: +X is arbitrary; switch to the unit's facing once units have one (block 5 C1, T1b/H1).
            if (direction.LengthSquared.Sign == 0) direction = new SimVec3(SimFix.FromInt(1), default, default);
            if (combat.AllyBlocks(shooter, origin, direction) && sim.Rng.NextInt(allyStream, 65536) >= combat.Pack.AllyCheckMissRaw)
                return AutoFireAction.HoldFire;
            return AutoFireAction.Fire;
        }
    }
}
