using WarCore.Sim;

namespace WarCore.Tests
{
    internal sealed class ProbeInput : IControlInputHandler
    {
        private readonly ComponentType<MassClass> masses;
        internal int Calls;
        internal EntityId LastTarget;
        internal InputFrame LastInput;
        public string StateTypeId => "test.control-input";
        public int StateVersion => 1;
        internal ProbeInput(ComponentType<MassClass> masses)
        {
            this.masses = masses;
        }

        public void Apply(SimContext context, EntityId target, in InputFrame input)
        {
            context.Entities.Set(masses, target, new MassClass(input.SkillSlot));
            LastTarget = target;
            LastInput = input;
            Calls++;
        }

        public void WriteState(StateHash hash)
        {
            hash.Add(Calls);
            LastTarget.WriteState(hash);
            LastInput.WriteState(hash);
        }

        public void SaveState(SimStateWriter writer)
        {
            writer.Int32(Calls);
            SnapshotValues.Entity(writer, LastTarget);
            SnapshotValues.Input(writer, LastInput);
        }

        public void LoadState(SimStateReader reader)
        {
            Calls = reader.Int32();
            LastTarget = SnapshotValues.Entity(reader);
            LastInput = SnapshotValues.Input(reader);
        }
    }

    internal sealed class SwitchCommand : ISimSystem, ISimStateSerializer
    {
        private readonly ControlTargetSystem control;
        private readonly EntityId alternate;
        internal bool Flood;
        public int Order => 15;
        public string StateTypeId => "test.control-command";
        public int StateVersion => 1;
        internal SwitchCommand(ControlTargetSystem control, EntityId alternate)
        {
            this.control = control;
            this.alternate = alternate;
        }

        public void Step(SimContext context)
        {
            if (context.Input.SkillSlot == 2)
                control.Switch(context, alternate);
            if (Flood)
                for (int i = 0; i < 24; i++)
                    context.Emit(new SimEvent(new EventTypeId(812), tag: i));
        }

        public void WriteState(StateHash hash)
        {
            alternate.WriteState(hash);
            hash.Add(Flood);
        }

        public void SaveState(SimStateWriter writer)
        {
            SnapshotValues.Entity(writer, alternate);
            writer.Bool(Flood);
        }

        public void LoadState(SimStateReader reader)
        {
            SimStateReader.Require(SnapshotValues.Entity(reader).Equals(alternate), "Command target mismatch.");
            Flood = reader.Bool();
        }
    }

    internal sealed class ProbeCondition : ITriggerCondition
    {
        private readonly ComponentType<StructureHealth> healths;
        private readonly int kind;
        public string StateTypeId => "test.condition/" + kind;
        public int StateVersion => 1;
        internal ProbeCondition(int kind, ComponentType<StructureHealth> healths = default)
        {
            this.kind = kind;
            this.healths = healths;
        }

        public bool Evaluate(SimContext context, in TriggerDefinition definition)
        {
            if (kind == 1)
                return context.Clock.Tick >= definition.ConditionArgument;
            if (kind == 2)
                return !context.Entities.IsAlive(definition.Subject);
            if (kind == 3)
                return context.Entities.Get(healths, definition.Subject).Collapsed;
            int living = 0;
            foreach (EntityId entity in context.Entities)
                living++;
            return living < definition.ConditionArgument;
        }

        public void WriteState(StateHash hash) => hash.Add(kind);
        public void SaveState(SimStateWriter writer) => writer.Int32(kind);
        public void LoadState(SimStateReader reader) => SimStateReader.Require(reader.Int32() == kind, "Condition kind mismatch.");
    }

    internal sealed class ProbeAction : ITriggerAction
    {
        private readonly ComponentType<Container> containers;
        internal int Calls;
        internal ProbeAction(ComponentType<Container> containers)
        {
            this.containers = containers;
        }

        public string StateTypeId => "test.container-action";
        public int StateVersion => 1;
        public void Execute(SimContext context, in TriggerDefinition definition)
        {
            Container value = context.Entities.Get(containers, definition.Target);
            context.Entities.Set(containers, definition.Target, new Container(value.Capacity, value.Count + (int)definition.ActionArgument));
            Calls++;
        }

        public void WriteState(StateHash hash) => hash.Add(Calls);
        public void SaveState(SimStateWriter writer) => writer.Int32(Calls);
        public void LoadState(SimStateReader reader)
        {
            Calls = reader.Int32();
        }
    }

    internal sealed class ExtensionFixture
    {
        internal readonly PostureFixture P = new PostureFixture();
        internal readonly ComponentType<Seat> Seats;
        internal readonly ComponentType<Occupant> Occupants;
        internal readonly ComponentType<Container> Containers;
        internal readonly ComponentType<StructureHealth> Healths;
        internal readonly ComponentType<MassClass> Masses;
        internal readonly EntityId Second;
        internal readonly EntityId Recycled;
        internal readonly EntityId Free;
        internal readonly ProbeInput Input;
        internal readonly ControlTargetSystem Control;
        internal readonly SwitchCommand Commands;
        internal readonly ProbeAction Action;
        internal readonly TriggerSystem Triggers;
        internal readonly EventTypeId SwitchType = new EventTypeRegistry().Register("control-switched");
        internal ExtensionFixture(int actionType = 101)
        {
            var sim = P.Sim;
            Seats = sim.Entities.Register<Seat>("seat", new SeatSerializer());
            Occupants = sim.Entities.Register<Occupant>("occupant", new OccupantSerializer());
            Containers = sim.Entities.Register<Container>("container", new ContainerSerializer());
            Healths = sim.Entities.Register<StructureHealth>("structure", new StructureHealthSerializer());
            Masses = sim.Entities.Register<MassClass>("mass", new MassClassSerializer());
            Second = P.Add(3, 0, 2);
            EntityId old = P.Add(6, 0, 1);
            sim.Entities.Destroy(old);
            sim.CompleteSpatialBoundary();
            Recycled = P.Add(6, 0, 3);
            Free = P.Add(9, 0, 1);
            sim.Entities.Destroy(Free);
            sim.Entities.Set(Seats, Second, new Seat(Second, 2, P.Unit));
            sim.Entities.Set(Occupants, P.Unit, new Occupant(Second));
            sim.Entities.Set(Containers, P.Unit, new Container(100, 1));
            sim.Entities.Set(Containers, Second, new Container(100, 3));
            sim.Entities.Set(Healths, Second, new StructureHealth(SimFix.FromInt(4), SimFix.FromInt(5)));
            sim.Entities.Set(Masses, P.Unit, new MassClass(3));
            sim.Entities.Set(Masses, Second, new MassClass(4));
            Input = new ProbeInput(Masses);
            Control = new ControlTargetSystem(sim.Entities, P.Unit, Input, SwitchType, 20);
            Commands = new SwitchCommand(Control, Second);
            sim.Register("commands", Commands);
            sim.Register("control", Control);
            sim.Register("structures", new StructureSystem(sim.Entities, Healths, P.Positions, P.Colliders, 71, 30));
            var registry = new TriggerRegistry();
            registry.RegisterCondition(1, new ProbeCondition(1));
            registry.RegisterCondition(2, new ProbeCondition(2));
            registry.RegisterCondition(3, new ProbeCondition(3, Healths));
            Action = new ProbeAction(Containers);
            registry.RegisterAction(actionType, Action);
            Triggers = new TriggerSystem(registry, 40,
                new TriggerDefinition(1, 3, actionType, Second, P.Unit, actionArgument: 2),
                new TriggerDefinition(2, 1, actionType, target: Second, conditionArgument: 5, actionArgument: 4),
                new TriggerDefinition(3, 2, actionType, Recycled, P.Unit, actionArgument: 1));
            sim.Register("triggers", Triggers);
            P.Grid.Rebuild();
        }
    }
}
