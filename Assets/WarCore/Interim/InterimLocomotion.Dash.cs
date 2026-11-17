using System;
using WarCore.Sim;

namespace WarCore.Interim
{
    // Dash: a short, fast, straight move a unit is asked for through TryDash (no input or AI asks yet). Each dash step goes
    // through the controlled-step checks (walls swept along the step, blocked cells, units swept along the step) with their
    // axis fallback. A dash ignores inertia: it does not ask the motion model, so the unit's next ordinary step starts from rest.
    // While a unit dashes, its order and the player's step for it wait. A step that cannot move at all ends the dash.
    public sealed partial class InterimLocomotion
    {
        private ComponentType<DashSpec> dashSpecs;
        private ComponentType<DashState> dashStates;
        private SimClock dashClock;

        public void BindDash(ComponentType<DashSpec> specs, ComponentType<DashState> states, SimClock clock)
        {
            if (!crowd || terrain != null) throw new InvalidOperationException("Dash needs a crowd world without terrain.");
            dashClock = clock ?? throw new ArgumentNullException(nameof(clock));
            dashSpecs = specs;
            dashStates = states;
        }

        // Starts a dash along the horizontal part of direction (its length does not matter) and returns true; its first step
        // is taken on the next tick the locomotion runs after this tick. Refused (false, nothing written) when the world has no
        // dash, the unit has no dash or no body, the direction has no horizontal part, or the clock has not reached the unit's ready tick.
        public bool TryDash(EntityId unit, SimVec3 direction)
        {
            if (dashClock == null || !entities.TryGet(dashSpecs, unit, out DashSpec spec)
                || !entities.TryGet(dashStates, unit, out DashState state) || !entities.TryGet(positions, unit, out _))
                return false;
            long now = dashClock.Tick;
            if (now < state.Ready) return false;
            var flat = new SimVec3(direction.X, default, direction.Z);
            if (flat.LengthSquared.Sign == 0) return false;
            SimVec3 total = flat.Normalized * spec.Distance;
            entities.Set(dashStates, unit, new DashState(total.X, total.Z, now, spec.Ticks, now + spec.Cooldown));
            return true;
        }

        // True while the unit's dash still has steps to take after the current tick.
        public bool IsDashing(EntityId unit) =>
            dashClock != null && entities.TryGet(dashStates, unit, out DashState state) && state.StepAt(dashClock.Tick + 1) > 0;

        private bool DashesNow(int i) =>
            dashClock != null && entities.TryGet(dashStates, provisional[i].Unit, out DashState state) && state.StepAt(dashClock.Tick) > 0;

        // Step k of n moves floor(T * k / n) - floor(T * (k - 1) / n) raw units on each axis (floor toward negative infinity),
        // so the n steps add up to the total exactly. |T| <= 100 * 2^16 < 2^23 and k <= 300 < 2^9, so T * k fits in 32 bits.
        internal static long DashPart(long total, int k, int n) => FloorDiv(total * k, n) - FloorDiv(total * (k - 1), n);

        private static long FloorDiv(long a, long b)
        {
            long q = a / b;
            return (a % b != 0 && (a < 0) != (b < 0)) ? q - 1 : q;
        }

        // Takes this tick's dash step for member i when it has one; false when the member does not dash now.
        private bool StepDash(SimContext context, int i)
        {
            EntityId unit = provisional[i].Unit;
            if (dashClock == null || !entities.TryGet(dashStates, unit, out DashState state)) return false;
            int k = state.StepAt(dashClock.Tick);
            if (k == 0) return false;
            var step = new SimVec3(SimFix.FromRaw(DashPart(state.TotalX.Raw, k, state.Ticks)), default,
                SimFix.FromRaw(DashPart(state.TotalZ.Raw, k, state.Ticks)));
            SimVec3 before = provisional[i].Point;
            StepControlled(context, i, step);
            bool blocked = step.LengthSquared.Sign != 0 && provisional[i].Point.Equals(before);
            if (blocked) entities.Set(dashStates, unit, state.EndedAfter(k - 1));
            if (blocked || k == state.Ticks) Rejoin(unit, provisional[i].Point);
            return true;
        }

        // After a dash, a unit with an order heads for it again from where the dash left it.
        private void Rejoin(EntityId unit, SimVec3 point)
        {
            MoveIntent intent = entities.Get(intents, unit);
            if (!intent.Active) return;
            if (TryCrowdWaypoint(point, intent.Target, out SimVec3 waypoint)) entities.Set(waypoints, unit, new MoveWaypoint(waypoint, true));
            else Stop(unit);
        }
    }
}
