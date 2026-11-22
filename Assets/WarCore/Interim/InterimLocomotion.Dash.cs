using System;
using WarCore.Sim;

namespace WarCore.Interim
{
    // Dash: a short, fast, straight move a unit is asked for through TryDash (no input or AI asks yet). Each dash step goes
    // through the controlled-step checks (walls swept along the step, blocked cells, units swept along the step) with their
    // axis fallback. A dash ignores inertia: it does not ask the motion model, so the unit's next ordinary step starts from rest.
    // While a unit dashes, its order and the player's step for it wait. A step that cannot move at all ends the dash.
    // On a terrain or plate world (DASH-T) each tick's step is taken in pieces of at most 0.25 m per axis, each scaled by the
    // surface where it starts; a world without terrain takes the whole step at once, as before.
    public sealed partial class InterimLocomotion
    {
        private ComponentType<DashSpec> dashSpecs;
        private ComponentType<DashState> dashStates;
        private SimClock dashClock;
        private IUnitBusy busy;

        public void BindDash(ComponentType<DashSpec> specs, ComponentType<DashState> states, SimClock clock)
        {
            if (!crowd) throw new InvalidOperationException("Dash needs a crowd world.");
            dashClock = clock ?? throw new ArgumentNullException(nameof(clock));
            dashSpecs = specs;
            dashStates = states;
        }

        // The rule that says a unit is busy (for example crossing a window) and may not start a dash. Unbound: never busy.
        public void BindBusy(IUnitBusy rule) { busy = rule ?? throw new ArgumentNullException(nameof(rule)); }

        // Starts a dash along the horizontal part of direction (its length does not matter) and returns true; its first step
        // is taken on the next tick the locomotion runs after this tick. Refused (false, nothing written) when the world has no
        // dash, the unit has no dash or no body, the direction has no horizontal part, the clock has not reached the unit's ready
        // tick, or the unit is busy (BindBusy).
        public bool TryDash(EntityId unit, SimVec3 direction)
        {
            if (busy != null && busy.IsBusy(unit)) return false;
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
            bool blocked;
            if (terrain != null) blocked = !StepDashTerrain(context, i, step);
            else
            {
                StepControlled(context, i, step);
                blocked = step.LengthSquared.Sign != 0 && provisional[i].Point.Equals(before);
            }
            // A blocked dash ends after the step before this one; on a terrain world this tick may still have moved part of the way.
            if (blocked) entities.Set(dashStates, unit, state.EndedAfter(k - 1));
            if (blocked || k == state.Ticks) Rejoin(unit, provisional[i].Point);
            return true;
        }

        // The tick's dash step on a terrain or plate world, in m = ceil(max(|dx|, |dz|) / 0.25 m) pieces: piece j of m moves
        // DashPart(d, j, m) on each axis (so the pieces add up to the step exactly), is scaled by the surface at its start (toward
        // zero) and is tried as a controlled step. A piece is at most 0.25 m per axis before and 0.5 m after scaling, so it enters
        // at most one cell per axis (TryEnter). Each piece taken moves the member's point and layer at once, for the next piece;
        // the tick submits once, from where it started to where the last piece left it. False when a piece cannot move (no
        // candidate, or nothing left after scaling): the pieces before it stay. A step of nothing on both axes moves nothing and
        // is not blocked, as on flat ground. |d| < 2^23 and m <= 2^9, so d * j stays inside 32 bits.
        private bool StepDashTerrain(SimContext context, int i, SimVec3 step)
        {
            long dx = step.X.Raw, dz = step.Z.Raw;
            if (dx == 0 && dz == 0) return true;
            long ax = dx < 0 ? -dx : dx, az = dz < 0 ? -dz : dz;
            int m = (int)((MaxRaw(ax, az) + MaxTerrainStepRaw - 1) / MaxTerrainStepRaw);
            EntityId unit = provisional[i].Unit;
            SimVec3 start = provisional[i].Point;
            bool moved = true;
            for (int j = 1; j <= m; j++)
            {
                var piece = new SimVec3(SimFix.FromRaw(DashPart(dx, j, m)), default, SimFix.FromRaw(DashPart(dz, j, m)));
                if (!ControlledTerrainCandidate(context, i, piece, out SimVec3 next)) { moved = false; break; }
                if (layered) CommitLayer(unit);
                provisional[i].Point = next;
            }
            SimVec3 end = provisional[i].Point;
            if (!end.Equals(start))
            {
                context.SubmitMovement(unit, end);
                Face(unit, start, end);
            }
            return moved;
        }

        // After a dash, a unit with an order heads for it again from where the dash left it (on a plate world over the layered
        // graph from its layer). On a terrain world the order's progress restarts its per-waypoint measure from there, takes the
        // new route's length beyond the waypoint, and keeps its re-plan budget and order-level guard; no route is a failure.
        private void Rejoin(EntityId unit, SimVec3 point)
        {
            MoveIntent intent = entities.Get(intents, unit);
            if (!intent.Active) return;
            if (TryCrowdRoute(unit, point, intent.Target, out SimVec3 waypoint))
            {
                entities.Set(waypoints, unit, new MoveWaypoint(waypoint, true));
                if (terrain == null) return;
                MoveProgress p = entities.Get(progress, unit);
                p.Tail = routeTail; p.BestDist2 = Dist2(point, waypoint); p.Stall = 0;
                tailFresh = false;
                entities.Set(progress, unit, p);
                return;
            }
            if (terrain == null) { Stop(unit); return; }
            MoveProgress lost = entities.Get(progress, unit);
            Stop(unit);
            lost.Failed = 1;
            entities.Set(progress, unit, lost);
        }
    }
}
