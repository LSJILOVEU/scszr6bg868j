using WarCore.Interim;
using WarCore.Combat;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Control
{
    // Queries/29: after movement has committed, before Combat consumes intents at order 30.
    public sealed class PlayerFire : IRetainedGameplaySystem
    {
        private readonly Simulation sim;
        private readonly CombatSystem combat;
        private readonly IControlTarget controlled;
        private readonly ComponentType<Position> positions;
        private readonly ComponentType<Combatant> combatants;
        private readonly ComponentType<UnitFacing>? facings;
        public int Order => 29;
        public string StateTypeId => "control.player-fire";
        public int StateVersion => 1;

        public PlayerFire(Simulation sim, CombatSystem combat, IControlTarget controlled, ComponentType<UnitFacing>? facings = null)
        {
            this.facings = facings;
            this.sim = sim; this.combat = combat; this.controlled = controlled;
            positions = sim.Entities.Resolve<Position>();
            combatants = sim.Entities.Resolve<Combatant>();
        }

        public void Step(SimContext context)
        {
            // Fallback can change Current this tick. Never cache the entity or its stable identity.
            EntityId unit = controlled.Current;
            if (!sim.Entities.IsAlive(unit)) return;
            StableId stable = sim.Entities.StableOf(unit);
            if (sim.GameplayLife(stable).State == GameplayLifeState.Dead
                || !sim.Entities.TryGet(positions, unit, out Position position)
                || !sim.Entities.TryGet(combatants, unit, out Combatant state)) return;
            // Holding the block input keeps a hold toward the aim point (the facing when the aim is on the unit); letting go releases it.
            if (combat.CanBlock(unit))
            {
                var aimed = new SimVec3(context.Input.Aim.X - position.Value.X, default, context.Input.Aim.Z - position.Value.Z);
                if (!context.Input.Block) combat.Release(unit);
                else if (aimed.LengthSquared.Sign != 0) combat.Hold(unit, aimed);
                else if (facings != null)
                {
                    UnitFacing facing = sim.Entities.Get(facings.Value, unit);
                    combat.Hold(unit, new SimVec3(facing.X, default, facing.Z));
                }
            }
            if ((state.Weapon < 0 && !combat.CanMelee(unit)) || !context.Input.PrimaryAttack) return;
            // The aimed shot also turns the unit toward the aim point; an aim on top of the unit keeps its facing.
            if (facings != null)
            {
                var toward = new SimVec3(context.Input.Aim.X - position.Value.X, default, context.Input.Aim.Z - position.Value.Z);
                if (toward.LengthSquared.Sign != 0)
                {
                    SimVec3 direction = toward.Normalized;
                    sim.Entities.Set(facings.Value, unit, new UnitFacing(direction.X, direction.Z));
                }
            }
            // Position is the capsule centre. Keep its Y; do not add a muzzle offset.
            combat.Submit(context, FireIntent.Aimed(stable,
                new SimVec3(context.Input.Aim.X, position.Value.Y, context.Input.Aim.Z)));
        }

        public void SaveState(SimStateWriter writer) { }
        public void LoadState(SimStateReader reader) { }
        public void WriteState(StateHash hash) { hash.Add(StateTypeId); hash.Add(StateVersion); }
    }
}
