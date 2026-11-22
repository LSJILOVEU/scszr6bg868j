using WarCore.Sim;

namespace WarCore.Combat
{
    // DOWN-1: a unit that is alive but cannot fight. Kind: Stunned (1) or Downed (2). Start: the tick it began. Due: for a downed
    // unit the tick it bleeds out, for a stunned one the first tick it is awake again. A unit without the component is neither
    // (as a unit without a posture stands): it is added when the unit goes down or is stunned and removed when that ends.
    public readonly struct Casualty : IStateHash
    {
        public const string ComponentId = "combat.casualty";
        public const byte Stunned = 1, Downed = 2;

        public readonly byte Kind;
        public readonly long Start, Due;

        public Casualty(byte kind, long start, long due) { Kind = kind; Start = start; Due = due; }

        public void WriteState(StateHash hash) { hash.Add(Kind); hash.Add(Start); hash.Add(Due); }

        // Registers the component on a store; call it with the other components.
        public static ComponentType<Casualty> Register(EntityStore entities) =>
            entities.Register<Casualty>(ComponentId, new CasualtySerializer());
    }

    public sealed class CasualtySerializer : IStableComponentSerializer<Casualty>
    {
        public int TypeId => 3004;
        public int Version => 1;

        public void Write(SimStateWriter writer, in Casualty value)
        { writer.Byte(value.Kind); writer.Int64(value.Start); writer.Int64(value.Due); }

        public Casualty Read(SimStateReader reader)
        {
            byte kind = reader.Byte();
            long start = reader.Int64(), due = reader.Int64();
            SimStateReader.Require(kind == Casualty.Stunned || kind == Casualty.Downed, "Invalid casualty kind.");
            SimStateReader.Require(start >= 0 && start <= due, "Invalid casualty ticks.");
            return new Casualty(kind, start, due);
        }
    }
}
