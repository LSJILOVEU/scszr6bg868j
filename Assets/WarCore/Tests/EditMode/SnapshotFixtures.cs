using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    internal sealed class HealthSerializer : IComponentSerializer<Health>
    {
        public int TypeId => 1001;
        public int Version => 1;
        public void Write(SimStateWriter writer, in Health value) => writer.Int32(value.Value);
        public Health Read(SimStateReader reader) => new Health { Value = reader.Int32() };
    }

    internal sealed class FacingSerializer : IComponentSerializer<Facing>
    {
        public int TypeId => 1002;
        public int Version => 1;
        public void Write(SimStateWriter writer, in Facing value) => SnapshotValues.Vector(writer, value.Direction);
        public Facing Read(SimStateReader reader) => new Facing { Direction = SnapshotValues.Vector(reader) };
    }

    internal sealed class SnapshotMode : IMatchMode, ISimStateSerializer
    {
        public int Starts;
        public int Steps;
        public int FinishAt = int.MaxValue;
        public bool IsFinished => Steps >= FinishAt;
        public string StateTypeId => "test.snapshot-mode";
        public int StateVersion => 1;
        public void Start(SimContext context)
        {
            Starts++;
        }

        public void Step(SimContext context)
        {
            Steps++;
        }

        public void WriteState(StateHash hash)
        {
            hash.Add(Starts);
            hash.Add(Steps);
            hash.Add(FinishAt);
        }

        public void SaveState(SimStateWriter writer)
        {
            writer.Int32(Starts);
            writer.Int32(Steps);
            writer.Int32(FinishAt);
        }

        public void LoadState(SimStateReader reader)
        {
            Starts = reader.Int32();
            Steps = reader.Int32();
            FinishAt = reader.Int32();
        }
    }

    internal sealed class SnapshotOutput : ISimSystem, ISimStateSerializer
    {
        public int Count;
        public int Order => 1;
        public string StateTypeId => "test.snapshot-output";
        public int StateVersion => 1;
        public void Step(SimContext context)
        {
            Count++;
            for (int i = 0; i < 4; i++)
                context.Emit(new SimEvent(new EventTypeId(70), source: new EntityId(2, 3), target: new EntityId(4, 5),
                    position: SimVec3.Metres(1, 2, 3), aux: SimVec3.Metres(-4, 5, 6),
                    magnitude: SimFix.FromRaw(17), radius: SimFix.FromRaw(19), tag: Count * 4 + i));
        }

        public void WriteState(StateHash hash) => hash.Add(Count);
        public void SaveState(SimStateWriter writer) => writer.Int32(Count);
        public void LoadState(SimStateReader reader)
        {
            Count = reader.Int32();
        }
    }

    internal static class SnapshotFixtures
    {
        internal static SimEvent Payload(int tag) => new SimEvent(new EventTypeId(741),
            source: new EntityId(2, 3), target: new EntityId(4, 5), position: SimVec3.Metres(tag, 2, 3),
            aux: SimVec3.Metres(4, 5, 6), magnitude: SimFix.Ratio(3, 2), radius: SimFix.FromInt(7), tag: tag);

        internal static Simulation A3(bool reverse)
        {
            var sim = new Simulation(123, config: new SimConfig(3, 8, 8, 4, 8));
            int[] ticks = { 2, 2, 2, 3, 2 };
            for (int i = 0; i < ticks.Length; i++)
                sim.Scheduler.Schedule(ticks[i], Payload(i + 1));
            sim.Scheduler.Cancel(reverse ? 3 : 1);
            sim.Scheduler.Cancel(reverse ? 1 : 3);
            sim.Step(default);
            return sim;
        }
    }
}
