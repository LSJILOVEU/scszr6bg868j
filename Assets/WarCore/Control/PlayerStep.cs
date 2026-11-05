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

        public PlayerStep(Simulation sim, IControlTarget controlled)
        {
            this.sim = sim; this.controlled = controlled;
            intents = sim.Entities.Resolve<MoveIntent>();
        }

        public bool TryGetStep(SimContext context, out EntityId unit, out SimVec3 displacement)
        {
            unit = controlled.Current;
            displacement = default;
            if (!sim.Entities.IsAlive(unit) || !sim.Entities.TryGet(intents, unit, out MoveIntent intent)
                || sim.GameplayLife(sim.Entities.StableOf(unit)).State == GameplayLifeState.Dead) return false;
            SimVec3 move = context.Input.Move;
            move = new SimVec3(move.X, default, move.Z);
            if (move.LengthSquared > UnitSquared) move = move.Normalized;
            displacement = move * intent.SpeedPerTick;
            return true;
        }
    }
}
