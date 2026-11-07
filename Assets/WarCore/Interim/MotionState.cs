using WarCore.Sim;

namespace WarCore.Interim
{
    // What the inertia model answered a unit last: the step length it returned and the tick it returned it. It exists only in
    // worlds that open the inertia option with a non-zero rate, so every other world keeps its exact snapshots and hashes.
    // A unit starts at rest and unasked: Speed 0, Tick -1.
    public readonly struct MotionState : IStateHash
    {
        public const string ComponentId = "interim.motion-state";
        public static MotionState Initial => new MotionState(default, -1);
        public SimFix Speed { get; }
        public long Tick { get; }
        public MotionState(SimFix speed, long tick) { Speed = speed; Tick = tick; }
        public void WriteState(StateHash hash) { hash.Add(Speed.Raw); hash.Add(Tick); }
    }

    public sealed class MotionStateSerializer : IStableComponentSerializer<MotionState>
    {
        public int TypeId => 2007;
        public int Version => 1;
        public void Write(SimStateWriter writer, in MotionState value) { writer.Int64(value.Speed.Raw); writer.Int64(value.Tick); }
        public MotionState Read(SimStateReader reader) => new MotionState(SimFix.FromRaw(reader.Int64()), reader.Int64());
    }
}
