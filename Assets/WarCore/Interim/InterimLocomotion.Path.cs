using System;
using WarCore.Sim;

namespace WarCore.Interim
{
    public sealed partial class InterimLocomotion
    {
        private readonly NavGrid grid;
        private readonly GridPathfinder pathfinder;
        private readonly ComponentType<MoveWaypoint> waypoints;

        private bool TryWaypoint(SimVec3 point, SimVec3 target, out SimVec3 waypoint)
        {
            waypoint = default;
            if (pathfinder.Find(point, target) != GridPathStatus.Found) return false;
            ReadOnlySpan<int> route = pathfinder.Path;
            SimVec3 center = Centre(route[0], point.Y);
            if (!point.Equals(center)) { waypoint = center; if (terrain != null) NoteTail(route, 0); return true; }
            if (route.Length == 1) { waypoint = target; if (terrain != null) NoteTail(route, 0); return true; }
            int vertex = 1;
            int dx = route[1] % grid.Width - route[0] % grid.Width;
            int dz = route[1] / grid.Width - route[0] / grid.Width;
            while (vertex + 1 < route.Length
                && route[vertex + 1] % grid.Width - route[vertex] % grid.Width == dx
                && route[vertex + 1] / grid.Width - route[vertex] / grid.Width == dz) vertex++;
            waypoint = Centre(route[vertex], point.Y);
            if (terrain != null) NoteTail(route, vertex);
            return true;
        }

        private void StepPath(SimContext context, EntityId unit, MoveIntent intent, SimVec3 point)
        {
            SimVec3 waypoint = entities.Get(waypoints, unit).Point;
            SimVec3 delta = waypoint - point;
            SimFix speed = motion.StepSpeed(unit, intent.SpeedPerTick);
            SimWide stepSquared = new SimVec3(speed, default, default).LengthSquared;
            bool arrived = delta.LengthSquared <= stepSquared;
            SimVec3 next = arrived ? waypoint : point + delta.Normalized * speed;
            if (wallMask != 0)
            {
                if (!entities.TryGet(colliders, unit, out Collider collider)
                    || collider.ShapeKind != ShapeKind.VerticalCapsule || (collider.Layer & wallMask) != 0)
                    throw new InvalidOperationException("Blocked locomotion requires a capsule on a separate mover layer.");
                if (context.SpatialQuery.Any(SpatialQueries.Capsule(point, next, collider.Radius, wallMask))) return;
            }
            context.SubmitMovement(unit, next);
            Face(unit, point, next);
            if (!arrived) return;
            if (waypoint.Equals(intent.Target)) { Stop(unit); return; }
            // Movement commits later; compute from the submitted point, not the old Position component.
            if (TryWaypoint(next, intent.Target, out SimVec3 following))
                entities.Set(waypoints, unit, new MoveWaypoint(following, true));
            else Stop(unit);
        }
    }
}
