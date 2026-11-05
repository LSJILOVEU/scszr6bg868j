namespace WarCore.Sim
{
    public sealed partial class PostureSystem
    {
        public TaskKey BeginStableTransition(SimContext context, EntityLink target, int targetId, long durationTicks, ulong kind)
        {
            entities.RequireStableWorld();
            if (context.Entities != entities || durationTicks <= 0)
                throw new SimBoundaryException("Stable posture requires its owning context and a positive duration.");
            EntityId entity = entities.Resolve(target);
            Posture previous = entities.TryGet(postures,entity,out Posture value) ? value : default;
            if (previous.IsTransitioning)
                throw new SimBoundaryException("Cancel the active posture transition before replacing it.");
            Geometry.BodyFor(entity,entities.Read(positions,entity).Value,entities.Read(colliders,entity),targetId);
            if (targetId == previous.CurrentId)
                return default;
            long due = SimChecked.Add(context.Clock.Tick,durationTicks);
            TaskKey key = context.Scheduler.ScheduleFor(due,0,target.Id,kind,new StableSimEvent(transitionType,target:target,tag:targetId));
            entities.Set(postures,entity,new Posture(previous.CurrentId,targetId,key));
            return key;
        }
        private void StepStable(SimContext context)
        {
            while (context.Scheduler.TryDequeueStable(transitionType,out StableScheduledTask due))
            {
                if (!due.Event.Target.HasValue || !entities.TryResolve(due.Event.Target.Id,out EntityRef target))
                    continue;
                EntityId entity = target;
                if (!entities.TryGet(postures,entity,out Posture posture) || !posture.UsesStableTask || !posture.StableTask.Equals(due.Key))
                    continue;
                if (posture.DueTick != context.Clock.Tick || due.Event.Tick != posture.DueTick || due.Event.Tag != posture.TargetId)
                    throw new SimBoundaryException("Stable posture deadline or payload mismatch.");
                Geometry.BodyFor(entity,entities.Read(positions,entity).Value,entities.Read(colliders,entity),posture.TargetId);
                entities.Set(postures,entity,new Posture(posture.TargetId));
            }
        }
        private void ValidateStablePosture(SimContext context, EntityId entity, Posture posture)
        {
            bool matches = posture.UsesStableTask && context.Scheduler.TryGetPendingStable(posture.StableTask,out StableScheduledTask task)
                && task.Key.DueTick == posture.DueTick && task.Key.DueTick > context.Clock.Tick
                && task.Event.Type.Equals(transitionType) && task.Event.Target.Equals(entities.Link(entity)) && task.Event.Tag == posture.TargetId;
            SimStateReader.Require(matches,"Stable posture has no matching pending TaskKey.");
        }
    }
}
