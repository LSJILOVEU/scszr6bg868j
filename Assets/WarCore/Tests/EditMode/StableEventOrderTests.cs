using System;
using NUnit.Framework;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    [TestFixture, Category("StableIdentity")]
    public sealed class StableEventOrderTests
    {
        private readonly struct Draw : IStateHash
        {
            internal readonly ulong Value;
            internal Draw(ulong value) => Value = value;
            public void WriteState(StateHash hash) => hash.Add(Value);
        }
        private sealed class DrawSerializer : IStableComponentSerializer<Draw>
        {
            public int TypeId => 1100;
            public int Version => 1;
            public void Write(SimStateWriter writer,in Draw value) => writer.UInt64(value.Value);
            public Draw Read(SimStateReader reader) => new Draw(reader.UInt64());
        }
        private sealed class Batch : ISimSystem, IStableStateSerializer
        {
            private readonly bool reverse;
            private readonly EntityStage stage;
            private readonly ComponentType<Draw> draws;
            private readonly RandomStreamId stream;
            private readonly StableProducedEvent[] buffer = new StableProducedEvent[24];
            public int Order => 1;
            public string StateTypeId => "test.stable-batch";
            public int StateVersion => 1;
            internal Batch(Simulation sim,bool reverse,ComponentType<Draw> draws)
            {
                this.reverse = reverse;
                this.draws = draws;
                stage = new EntityStage(sim.Entities);
                stream = sim.Rng.Stream("shared");
            }
            public void Step(SimContext context)
            {
                int count = 0;
                foreach (EntityId entity in stage.Capture())
                {
                    EntityLink source = context.Entities.Link(entity);
                    ulong draw = context.Rng.NextUInt64(stream);
                    context.Entities.Set(draws,entity,new Draw(draw));
                    int at = reverse ? buffer.Length-count-1 : count;
                    buffer[at] = new StableProducedEvent(new StableRequestKey(1,0,source.Id,1,0,100),
                        new StableSimEvent(new EventTypeId(100),source,tag:(int)(draw%1000)));
                    count++;
                }
                context.EmitStableBatch(buffer);
            }
            public void WriteState(StateHash hash) { }
            public void SaveState(SimStateWriter writer) { }
            public void LoadState(SimStateReader reader) { }
        }
        private static Simulation Build(bool reverse)
        {
            var sim = new Simulation(913,config:new SimConfig(entityCapacity:24,eventInitialCapacity:5,eventHardLimit:5),
                identityMode:IdentityMode.StableWorld);
            var positions = sim.Entities.Register<Position>("p",new PositionSerializer());
            var colliders = sim.Entities.Register<Collider>("c",new ColliderSerializer());
            var health = sim.Entities.Register<StructureHealth>("h",new StructureHealthSerializer());
            var draws = sim.Entities.Register<Draw>("draw",new DrawSerializer());
            for (int i = 0; i < 24; i++)
            {
                int logical = reverse ? 23-i : i;
                EntityId entity = sim.Entities.Create(StableEntityKey.Placed(1,(ulong)logical));
                sim.Entities.Set(positions,entity,new Position(SimVec3.Metres(logical,0,0)));
                sim.Entities.Set(colliders,entity,new Collider(SimFix.FromInt(1),SimFix.FromInt(2),1,isStatic:logical%2==0));
                sim.Entities.Set(health,entity,new StructureHealth(default,SimFix.FromInt(1)));
            }
            sim.Register("structure",new StructureSystem(sim.Entities,health,positions,colliders,10,0));
            sim.Register("random-and-batch",new Batch(sim,reverse,draws));
            return sim;
        }
        [Test]
        public void SharedRandomStructureCollapseAndOverflowCompareFullLogicalRecords()
        {
            Simulation a = Build(false);
            Simulation b = Build(true);
            var meter = new AllocationMeter();
            for (int tick = 1; tick <= 300; tick++)
            {
                var before = meter.Read();
                a.Step(default);
                b.Step(default);
                if (tick > 32 && meter.Basis == AllocationBasis.Direct)
                    Assert.That(meter.Difference(before).Bytes,Is.Zero);
                Assert.That(a.StateHashStable(),Is.EqualTo(b.StateHashStable()));
                Assert.That(a.Context.Rng.NextUInt64(new RandomStreamId("shared")),
                    Is.EqualTo(b.Context.Rng.NextUInt64(new RandomStreamId("shared"))));
                for (ulong i = 0; i < 24; i++)
                {
                    var link = new EntityLink(StableEntityKey.Placed(1,i).Id);
                    Assert.That(a.Entities.Read(a.Entities.Resolve<Draw>(),a.Entities.Resolve(link)).Value,
                        Is.EqualTo(b.Entities.Read(b.Entities.Resolve<Draw>(),b.Entities.Resolve(link)).Value));
                }
                Assert.That(a.Events.Read(tick).Length,Is.EqualTo(6));
                Assert.That(a.Events.EventsDroppedThisTick,Is.EqualTo(tick == 1 ? 43 : 19));
                for (int i = 0; i < a.Events.Read(tick).Length; i++)
                {
                    StableSimEvent x = a.ReadStableEvent(tick,i);
                    StableSimEvent y = b.ReadStableEvent(tick,i);
                    Assert.That(x.Source,Is.EqualTo(y.Source));
                    Assert.That(x.Target,Is.EqualTo(y.Target));
                    Assert.That(x.Sequence,Is.EqualTo(y.Sequence));
                    Assert.That(x.Payload.Position,Is.EqualTo(y.Payload.Position));
                    Assert.That(x.Tag,Is.EqualTo(y.Tag));
                }
                if (tick == 32)
                    b.RestoreStableSnapshot(a.SaveStableSnapshot(),true);
            }
        }
        [Test]
        public void EventBatchRejectsDuplicateKeysBeforePublishingAnyEvent()
        {
            var sim = new Simulation(1,identityMode:IdentityMode.StableWorld);
            var key = new StableRequestKey(1,1,new StableId(1),1,1,1);
            var values = new[] { new StableProducedEvent(key,new StableSimEvent(new EventTypeId(100))),
                new StableProducedEvent(key,new StableSimEvent(new EventTypeId(100))) };
            ((SimEventBuffer)sim.Events).BeginTick(1);
            sim.Context.Begin(default);
            Assert.Throws<SimBoundaryException>(() => sim.Context.EmitStableBatch(values));
            sim.Context.End();
            Assert.That(sim.Events.Read(1).Length,Is.Zero);
            Assert.That(sim.Events.NextSequence,Is.EqualTo(1));
            ((SimEventBuffer)sim.Events).EndTick();
        }
    }
}
