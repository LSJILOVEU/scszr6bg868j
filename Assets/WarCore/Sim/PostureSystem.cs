using System;

namespace WarCore.Sim
{
    public sealed partial class PostureSystem : ISimSystem, IStableStateSerializer, ISimRestoreValidator
    {
        private readonly EntityStore entities;
        private readonly ComponentType<Posture> postures;
        private readonly ComponentType<Position> positions;
        private readonly ComponentType<Collider> colliders;
        private readonly EventTypeId transitionType;
        public PostureGeometry Geometry { get; }
        public int Order { get; }
        public string StateTypeId { get; }
        public int StateVersion => 1;

        public PostureSystem(EntityStore entities, ComponentType<Posture> postures, ComponentType<Position> positions,
            ComponentType<Collider> colliders, PostureGeometry geometry, EventTypeId transitionType, int order,
            GridSpatialQuery grid = null)
        {
            if (entities == null || postures.Owner != entities || positions.Owner != entities || colliders.Owner != entities
                || geometry == null || geometry.Entities != entities || geometry.ComponentId != postures.Storage.Name
                || transitionType.Value <= 0 || (grid != null && grid.Postures != geometry))
                throw new ArgumentException("Posture system requires matching handles, geometry and a registered event type.");
            this.entities = entities;
            this.postures = postures;
            this.positions = positions;
            this.colliders = colliders;
            this.transitionType = transitionType;
            Geometry = geometry;
            Order = order;
            StateTypeId = "warcore.posture/" + postures.Storage.Name + "/" + positions.Storage.Name + "/" + colliders.Storage.Name;
        }

        public long BeginTransition(SimContext context, EntityId entity, int targetId, long durationTicks)
        {
            if (entities.IdentityMode == IdentityMode.StableWorld)
                throw new SimBoundaryException("StableWorld requires BeginStableTransition.");
            if (context.Entities != entities || durationTicks <= 0)
                throw new ArgumentException("A posture transition requires its owning context and a positive duration.");
            Posture previous = entities.TryGet(postures, entity, out Posture value) ? value : default;
            if (previous.IsTransitioning)
                throw new InvalidOperationException("Cancel the active posture transition before replacing it.");
            Geometry.BodyFor(entity, entities.Get(positions, entity).Value, entities.Get(colliders, entity), targetId);
            if (targetId == previous.CurrentId)
                return 0;
            long due = SimChecked.Add(context.Clock.Tick, durationTicks);
            long id = context.Scheduler.Schedule(due, new SimEvent(transitionType, target: entity, tag: targetId));
            entities.Set(postures, entity, new Posture(previous.CurrentId, targetId, due, id));
            return id;
        }

        public bool CancelTransition(SimContext context, EntityId entity)
        {
            if (context.Entities != entities)
                throw new ArgumentException("Posture context belongs to another simulation.");
            if (!entities.TryGet(postures, entity, out Posture posture) || !posture.IsTransitioning)
                return false;
            if (posture.UsesStableTask)
                context.Scheduler.CancelStable(posture.StableTask);
            else
                context.Scheduler.Cancel(posture.TaskId);
            entities.Set(postures, entity, new Posture(posture.CurrentId));
            return true;
        }

        public void Step(SimContext context)
        {
            if (context.Entities != entities)
                throw new InvalidOperationException("Posture system belongs to another simulation.");
            if (entities.IdentityMode == IdentityMode.StableWorld)
            {
                StepStable(context);
                return;
            }
            while (context.Scheduler.TryDequeue(transitionType, out ScheduledTask due))
            {
                EntityId entity = due.Event.Target;
                if (!entities.TryGet(postures, entity, out Posture posture) || posture.TaskId != due.Id)
                    continue;
                if (posture.DueTick != context.Clock.Tick || due.Event.Tick != posture.DueTick || due.Event.Tag != posture.TargetId)
                    throw new InvalidOperationException("Posture transition deadline or payload mismatch.");
                Geometry.BodyFor(entity, entities.Get(positions, entity).Value, entities.Get(colliders, entity), posture.TargetId);
                entities.Set(postures, entity, new Posture(posture.TargetId));
            }
        }

        public void WriteState(StateHash hash)
        {
            hash.Add(StateTypeId);
            transitionType.WriteState(hash);
            Geometry.Catalog.WriteState(hash);
        }

        public void SaveState(SimStateWriter writer)
        {
            writer.Int32(transitionType.Value);
            Geometry.Catalog.SaveState(writer);
        }

        public void LoadState(SimStateReader reader)
        {
            SimStateReader.Require(reader.Int32() == transitionType.Value, "Posture event registration mismatch.");
            Geometry.Catalog.LoadState(reader);
            foreach (EntityId entity in entities)
            {
                if (!entities.TryGet(postures, entity, out Posture posture))
                    continue;
                if (!entities.TryGet(positions, entity, out Position position) || !entities.TryGet(colliders, entity, out Collider collider))
                    continue;
                SimVec3 center = position.Value;
                Geometry.BodyFor(entity, center, collider, posture.CurrentId);
                if (posture.IsTransitioning)
                    Geometry.BodyFor(entity, center, collider, posture.TargetId);
            }
        }
        public void ValidateRestoredState(SimContext context)
        {
            // Cross-system checks run after every payload has loaded, regardless of registration order.
            foreach (EntityId entity in entities)
            {
                if (!entities.TryGet(postures, entity, out Posture posture) || !posture.IsTransitioning)
                    continue;
                if (entities.IdentityMode == IdentityMode.StableWorld)
                {
                    ValidateStablePosture(context,entity,posture);
                    continue;
                }
                bool matches = context.Scheduler.TryGetPending(posture.TaskId, out ScheduledTask task)
                    && task.Event.Tick == posture.DueTick && task.Event.Tick > context.Clock.Tick
                    && task.Event.Type.Equals(transitionType) && task.Event.Target.Equals(entity)
                    && task.Event.Tag == posture.TargetId;
                SimStateReader.Require(matches, "Posture transition has no matching pending task.");
            }
        }
    }
}
