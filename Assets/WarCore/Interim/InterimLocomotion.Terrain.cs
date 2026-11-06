using System;
using WarCore.Sim;

namespace WarCore.Interim
{
    // Terrain mode (T1a-2a): tables from the map under one rule set. Everything here is inert when `terrain` is null, so a
    // world without terrain keeps its exact behaviour, serialised bytes and hashes.
    public sealed partial class InterimLocomotion
    {
        // Base step cap 0.25 m. With the largest surface multiplier (2x) a step is at most 0.5 m, below the 1 m a wall is
        // thick, and `base * 2000` stays far inside a long.
        public const long MaxTerrainStepRaw = SimFix.Scale / 4;
        public const long MaxEffectiveStepRaw = SimFix.Scale / 2;
        // Clearance radius of a unit against blocked cells and the map edge (T1a-2a uses 0.25 m for every unit).
        public static SimFix UnitFootprint => SimFix.FromRaw(SimFix.Scale / 4);
        // Steps without getting closer before a re-plan, re-plans per order, and the order-level limit (4 x StallLimit).
        public const int StallLimit = 60, MaxRetries = 3, OrderStallLimit = 240;

        private readonly TerrainTables terrain;
        // Non-null only when the map has a height field: then distances are horizontal and Y follows the ground.
        private readonly HeightField ground;
        private readonly ComponentType<MoveProgress> progress;

        public InterimLocomotion(EntityStore entities, uint wallMask, NavGrid navGrid, bool crowd,
            IControlledStep controlledStep, ICrowdPass pass, TerrainTables terrain)
            : this(entities, wallMask, terrain == null ? navGrid : terrain.Nav, crowd, controlledStep, pass)
        {
            if (terrain == null) return;
            if (navGrid != null && !ReferenceEquals(navGrid, terrain.Nav))
                throw new ArgumentException("Navigation must be the terrain's own grid.", nameof(navGrid));
            if (grid == null) throw new ArgumentException("Terrain needs a path or crowd mover.", nameof(terrain));
            for (int key = 0; key < terrain.Nav.Count; key++)
                if (terrain.SpeedMilliAt(key) > TerrainTables.MaxSpeedMilli)
                    throw new ArgumentException("Speed multiplier exceeds the terrain limit.", nameof(terrain));
            this.terrain = terrain;
            ground = terrain.Ground;
            pathfinder = new GridPathfinder(terrain);
            progress = entities.Resolve<MoveProgress>();
        }

        // Length of the route from index `from` to its end, in raw units (an orthogonal edge is one cell, a diagonal one
        // 92682 = round(sqrt 2 x 65536)). Left for the caller of the route search to pick up: BeginProgress at order time,
        // Account after a waypoint change. Scratch only, never part of the saved state.
        private long routeTail;
        private bool tailFresh;

        private void NoteTail(ReadOnlySpan<int> route, int from)
        {
            long length = 0;
            for (int i = from + 1; i < route.Length; i++)
            {
                bool diagonal = route[i] % grid.Width != route[i - 1] % grid.Width && route[i] / grid.Width != route[i - 1] / grid.Width;
                length += diagonal ? 92682 : SimFix.Scale;
            }
            routeTail = length;
            tailFresh = true;
        }

        // Did the last order of this unit give up (no route, or no progress within the limits)?
        public bool HasFailed(EntityId unit) => terrain != null && entities.TryGet(progress, unit, out MoveProgress p) && p.Failed != 0;

        // Step length after the surface under the unit: floor(base x speedMilli / 1000), at least one raw so a unit
        // never freezes. The base is checked before the product.
        private SimFix StepOf(SimVec3 point, SimFix baseStep)
        {
            if (baseStep.Raw > MaxTerrainStepRaw) throw new InvalidOperationException("Terrain worlds cap a unit step at 0.25 m per tick.");
            long milli = grid.TryCell(point, out int cell) ? terrain.SpeedMilliAt(cell) : 1000;
            long scaled = baseStep.Raw * milli / 1000;
            if (scaled < 1) scaled = 1;
            if (scaled > MaxEffectiveStepRaw) throw new InvalidOperationException("Terrain step exceeds 0.5 m.");
            return SimFix.FromRaw(scaled);
        }

        // On a height field the ground decides Y; outside its domain (or without one) the point is unchanged.
        private SimVec3 Grounded(SimVec3 p) =>
            ground != null && ground.TryHeightAt(p.X, p.Z, out SimFix y) ? new SimVec3(p.X, y, p.Z) : p;

        private SimVec3 Planar(SimVec3 v) => ground != null ? new SimVec3(v.X, default, v.Z) : v;

        private SimVec3 Centre(int cell, SimFix y) => Grounded(grid.Center(cell, y));

        // One step of `step` along `direction` from the point. Normalisation can truncate a low-speed diagonal step to
        // nothing on both axes; then the dominant axis of the direction moves one raw (X on a tie), so an unblocked unit
        // always makes progress. The point is placed on the ground afterwards.
        private SimVec3 Advance(SimVec3 point, SimVec3 direction, SimFix step) =>
            Nudge(point, point + direction.Normalized * step, direction);

        private SimVec3 Nudge(SimVec3 point, SimVec3 next, SimVec3 direction)
        {
            if (next.X.Raw == point.X.Raw && next.Z.Raw == point.Z.Raw && (direction.X.Raw != 0 || direction.Z.Raw != 0))
            {
                long ax = direction.X.Raw < 0 ? -direction.X.Raw : direction.X.Raw;
                long az = direction.Z.Raw < 0 ? -direction.Z.Raw : direction.Z.Raw;
                if (ax >= az) next = new SimVec3(SimFix.FromRaw(point.X.Raw + (direction.X.Raw < 0 ? -1 : 1)), next.Y, next.Z);
                else next = new SimVec3(next.X, next.Y, SimFix.FromRaw(point.Z.Raw + (direction.Z.Raw < 0 ? -1 : 1)));
            }
            return Grounded(next);
        }

        private static long Dist2(SimVec3 a, SimVec3 b)
        {
            long dx = a.X.Raw - b.X.Raw, dz = a.Z.Raw - b.Z.Raw;
            return dx * dx + dz * dz;
        }

        // Floor of the square root of a non-negative long (distances in a map are < 2^26 raw, squares < 2^51).
        private static long Distance(SimVec3 a, SimVec3 b)
        {
            long square = Dist2(a, b), root = 0;
            // Bitwise integer square root: no floating point in the deterministic layer.
            for (long bit = 1L << 30; bit != 0; bit >>= 1)
                if ((root + bit) * (root + bit) <= square) root += bit;
            return root;
        }

        // Called at order time: fresh progress for the new order, or an immediate failure when no route exists.
        private void BeginProgress(EntityId unit, SimVec3 point, SimVec3 waypoint, SimVec3 target, bool routed)
        {
            if (!routed) { entities.Set(progress, unit, new MoveProgress { Failed = 1 }); return; }
            tailFresh = false;
            entities.Set(progress, unit, new MoveProgress
            {
                BestDist2 = Dist2(point, waypoint), OrderBest = Distance(point, waypoint) + routeTail, Tail = routeTail,
            });
        }

        private void StepPathTerrain(SimContext context, EntityId unit, MoveIntent intent, SimVec3 point)
        {
            SimVec3 waypoint = entities.Get(waypoints, unit).Point;
            SimFix step = StepOf(point, motion.StepSpeed(unit, intent.SpeedPerTick));
            SimVec3 delta = Planar(waypoint - point);
            SimWide stepSquared = new SimVec3(step, default, default).LengthSquared;
            bool arrived = delta.LengthSquared <= stepSquared;
            SimVec3 next = arrived ? waypoint : Advance(point, delta, step);
            SimVec3 now = point;
            if (grid.SegmentClear(point, next, UnitFootprint) && !WallHit(context, unit, point, next))
            {
                context.SubmitMovement(unit, next);
                Face(unit, point, next);
                now = next;
                if (arrived)
                {
                    if (waypoint.Equals(intent.Target)) Stop(unit);
                    // Movement commits later; compute from the submitted point, not the old Position component.
                    else if (TryWaypoint(next, intent.Target, out SimVec3 following))
                        entities.Set(waypoints, unit, new MoveWaypoint(following, true));
                    else Stop(unit);
                }
            }
            Account(unit, waypoint, now, step, crowdMode: false);
        }

        private bool WallHit(SimContext context, EntityId unit, SimVec3 from, SimVec3 to)
        {
            if (wallMask == 0) return false;
            if (!entities.TryGet(colliders, unit, out Collider collider)
                || collider.ShapeKind != ShapeKind.VerticalCapsule || (collider.Layer & wallMask) != 0)
                throw new InvalidOperationException("Blocked locomotion requires a capsule on a separate mover layer.");
            return context.SpatialQuery.Any(SpatialQueries.Capsule(from, to, collider.Radius, wallMask));
        }

        // The terrain twin of the per-unit body of StepCrowd: same candidates in the same order (forward, back to the own
        // cell centre when a wall clipped the step, sidestep when a unit blocks), with steps from Advance and clearance
        // from TerrainAllows. The flat loop is untouched.
        private void StepCrowdUnitTerrain(SimContext context, int i)
        {
            EntityId unit = provisional[i].Unit;
            MoveIntent intent = entities.Get(intents, unit);
            if (!intent.Active) return;
            SimVec3 point = provisional[i].Point;
            SimVec3 waypoint = entities.Get(waypoints, unit).Point;
            SimFix step = StepOf(point, motion.StepSpeed(unit, intent.SpeedPerTick));
            SimVec3 delta = Planar(waypoint - point);
            SimWide stepSquared = new SimVec3(step, default, default).LengthSquared;
            SimVec3 next = delta.LengthSquared <= stepSquared ? waypoint : Advance(point, delta, step);
            bool moved = true;
            if (!UnitsAllow(i, next, out int blocker) || !TerrainAllows(context, i, next))
            {
                if (blocker < 0)
                {
                    // A wall clipped the step: ease back toward the own cell centre.
                    moved = false;
                    if (grid.TryCell(point, out int own))
                    {
                        SimVec3 centre = Centre(own, point.Y);
                        SimVec3 back = Planar(centre - point);
                        if (back.LengthSquared.Sign != 0)
                        {
                            next = back.LengthSquared <= stepSquared ? centre : Advance(point, back, step);
                            moved = UnitsAllow(i, next, out _) && TerrainAllows(context, i, next);
                        }
                    }
                }
                else
                {
                    SimVec3 radial = Planar(point - provisional[blocker].Point);
                    // Coincident points use +X as the radial axis. Zero dot keeps the first side.
                    if (radial.LengthSquared.Sign == 0) radial = SimVec3.Metres(1, 0, 0);
                    // Same arithmetic as the flat loop (normalise, scale, then test the side against the heading).
                    SimVec3 perpendicular = new SimVec3(-radial.Z, default, radial.X);
                    SimVec3 side = perpendicular.Normalized * step;
                    if (SimVec3.Dot(side, delta).Sign < 0) { side = default(SimVec3) - side; perpendicular = default(SimVec3) - perpendicular; }
                    next = Nudge(point, point + side, perpendicular);
                    moved = UnitsAllow(i, next, out _) && TerrainAllows(context, i, next);
                    if (!moved)
                    {
                        next = Nudge(point, point - side, default(SimVec3) - perpendicular);
                        moved = UnitsAllow(i, next, out _) && TerrainAllows(context, i, next);
                    }
                }
            }
            SimVec3 now = point;
            if (moved)
            {
                context.SubmitMovement(unit, next);
                Face(unit, point, next);
                provisional[i].Point = next;
                now = next;
                if (next.Equals(waypoint) && waypoint.Equals(intent.Target)) Stop(unit);
                else
                {
                    // Progress is entering the next cell, not standing on its centre: a crowd cannot all hit one point.
                    grid.TryCell(point, out int before);
                    grid.TryCell(next, out int after);
                    if (before != after)
                    {
                        if (TryCrowdWaypoint(next, intent.Target, out SimVec3 following))
                            entities.Set(waypoints, unit, new MoveWaypoint(following, true));
                        else Stop(unit);
                    }
                }
            }
            Account(unit, waypoint, now, step, crowdMode: true);
        }

        // After a step attempt: fixed order. (1) Done (arrival resets, a failed re-route fails). (2) A new waypoint restarts
        // the per-waypoint measure and keeps the retry budget. (3) Else a strictly closer waypoint is progress, anything
        // else is a stall; StallLimit stalls re-plan (MaxRetries per order) and then fail. (4) The order-level guard:
        // not getting closer to the final target for OrderStallLimit steps fails, whatever the cells did meanwhile.
        private void Account(EntityId unit, SimVec3 waypointBefore, SimVec3 now, SimFix step, bool crowdMode)
        {
            MoveIntent intent = entities.Get(intents, unit);
            MoveProgress p = entities.Get(progress, unit);
            if (!intent.Active)
            {
                tailFresh = false;
                // Stopped inside the step: at the target it is an arrival, anywhere else the route was lost.
                p = default;
                if (!now.Equals(intent.Target)) p.Failed = 1;
                entities.Set(progress, unit, p);
                return;
            }
            SimVec3 waypoint = entities.Get(waypoints, unit).Point;
            if (tailFresh) { p.Tail = routeTail; tailFresh = false; }
            if (!waypoint.Equals(waypointBefore)) { p.BestDist2 = Dist2(now, waypoint); p.Stall = 0; }
            else
            {
                long d2 = Dist2(now, waypoint);
                if (d2 < p.BestDist2) { p.BestDist2 = d2; p.Stall = 0; } else p.Stall++;
            }
            // Progress along the route must be real: the remaining length (distance to the waypoint plus the route beyond it)
            // must shrink by at least a quarter of this step (one raw at the least), so a unit that only jiggles sideways while
            // blocked cannot keep resetting the clock, and one on a long detour is not mistaken for a stuck one.
            long toTarget = Distance(now, waypoint) + p.Tail, margin = step.Raw / 4 > 1 ? step.Raw / 4 : 1;
            if (toTarget + margin <= p.OrderBest) { p.OrderBest = toTarget; p.OrderStall = 0; } else p.OrderStall++;
            bool fail = p.OrderStall >= OrderStallLimit;
            if (!fail && p.Stall >= StallLimit)
            {
                if (p.Retries < MaxRetries)
                {
                    p.Retries++; p.Stall = 0;
                    bool routed = crowdMode ? TryCrowdWaypoint(now, intent.Target, out SimVec3 again)
                        : TryWaypoint(now, intent.Target, out again);
                    if (routed)
                    {
                        entities.Set(waypoints, unit, new MoveWaypoint(again, true));
                        p.BestDist2 = Dist2(now, again); p.Tail = routeTail; tailFresh = false;
                    }
                    else fail = true;
                }
                else fail = true;
            }
            if (fail)
            {
                Stop(unit);
                p.Failed = 1;
            }
            entities.Set(progress, unit, p);
        }
    }
}
