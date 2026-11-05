using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    public struct Facing : IStateHash
    {
        public SimVec3 Direction;
        public void WriteState(StateHash hash)
        {
            Direction.WriteState(hash);
        }
    }

    public struct Health : IStateHash
    {
        public int Value;
        public void WriteState(StateHash hash)
        {
            hash.Add(Value);
        }
    }

    public sealed class InputSystem : ISimSystem, ISimStateSerializer
    {
        private readonly EntityId entity;
        private readonly ComponentType<Position> positions;
        private readonly ComponentType<Health> healths;
        private readonly ComponentType<Facing> facings;
        private readonly RandomStreamId combat;
        public int Order => 10;
        public string StateTypeId => "test.input";
        public int StateVersion => 1;
        public void SaveState(SimStateWriter writer) => SnapshotValues.Entity(writer, entity);
        public void LoadState(SimStateReader reader)
        {
            SimStateReader.Require(SnapshotValues.Entity(reader).Equals(entity), "Input entity configuration mismatch.");
        }
        public InputSystem(Simulation sim, EntityId entity)
        {
            this.entity = entity;
            positions = sim.Entities.Resolve<Position>();
            healths = sim.Entities.Resolve<Health>();
            facings = sim.Entities.Resolve<Facing>();
            combat = sim.Rng.Stream("combat");
        }

        public void Step(SimContext context)
        {
            ref Position position = ref context.Entities.Get(positions, entity);
            position.Value += context.Input.Move;
            context.Entities.Set(facings, entity, new Facing{Direction = context.Input.Aim});
            while (context.Scheduler.TryDequeue(out _))
            {
            }

            if (context.Input.PrimaryAttack)
            {
                ref Health health = ref context.Entities.Get(healths, entity);
                health.Value -= context.Rng.NextInt(combat, 3);
                context.Emit(new SimEvent(new EventTypeId(Fixtures.Attack), source: entity, tag: health.Value));
            }

            if (context.Input.Throw)
                context.Scheduler.Schedule(context.Clock.Tick + 3, new SimEvent(new EventTypeId(Fixtures.Throw), source: entity));
            if (context.Input.Block)
                context.Emit(new SimEvent(new EventTypeId(Fixtures.Block), source: entity));
            if (context.Input.SkillSlot != 0)
                context.Emit(new SimEvent(new EventTypeId(Fixtures.Skill), source: entity, tag: context.Input.SkillSlot));
            if (context.Input.Squad.HasValue)
                context.Emit(new SimEvent(new EventTypeId(Fixtures.Squad), source: entity, tag: (int)context.Input.Squad.Value.Kind));
        }

        public void WriteState(StateHash hash)
        {
            entity.WriteState(hash);
        }
    }

    public sealed class PulseSystem : ISimSystem, ISimStateSerializer
    {
        private int count;
        public int Order => 20;
        public int Count => count;
        public string StateTypeId => "test.pulse";
        public int StateVersion => 1;
        public void SaveState(SimStateWriter writer) => writer.Int32(count);
        public void LoadState(SimStateReader reader)
        {
            count = reader.Int32();
        }
        public void Step(SimContext context)
        {
            count++;
            if (count % 7 == 0)
                context.Emit(new SimEvent(new EventTypeId(Fixtures.Pulse), tag: context.Rng.NextInt(context.Rng.Stream("ai"), 100)));
        }

        public void WriteState(StateHash hash)
        {
            hash.Add(count);
        }
    }

    public static class Fixtures
    {
        public const int Attack = 10;
        public const int Throw = 11;
        public const int Block = 12;
        public const int Skill = 13;
        public const int Squad = 14;
        public const int Pulse = 15;
        public static EventTypeId Type(string name) => new EventTypeRegistry().Register(name);
        public static Simulation Empty(ulong seed) => new Simulation(seed, eventBuffer: new SimEventBuffer(8, 64, 256));
        public static Simulation Create(ulong seed)
        {
            var sim = Empty(seed);
            sim.Entities.Register<Position>("position", new PositionSerializer());
            sim.Entities.Register<Facing>("facing", new FacingSerializer());
            sim.Entities.Register<Health>("health", new HealthSerializer());
            sim.Rng.Stream("ai");
            EntityId entity = sim.Entities.Create();
            sim.Entities.Set(entity, new Position());
            sim.Entities.Set(entity, new Facing());
            sim.Entities.Set(entity, new Health{Value = 10000});
            sim.Register("pulse", new PulseSystem());
            sim.Register("input", new InputSystem(sim, entity));
            return sim;
        }

        public static InputFrame Input(long tick) => new InputFrame(SimVec3.Metres(tick % 3 - 1, tick % 5 - 2, 0), SimVec3.Metres(1, tick % 2, 0), tick % 3 == 0, tick % 11 == 0, tick % 17 == 0, (int)(tick % 4), tick % 19 == 0 ? new SquadCommand(SquadCommandKind.Follow, 12, SimVec3.Metres(3, 4, 0), new EntityId(0, 1)) : (SquadCommand? )null);
        public static ulong Hash(IStateHash state)
        {
            var hash = new StateHash();
            state.WriteState(hash);
            return hash.Value;
        }

        public static ulong Authority(Simulation sim)
        {
            var hash = new StateHash();
            sim.WriteAuthoritativeState(hash);
            return hash.Value;
        }
    }
}
