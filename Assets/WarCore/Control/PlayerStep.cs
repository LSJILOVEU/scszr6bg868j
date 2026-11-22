using WarCore.Interim;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Control
{
    public sealed class PlayerStep : IControlledStep
    {
        private readonly Simulation sim;
        private readonly IControlTarget controlled;
        private readonly ComponentType<MoveIntent> intents;
        private static readonly SimWide UnitSquared = SimVec3.Metres(1, 0, 0).LengthSquared;
        // STANCE-1: the world's speed line, or null (the unit's own speed, as before).
        private SpeedLine speed;

        public PlayerStep(Simulation sim, IControlTarget controlled)
        {
            this.sim = sim; this.controlled = controlled;
            intents = sim.Entities.Resolve<MoveIntent>();
        }

        // The player's step takes the unit's speed through the line's gears (no motion model: the player has no inertia).
        public void BindSpeed(SpeedLine line) { speed = line ?? throw new System.ArgumentNullException(nameof(line)); }

        public bool TryGetStep(SimContext context, out EntityId unit, out SimVec3 displacement)
        {
            unit = controlled.Current;
            displacement = default;
            if (!sim.Entities.IsAlive(unit) || !sim.Entities.TryGet(intents, unit, out MoveIntent intent)
                || sim.GameplayLife(sim.Entities.StableOf(unit)).State == GameplayLifeState.Dead) return false;
            SimVec3 move = context.Input.Move;
            move = new SimVec3(move.X, default, move.Z);
            if (move.LengthSquared > UnitSquared) move = move.Normalized;
            displacement = move * (speed == null ? intent.SpeedPerTick : speed.Wanted(unit, intent.SpeedPerTick));
            return true;
        }
    }
}
