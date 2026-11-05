using System;

namespace WarCore.Sim
{
    public readonly struct TriggerDefinition : IStateHash
    {
        public int Id { get; }
        public int ConditionType { get; }
        public int ActionType { get; }
        public EntityId Subject { get; }
        public EntityLink SubjectLink { get; }
        public EntityLink TargetLink { get; }
        internal bool StableLinks { get; }
        public EntityId Target { get; }
        public long ConditionArgument { get; }
        public long ActionArgument { get; }
        public TriggerDefinition(int id, int conditionType, int actionType, EntityId subject = default,
            EntityId target = default, long conditionArgument = 0, long actionArgument = 0) : this()
        {
            if (id <= 0 || conditionType <= 0 || actionType <= 0)
                throw new ArgumentOutOfRangeException(nameof(id), "Trigger and operation ids must be positive.");
            Id = id;
            ConditionType = conditionType;
            ActionType = actionType;
            Subject = subject;
            Target = target;
            ConditionArgument = conditionArgument;
            ActionArgument = actionArgument;
        }

        private TriggerDefinition(int id, int conditionType, int actionType, EntityLink subject, EntityLink target, long conditionArgument, long actionArgument)
            : this(id,conditionType,actionType,default(EntityId),default(EntityId),conditionArgument,actionArgument)
        {
            SubjectLink = subject;
            TargetLink = target;
            StableLinks = true;
        }
        public static TriggerDefinition Stable(int id, int conditionType, int actionType, EntityLink subject = default,
            EntityLink target = default, long conditionArgument = 0, long actionArgument = 0)
            => new TriggerDefinition(id,conditionType,actionType,subject,target,conditionArgument,actionArgument);

        public void WriteState(StateHash hash)
        {
            hash.Add(Id);
            hash.Add(ConditionType);
            hash.Add(ActionType);
            Subject.WriteState(hash);
            Target.WriteState(hash);
            hash.Add(ConditionArgument);
            hash.Add(ActionArgument);
        }

        internal void Save(SimStateWriter writer)
        {
            writer.Int32(Id);
            writer.Int32(ConditionType);
            writer.Int32(ActionType);
            if (writer.IsStable)
            {
                if (!StableLinks)
                    throw new SimBoundaryException("Stable trigger definitions require captured entity links.");
                writer.Link(SubjectLink);
                writer.Link(TargetLink);
            }
            else
            {
                SnapshotValues.Entity(writer,Subject);
                SnapshotValues.Entity(writer,Target);
            }
            writer.Int64(ConditionArgument);
            writer.Int64(ActionArgument);
        }

        internal static TriggerDefinition Read(SimStateReader reader) => reader.IsStable
            ? Stable(reader.Int32(),reader.Int32(),reader.Int32(),reader.Link(),reader.Link(),reader.Int64(),reader.Int64())
            : new TriggerDefinition(reader.Int32(),reader.Int32(),reader.Int32(),SnapshotValues.Entity(reader),SnapshotValues.Entity(reader),reader.Int64(),reader.Int64());
    }

    public interface ITrigger
    {
        TriggerDefinition Definition { get; }
        bool Fired { get; }
        long FiredTick { get; }
    }

    public readonly struct Trigger : ITrigger, IStateHash
    {
        public TriggerDefinition Definition { get; }
        public bool Fired { get; }
        public long FiredTick { get; }
        public Trigger(TriggerDefinition definition, bool fired = false, long firedTick = 0)
        {
            if (definition.Id <= 0 || firedTick < 0 || fired != (firedTick > 0))
                throw new ArgumentException("Invalid trigger state.");
            Definition = definition;
            Fired = fired;
            FiredTick = firedTick;
        }

        public void WriteState(StateHash hash)
        {
            Definition.WriteState(hash);
            hash.Add(Fired);
            hash.Add(FiredTick);
        }
    }

    public interface ITriggerOperation : IStateHash, ISimStateSerializer
    {
    }

    public interface ITriggerCondition : ITriggerOperation
    {
        // There is deliberately no event reader: conditions observe durable simulation state.
        bool Evaluate(SimContext context, in TriggerDefinition definition);
    }

    public interface ITriggerAction : ITriggerOperation
    {
        void Execute(SimContext context, in TriggerDefinition definition);
    }

    public sealed class TriggerSystem : ISimSystem, IStableStateSerializer
    {
        private readonly TriggerRegistry registry;
        private Trigger[] triggers;
        private ITriggerCondition[] conditions;
        private ITriggerAction[] actions;
        public int Order { get; }
        public int Count => triggers.Length;
        public string StateTypeId { get; }
        public int StateVersion => 1;
        public TriggerSystem(TriggerRegistry registry, int order, params TriggerDefinition[] definitions)
        {
            this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
            if (definitions == null)
                throw new ArgumentNullException(nameof(definitions));
            registry.Freeze();
            Order = order;
            StateTypeId = "warcore.triggers/" + registry.ManifestId;
            triggers = new Trigger[definitions.Length];
            for (int i = 0; i < definitions.Length; i++)
                triggers[i] = new Trigger(definitions[i]);
            Bind();
        }

        public Trigger Get(int index) => triggers[index];
        private void Bind()
        {
            conditions = new ITriggerCondition[triggers.Length];
            actions = new ITriggerAction[triggers.Length];
            int previous = 0;
            for (int i = 0; i < triggers.Length; i++)
            {
                TriggerDefinition definition = triggers[i].Definition;
                if (definition.Id <= previous)
                    throw new InvalidOperationException("Trigger ids must be unique, positive and sorted.");
                previous = definition.Id;
                conditions[i] = registry.Condition(definition.ConditionType);
                actions[i] = registry.Action(definition.ActionType);
            }
        }

        public void Step(SimContext context)
        {
            for (int i = 0; i < triggers.Length; i++)
            {
                Trigger value = triggers[i];
                TriggerDefinition definition = value.Definition;
                if (value.Fired || !conditions[i].Evaluate(context, in definition))
                    continue;
                triggers[i] = new Trigger(definition, true, context.Clock.Tick);
                actions[i].Execute(context, in definition);
            }
        }

        public void WriteState(StateHash hash)
        {
            registry.WriteState(hash);
            hash.Add(triggers.Length);
            foreach (Trigger trigger in triggers)
                trigger.WriteState(hash);
        }

        public void SaveState(SimStateWriter writer)
        {
            registry.SaveState(writer);
            writer.Int32(triggers.Length);
            foreach (Trigger trigger in triggers)
            {
                trigger.Definition.Save(writer);
                writer.Bool(trigger.Fired);
                writer.Int64(trigger.FiredTick);
            }
        }

        public void LoadState(SimStateReader reader)
        {
            registry.LoadState(reader);
            var restored = new Trigger[reader.Count(reader.Remaining / 61)];
            for (int i = 0; i < restored.Length; i++)
                restored[i] = new Trigger(TriggerDefinition.Read(reader), reader.Bool(), reader.Int64());
            triggers = restored;
            Bind();
        }
    }
}
