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
        public int Order => 29;
        public string StateTypeId => "control.player-fire";
        public int StateVersion => 1;

        public PlayerFire(Simulation sim, CombatSystem combat, IControlTarget controlled)
        {
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
                || !sim.Entities.TryGet(combatants, unit, out Combatant state)
                || (state.Weapon < 0 && !combat.CanMelee(unit)) || !context.Input.PrimaryAttack) return;
            // Position is the capsule centre. Keep its Y; do not add a muzzle offset.
            combat.Submit(context, FireIntent.Aimed(stable,
                new SimVec3(context.Input.Aim.X, position.Value.Y, context.Input.Aim.Z)));
        }

        public void SaveState(SimStateWriter writer) { }
        public void LoadState(SimStateReader reader) { }
        public void WriteState(StateHash hash) { hash.Add(StateTypeId); hash.Add(StateVersion); }
    }
}
