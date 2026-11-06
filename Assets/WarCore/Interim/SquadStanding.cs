using WarCore.Sim;

namespace WarCore.Interim
{
    public enum StandingKind { Move = 0, Hold = 1, Follow = 2 }

    // The squad command a unit is carrying out, kept after the one-tick command is gone. It exists only in worlds whose
    // recipe opens it, so every other world keeps its exact snapshots and hashes. A unit that has it is not steered by the
    // match AI. Target is the stable identity of the followed unit (Follow only; zero otherwise).
    public readonly struct SquadStanding : IStateHash
    {
        public const string ComponentId = "interim.standing";
        public StandingKind Kind { get; }
        public ulong Target { get; }
        public SquadStanding(StandingKind kind, ulong target = 0)
        {
            if (kind < StandingKind.Move || kind > StandingKind.Follow) throw new System.ArgumentOutOfRangeException(nameof(kind));
            Kind = kind; Target = target;
        }
        public void WriteState(StateHash hash) { hash.Add((int)Kind); hash.Add(Target); }
    }

    public sealed class SquadStandingSerializer : IStableComponentSerializer<SquadStanding>
    {
        public int TypeId => 2006;
        public int Version => 1;
        public void Write(SimStateWriter writer, in SquadStanding value) { writer.Int32((int)value.Kind); writer.UInt64(value.Target); }
        public SquadStanding Read(SimStateReader reader) => new SquadStanding((StandingKind)reader.Int32(), reader.UInt64());
    }
}
