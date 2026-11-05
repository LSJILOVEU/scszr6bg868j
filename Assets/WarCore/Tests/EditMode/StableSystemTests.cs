using NUnit.Framework;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    [TestFixture, Category("StableIdentity")]
    public sealed class StableSystemTests
    {
        internal sealed class Handler : IControlInputHandler, IStableStateSerializer
        {
            public string StateTypeId => "test.stable-control";
            public int StateVersion => 1;
            public void Apply(SimContext context,EntityId entity,in InputFrame input) { }
            public void SaveState(SimStateWriter writer) { }
            public void LoadState(SimStateReader reader) { }
            public void WriteState(StateHash hash) { }
        }
        internal sealed class Condition : ITriggerCondition, IStableStateSerializer
        {
            public string StateTypeId => "test.stable-condition";
            public int StateVersion => 1;
            public bool Evaluate(SimContext context,in TriggerDefinition definition) => context.Clock.Tick == definition.ConditionArgument;
            public void SaveState(SimStateWriter writer) { }
            public void LoadState(SimStateReader reader) { }
            public void WriteState(StateHash hash) { }
        }
        internal sealed class Action : ITriggerAction, IStableStateSerializer
        {
            public string StateTypeId => "test.stable-action";
            public int StateVersion => 1;
            public void Execute(SimContext context,in TriggerDefinition definition)
            {
                context.Emit(new StableSimEvent(new EventTypeId(500),definition.SubjectLink,definition.TargetLink));
                context.Entities.Destroy(context.Entities.Resolve(definition.SubjectLink));
            }
            public void SaveState(SimStateWriter writer) { }
            public void LoadState(SimStateReader reader) { }
            public void WriteState(StateHash hash) { }
        }
        internal static Simulation Make(bool reverse)
        {
            var sim = new Simulation(12,config:new SimConfig(entityCapacity:8),identityMode:IdentityMode.StableWorld,identityCapacity:16);
            var positions = sim.Entities.Register<Position>("position",new PositionSerializer());
            var colliders = sim.Entities.Register<Collider>("collider",new ColliderSerializer());
            var postures = sim.Entities.Register<Posture>("posture",new PostureSerializer());
            for (int i = 0; i < 3; i++)
            {
                EntityId entity = sim.Entities.Create(StableEntityKey.Placed(1,(ulong)(reverse ? 2-i : i)));
                sim.Entities.Set(positions,entity,new Position(SimVec3.Metres(0,1,0)));
                sim.Entities.Set(colliders,entity,new Collider(SimFix.FromRaw(SimFix.Scale/4),SimFix.FromInt(2)));
            }
            var link = new EntityLink(StableEntityKey.Placed(1,0).Id);
            var target = new EntityLink(StableEntityKey.Placed(1,1).Id);
            var geometry = new PostureGeometry(sim.Entities,postures,new PostureCatalog(
                new PostureDef(1,SimFix.FromInt(1),SimFix.FromInt(1),SimFix.FromInt(1),SimFix.FromInt(1))));
            sim.ConfigureSpatial(positions,colliders,SimFix.FromInt(4),postures:geometry);
            var posture = new PostureSystem(sim.Entities,postures,positions,colliders,geometry,new EventTypeId(400),1);
            sim.Register("posture",posture);
            posture.BeginStableTransition(sim.Context,target,1,5,99);
            sim.Register("control",new ControlTargetSystem(sim.Entities,sim.Entities.Resolve(link),new Handler(),new EventTypeId(401),2));
            var registry = new TriggerRegistry();
            registry.RegisterCondition(1,new Condition());
            registry.RegisterAction(1,new Action());
            sim.Register("trigger",new TriggerSystem(registry,3,
                TriggerDefinition.Stable(1,1,1,link,target,7,0)));
            return sim;
        }
        [Test]
        public void TaskKeysControlTriggersAndDeadEventsRemainStableAfterRemapping()
        {
            Simulation a = Make(false);
            Simulation b = Make(true);
            for (int tick = 1; tick <= 300; tick++)
            {
                a.Step(default);
                b.Step(default);
                if (tick == 2 || tick == 7)
                    b.RestoreStableSnapshot(a.SaveStableSnapshot(),true);
                Assert.That(a.StateHashStable(),Is.EqualTo(b.StateHashStable()),"tick="+tick);
                if (tick == 7)
                {
                    var item = b.ReadStableEvent(7,0);
                    Assert.That(item.Source.Id,Is.EqualTo(StableEntityKey.Placed(1,0).Id));
                    Assert.That(b.Entities.TryResolve(item.Source.Id,out _),Is.False);
                }
            }
        }
    }
}
