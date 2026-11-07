using System;
using WarCore.Sim;

namespace WarCore.Interim
{
    // A motion model with acceleration and braking. Each step, in raw integers:
    //   cap   = wanted, lowered to MotionLimits.MaxSpeed when that is set;
    //   prev  = the speed returned last tick, or 0 when the unit was not asked last tick (it starts from rest);
    //   v     = min(cap, prev + Accel) when Accel is set, else cap;
    //   brake = floor(sqrt(2 * Decel * r)), r the horizontal distance from the unit to its final target (not the next waypoint),
    //           and v = min(v, brake) when Decel is set and r > 0.
    // The answer is written back with the tick. A unit without motion limits or motion state moves at the speed it wanted
    // and nothing is written.
    public sealed class InertiaMotion : IMotionModel
    {
        private readonly EntityStore entities;
        private readonly ComponentType<Position> positions;
        private readonly ComponentType<MoveIntent> intents;
        private readonly ComponentType<MotionLimits> limits;
        private readonly ComponentType<MotionState> states;
        private readonly SimClock clock;

        public InertiaMotion(EntityStore entities, ComponentType<Position> positions, ComponentType<MoveIntent> intents,
            ComponentType<MotionLimits> limits, ComponentType<MotionState> states, SimClock clock)
        {
            this.entities = entities ?? throw new ArgumentNullException(nameof(entities));
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            this.positions = positions;
            this.intents = intents;
            this.limits = limits;
            this.states = states;
        }

        public SimFix StepSpeed(EntityId unit, SimFix wanted)
        {
            if (!entities.TryGet(limits, unit, out MotionLimits limit) || !entities.TryGet(states, unit, out MotionState state))
                return wanted;
            long now = clock.Tick;
            SimFix cap = wanted;
            if (limit.MaxSpeed.Raw > 0) cap = SimFix.Min(cap, limit.MaxSpeed);
            SimFix prev = state.Tick == now - 1 ? state.Speed : default;
            SimFix v = limit.Accel.Raw > 0 ? SimFix.Min(cap, prev + limit.Accel) : cap;
            if (limit.Decel.Raw > 0)
            {
                SimVec3 target = entities.Read(intents, unit).Target;
                SimVec3 point = entities.Read(positions, unit).Value;
                SimFix r = new SimVec3(target.X - point.X, default, target.Z - point.Z).LengthSquared.Sqrt();
                if (r.Raw > 0)
                {
                    SimFix brake = SimVec3.Dot(new SimVec3(limit.Decel + limit.Decel, default, default), new SimVec3(r, default, default)).Sqrt();
                    v = SimFix.Min(v, brake);
                }
            }
            entities.Set(states, unit, new MotionState(v, now));
            return v;
        }
    }
}
