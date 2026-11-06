using System;
using WarCore.Sim;

namespace WarCore.Interim
{
    public sealed partial class InterimLocomotion
    {
        private readonly bool crowd;
        private readonly IControlledStep controlledStep;

        public InterimLocomotion(EntityStore entities, uint wallMask, NavGrid navGrid, bool crowd,
            IControlledStep controlledStep) : this(entities, wallMask, navGrid, crowd)
        {
            if (controlledStep != null && !crowd)
                throw new ArgumentException("Controlled movement requires Crowd.", nameof(controlledStep));
            this.controlledStep = controlledStep;
        }
        private readonly ICrowdPass pass;

        public InterimLocomotion(EntityStore entities, uint wallMask, NavGrid navGrid, bool crowd,
            IControlledStep controlledStep, ICrowdPass pass) : this(entities, wallMask, navGrid, crowd, controlledStep)
        {
            if (pass != null && !crowd)
                throw new ArgumentException("A crowd pass rule requires Crowd.", nameof(pass));
            this.pass = pass;
        }
        private CrowdMember[] provisional = Array.Empty<CrowdMember>();
        private int memberCount;
        private bool useCrowdBroadPhase = true; // Reflection-only differential-test switch; not simulation state.

        private struct CrowdMember
        {
            internal EntityId Unit;
            internal SimVec3 Point;
            internal SimFix Radius;
            // Snapshot of MoveIntent.Active taken when the table is built, so the answer does not change within one step.
            internal bool Active;
        }

        private void StepCrowd(SimContext context)
        {
            memberCount = 0;
            foreach (EntityId unit in stage.Capture())
            {
                if (!entities.TryGet(intents, unit, out MoveIntent memberIntent) || !entities.TryGet(positions, unit, out Position position)) continue;
                if (!entities.TryGet(colliders, unit, out Collider collider)
                    || collider.ShapeKind != ShapeKind.VerticalCapsule || (collider.Layer & wallMask) != 0)
                    throw new InvalidOperationException("Crowd requires capsules on a separate mover layer.");
                if (memberCount == provisional.Length)
                    Array.Resize(ref provisional, memberCount < 8 ? 16 : memberCount * 2);
                provisional[memberCount++] = new CrowdMember { Unit = unit, Point = position.Value, Radius = collider.Radius, Active = memberIntent.Active };
            }
            // Keep the member table in stable order: blocker ties and all AI processing retain that order.
            int controlledIndex = -1;
            if (controlledStep != null && controlledStep.TryGetStep(context, out EntityId controlled, out SimVec3 displacement))
                for (int i = 0; i < memberCount; i++)
                    if (provisional[i].Unit.Equals(controlled))
                    {
                        controlledIndex = i;
                        StepControlled(context, i, displacement);
                        break;
                    }
            for (int i = 0; i < memberCount; i++)
            {
                if (i == controlledIndex) continue;
                if (terrain != null) { StepCrowdUnitTerrain(context, i); continue; }
                EntityId unit = provisional[i].Unit;
                MoveIntent intent = entities.Get(intents, unit);
                if (!intent.Active) continue;
                SimVec3 point = provisional[i].Point;
                SimVec3 waypoint = entities.Get(waypoints, unit).Point;
                SimVec3 delta = waypoint - point;
                SimFix speed = motion.StepSpeed(unit, intent.SpeedPerTick);
                SimWide stepSquared = new SimVec3(speed, default, default).LengthSquared;
                SimVec3 next = delta.LengthSquared <= stepSquared ? waypoint : point + delta.Normalized * speed;
                bool clear = UnitsAllow(i, next, out int blocker);
                if (!clear || !TerrainAllows(context, i, next))
                {
                    if (blocker < 0)
                    {
                        // A wall clipped the step (cutting the corner of the next cell): ease back toward the own cell centre.
                        if (!grid.TryCell(point, out int own)) continue;
                        SimVec3 centre = grid.Center(own, point.Y);
                        SimVec3 back = centre - point;
                        if (back.LengthSquared.Sign == 0) continue;
                        next = back.LengthSquared <= stepSquared ? centre : point + back.Normalized * speed;
                        if (!UnitsAllow(i, next, out _) || !TerrainAllows(context, i, next)) continue;
                        goto submit;
                    }
                    SimVec3 radial = point - provisional[blocker].Point;
                    // Coincident points use +X as the radial axis. Zero dot keeps the first side.
                    if (radial.LengthSquared.Sign == 0) radial = SimVec3.Metres(1, 0, 0);
                    SimVec3 side = new SimVec3(-radial.Z, default, radial.X).Normalized * speed;
                    if (SimVec3.Dot(side, delta).Sign < 0) side = default(SimVec3) - side;
                    next = point + side;
                    if (!UnitsAllow(i, next, out _) || !TerrainAllows(context, i, next))
                    {
                        next = point - side;
                        if (!UnitsAllow(i, next, out _) || !TerrainAllows(context, i, next)) continue;
                    }
                }
            submit:
                context.SubmitMovement(unit, next);
                Face(unit, point, next);
                provisional[i].Point = next;
                if (next.Equals(waypoint) && waypoint.Equals(intent.Target)) { Stop(unit); continue; }
                // Progress is entering the next cell, not standing on its centre: a crowd cannot all hit one point.
                grid.TryCell(point, out int before);
                grid.TryCell(next, out int after);
                if (before == after) continue;
                if (TryCrowdWaypoint(next, intent.Target, out SimVec3 following))
                    entities.Set(waypoints, unit, new MoveWaypoint(following, true));
                else Stop(unit);
            }
        }

        private void StepControlled(SimContext context, int i, SimVec3 displacement)
        {
            if (displacement.LengthSquared.Sign == 0) return;
            SimVec3 point = provisional[i].Point;
            SimVec3 next = point + displacement;
            if (!ControlledAllows(context, i, next))
            {
                next = point + new SimVec3(displacement.X, default, default);
                if (next.Equals(point) || !ControlledAllows(context, i, next))
                {
                    next = point + new SimVec3(default, default, displacement.Z);
                    if (next.Equals(point) || !ControlledAllows(context, i, next)) return;
                }
            }
            context.SubmitMovement(provisional[i].Unit, next);
            Face(provisional[i].Unit, point, next);
            provisional[i].Point = next;
        }

        private bool ControlledAllows(SimContext context, int i, SimVec3 next)
        {
            // Temporary endpoint rule: player steps are shorter than the radius. T1b replaces
            // this with Slide. Shrink by one raw so wall contact permits tangent/outward motion.
            SimFix radius = SimFix.FromRaw(MaxRaw(0, provisional[i].Radius.Raw - 1));
            if (wallMask != 0 && context.SpatialQuery.Any(SpatialQueries.Sphere(next, radius, wallMask))) return false;
            if (!ControlledGridAllows(next, radius)) return false;
            // Initial overlap uses the AI rule: permit each step that strictly increases distance
            // until separated; do not permit equal/closer steps while still overlapping.
            return UnitsAllow(i, next, out _);
        }

        private static long MaxRaw(long a, long b) => a > b ? a : b;

        private bool ControlledGridAllows(SimVec3 next, SimFix radius)
        {
            if (!grid.TryCell(next, out int cell)) return false;
            int cx = cell % grid.Width, cz = cell / grid.Width;
            SimWide threshold = new SimVec3(radius, default, default).LengthSquared;
            // Current player radius is 0.25 m in a 1 m grid. Only these 3x3 cells can touch it.
            // Out-of-map cells are blocked too, so the same nearest-point rule covers map edges.
            for (int z = cz - 1; z <= cz + 1; z++)
                for (int x = cx - 1; x <= cx + 1; x++)
                {
                    if (grid.IsPassable(x, z)) continue;
                    long left = grid.OriginX.Raw + x * SimFix.Scale;
                    long bottom = grid.OriginZ.Raw + z * SimFix.Scale;
                    long dx = MaxRaw(0, MaxRaw(left - next.X.Raw, next.X.Raw - (left + SimFix.Scale)));
                    long dz = MaxRaw(0, MaxRaw(bottom - next.Z.Raw, next.Z.Raw - (bottom + SimFix.Scale)));
                    if (new SimVec3(SimFix.FromRaw(dx), default, SimFix.FromRaw(dz)).LengthSquared < threshold)
                        return false;
                }
            return true;
        }

        // Crowd route: head for the centre of the next cell on the A* path, or the exact target in the target's cell.
        private bool TryCrowdWaypoint(SimVec3 point, SimVec3 target, out SimVec3 waypoint)
        {
            waypoint = default;
            if (pathfinder.Find(point, target) != GridPathStatus.Found) return false;
            ReadOnlySpan<int> route = pathfinder.Path;
            waypoint = route.Length == 1 ? target : Centre(route[1], point.Y);
            if (terrain != null) NoteTail(route, route.Length == 1 ? 0 : 1);
            return true;
        }

        private bool TerrainAllows(SimContext context, int i, SimVec3 next)
        {
            if (!grid.TryCell(next, out int cell) || !grid.IsPassable(cell % grid.Width, cell / grid.Width)) return false;
            if (terrain != null && !grid.SegmentClear(provisional[i].Point, next, UnitFootprint)) return false;
            return wallMask == 0 || !context.SpatialQuery.Any(SpatialQueries.Capsule(
                provisional[i].Point, next, provisional[i].Radius, wallMask));
        }

        private bool UnitsAllow(int i, SimVec3 next, out int blocker)
        {
            blocker = -1;
            SimWide nearest = default;
            for (int j = 0; j < memberCount; j++)
            {
                if (j == i) continue;
                if (useCrowdBroadPhase)
                {
                    long dx = next.X.Raw - provisional[j].Point.X.Raw;
                    if (dx < 0) dx = -dx;
                    long dz = next.Z.Raw - provisional[j].Point.Z.Raw;
                    if (dz < 0) dz = -dz;
                    long r = (provisional[i].Radius + provisional[j].Radius).Raw;
                    // A sufficient condition for newDistance >= threshold, including any Y separation.
                    if (dx >= r || dz >= r) continue;
                }
                if (pass != null && !provisional[j].Active && pass.MayPass(provisional[i].Unit, provisional[j].Unit)) continue;
                SimWide oldDistance = Planar(provisional[i].Point - provisional[j].Point).LengthSquared;
                SimWide newDistance = Planar(next - provisional[j].Point).LengthSquared;
                SimFix radius = provisional[i].Radius + provisional[j].Radius;
                SimWide threshold = new SimVec3(radius, default, default).LengthSquared;
                if (newDistance >= threshold || (oldDistance < threshold && newDistance > oldDistance)) continue;
                // Stable order breaks equal-distance ties.
                if (blocker < 0 || oldDistance < nearest) { blocker = j; nearest = oldDistance; }
            }
            return blocker < 0;
        }
    }
}
