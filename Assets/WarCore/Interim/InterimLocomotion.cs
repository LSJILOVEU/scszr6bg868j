using System;
using WarCore.Sim;

namespace WarCore.Interim
{
    public sealed partial class InterimLocomotion : ILocomotion, IRetainedGameplaySystem
    {
        private readonly EntityStore entities;
        private readonly EntityStage stage;
        private readonly ComponentType<Position> positions;
        private readonly ComponentType<MoveIntent> intents;
        private readonly ComponentType<Collider> colliders;
        private readonly uint wallMask;
        private ComponentType<UnitFacing> facing;
        private bool faceMovement;

        // The caller reserves wallMask for static boxes, separate from mover layers.
        public InterimLocomotion(EntityStore entities, uint wallMask = 0, NavGrid navGrid = null, bool crowd = false)
        {
            this.entities = entities ?? throw new ArgumentNullException(nameof(entities));
            stage = new EntityStage(entities);
            positions = entities.Resolve<Position>();
            intents = entities.Resolve<MoveIntent>();
            this.wallMask = wallMask;
            grid = navGrid;
            this.crowd = crowd;
            if (crowd && (grid == null || entities.IdentityMode != IdentityMode.StableWorld))
                throw new ArgumentException("Crowd requires navigation and stable identities.");
            if (grid != null)
            {
                pathfinder = new GridPathfinder(grid);
                waypoints = entities.Resolve<MoveWaypoint>();
            }
            if (wallMask != 0 || crowd)
                colliders = entities.Resolve<Collider>();
        }

        // Opens the facing write: a unit that moves faces the horizontal direction of the step; a step with no horizontal
        // part, or a unit that does not move, keeps its facing. Without this call no facing is touched.
        public void BindFacing(ComponentType<UnitFacing> component) { facing = component; faceMovement = true; }

        // The step length of a moving unit comes from the motion model; the default reproduces the unit's own speed exactly.
        private IMotionModel motion = new InstantMotion();
        public void BindMotion(IMotionModel model) { motion = model ?? throw new ArgumentNullException(nameof(model)); }

        private void Face(EntityId unit, SimVec3 from, SimVec3 to)
        {
            if (!faceMovement) return;
            var step = new SimVec3(to.X - from.X, default, to.Z - from.Z);
            if (step.LengthSquared.Sign == 0) return;
            SimVec3 direction = step.Normalized;
            entities.Set(facing, unit, new UnitFacing(direction.X, direction.Z));
        }

        int ISimSystem.Order => 20;
        public string StateTypeId => "interim.locomotion";
        // A terrain world adds 100 to the version of the same movement kind and appends the terrain digest to its payload.
        public int StateVersion => (crowd ? (pass != null ? 5 : 4) : grid != null ? 3 : wallMask == 0 ? 1 : 2) + (terrain != null ? 100 : 0);

        public void Order(EntityId unit, SimVec3 target)
        {
            MoveIntent intent = RequireIntent(unit);
            target = Grounded(target);
            if (grid != null)
            {
                SimVec3 point = entities.Get(positions, unit).Value;
                if (!(crowd ? TryCrowdWaypoint(point, target, out SimVec3 waypoint) : TryWaypoint(point, target, out waypoint)))
                {
                    Stop(unit);
                    if (terrain != null) BeginProgress(unit, point, default, target, false);
                    return;
                }
                entities.Set(waypoints, unit, new MoveWaypoint(waypoint, true));
                if (terrain != null) BeginProgress(unit, point, waypoint, target, true);
            }
            entities.Set(intents, unit, new MoveIntent(target, intent.SpeedPerTick, intent.Squad, true));
        }

        public void Stop(EntityId unit)
        {
            MoveIntent intent = RequireIntent(unit);
            entities.Set(intents, unit, new MoveIntent(intent.Target, intent.SpeedPerTick, intent.Squad, false));
            if (grid != null) entities.Set(waypoints, unit, default);
            // An explicit stop is not a failure; the internal give-ups set Failed after calling it.
            if (terrain != null && entities.TryGet(progress, unit, out _)) entities.Set(progress, unit, default);
        }

        public bool IsArrived(EntityId unit)
        {
            return entities.TryGet(intents, unit, out MoveIntent intent)
                && entities.TryGet(positions, unit, out Position position)
                && position.Value.Equals(intent.Target);
        }

        private MoveIntent RequireIntent(EntityId unit)
        {
            if (!entities.TryGet(intents, unit, out MoveIntent intent))
                throw new InvalidOperationException("Unit has no move intent.");
            return intent;
        }

        public void Step(SimContext context)
        {
            if (crowd) { StepCrowd(context); return; }
            foreach (EntityId unit in stage.Capture())
            {
                if (!entities.TryGet(intents, unit, out MoveIntent intent) || !intent.Active
                    || !entities.TryGet(positions, unit, out Position position))
                    continue;
                if (grid != null)
                {
                    if (terrain != null) StepPathTerrain(context, unit, intent, position.Value);
                    else StepPath(context, unit, intent, position.Value);
                    continue;
                }
                SimVec3 delta = intent.Target - position.Value;
                SimFix speed = motion.StepSpeed(unit, intent.SpeedPerTick);
                SimWide stepSquared = new SimVec3(speed, default, default).LengthSquared;
                bool arrived = delta.LengthSquared <= stepSquared;
                SimVec3 next = arrived ? intent.Target : position.Value + delta.Normalized * speed;
                if (wallMask != 0)
                {
                    if (!entities.TryGet(colliders, unit, out Collider collider)
                        || collider.ShapeKind != ShapeKind.VerticalCapsule || (collider.Layer & wallMask) != 0)
                        throw new InvalidOperationException("Blocked locomotion requires a capsule on a separate mover layer.");
                    if (context.SpatialQuery.Any(SpatialQueries.Capsule(position.Value, next, collider.Radius, wallMask)))
                        continue;
                }
                context.SubmitMovement(unit, next);
                Face(unit, position.Value, next);
                if (arrived)
                    Stop(unit);
            }
        }

        // Mutable state is in components; search buffers and bindings are derived.
        public void SaveState(SimStateWriter writer)
        {
            if (wallMask != 0 || grid != null)
                writer.Int64(wallMask);
            if (grid != null) writer.UInt64(grid.Digest);
            if (pass != null) writer.UInt64(pass.Digest);
            if (terrain != null) writer.UInt64(terrain.Digest);
        }

        public void LoadState(SimStateReader reader)
        {
            if ((wallMask != 0 || grid != null) && reader.Int64() != wallMask)
                throw new InvalidOperationException("Locomotion wall mask differs from the saved configuration.");
            if (grid != null && reader.UInt64() != grid.Digest)
                throw new InvalidOperationException("Navigation digest differs from the saved configuration.");
            if (pass != null && reader.UInt64() != pass.Digest)
                throw new InvalidOperationException("Crowd pass rule differs from the saved configuration.");
            if (terrain != null && reader.UInt64() != terrain.Digest)
                throw new InvalidOperationException("Terrain tables differ from the saved configuration.");
        }

        public void WriteState(StateHash hash)
        {
            hash.Add(StateTypeId);
            hash.Add(StateVersion);
            if (wallMask != 0 || grid != null)
                hash.Add((ulong)wallMask);
            if (grid != null) hash.Add(grid.Digest);
            if (pass != null) hash.Add(pass.Digest);
            if (terrain != null) hash.Add(terrain.Digest);
        }
    }
}
