using System;

namespace WarCore.Sim
{
    public interface IControlTarget
    {
        EntityId Current { get; }
        void Switch(SimContext context, EntityId target);
    }

    // Content decides how an input affects a soldier, carrier or other controlled entity.
    public interface IControlInputHandler : IStateHash, ISimStateSerializer
    {
        void Apply(SimContext context, EntityId target, in InputFrame input);
    }

    public sealed class ControlTargetSystem : IControlTarget, ISimSystem, IStableStateSerializer
    {
        private readonly EntityStore entities;
        private readonly IControlInputHandler handler;
        private readonly EventTypeId switchType;
        public EntityId Current { get; private set; }
        public EntityLink CurrentLink { get; private set; }
        public int Order { get; }
        public string StateTypeId { get; }
        public int StateVersion => 1;

        public ControlTargetSystem(EntityStore entities, EntityId initial, IControlInputHandler handler, EventTypeId switchType, int order)
        {
            if (entities == null || !entities.IsAlive(initial) || handler == null || switchType.Value <= 0
                || string.IsNullOrEmpty(handler.StateTypeId) || handler.StateVersion <= 0)
                throw new ArgumentException("Control requires a live target, a versioned input handler and a registered event type.");
            this.entities = entities;
            this.handler = handler;
            this.switchType = switchType;
            Current = initial;
            if (entities.IdentityMode == IdentityMode.StableWorld)
                CurrentLink = entities.Link(initial);
            Order = order;
            StateTypeId = "warcore.control/" + handler.StateTypeId + "/" + handler.StateVersion;
        }

        public void Switch(SimContext context, EntityId target)
        {
            context.RequireActive();
            if (context.Entities != entities || !entities.IsAlive(target))
                throw new ArgumentException("Control target must be alive in its owning simulation.");
            if (target.Equals(Current))
                return;
            if (entities.IdentityMode == IdentityMode.StableWorld && entities.Link(target).Equals(CurrentLink))
            {
                Current = target;
                return;
            }
            EntityId previous = Current;
            EntityLink previousLink = CurrentLink;
            Current = target;
            if (entities.IdentityMode == IdentityMode.StableWorld)
            {
                CurrentLink = entities.Link(target);
                context.Emit(new StableSimEvent(switchType,previousLink,CurrentLink));
            }
            else
                context.Emit(new SimEvent(switchType, source: previous, target: target));
        }

        public void Step(SimContext context)
        {
            if (context.Entities != entities)
                throw new InvalidOperationException("Control system belongs to another simulation.");
            // Cold reactivation changes the local generation while the captured identity stays the same.
            if (entities.IdentityMode == IdentityMode.StableWorld)
                Current = CurrentLink.HasValue && entities.TryResolve(CurrentLink.Id,out EntityRef target) ? (EntityId)target : default;
            InputFrame input = context.Input;
            if (entities.IsAlive(Current))
                handler.Apply(context, Current, in input);
        }

        public void WriteState(StateHash hash)
        {
            hash.Add(StateTypeId);
            switchType.WriteState(hash);
            Current.WriteState(hash);
            handler.WriteState(hash);
        }

        public void SaveState(SimStateWriter writer)
        {
            writer.Int32(switchType.Value);
            if (writer.IsStable)
            {
                writer.Link(CurrentLink);
                writer.Section(handler);
            }
            else
            {
                SnapshotValues.Entity(writer,Current);
                handler.SaveState(writer);
            }
        }

        public void LoadState(SimStateReader reader)
        {
            SimStateReader.Require(reader.Int32() == switchType.Value, "Control event registration mismatch.");
            if (reader.IsStable)
            {
                CurrentLink = reader.Link();
                Current = CurrentLink.HasValue && entities.TryResolve(CurrentLink.Id,out EntityRef target) ? (EntityId)target : default;
                var section = reader.Section();
                handler.LoadState(section);
                section.End();
            }
            else
            {
                Current = SnapshotValues.Entity(reader);
                handler.LoadState(reader);
            }
        }
    }
}
