namespace WarCore.Sim
{
    // Only the immutable archetype reference is a component. Mutable rows live in GameplayState.
    public readonly struct GameplayBinding : IStateHash
    {
        public ulong ArchetypeKey
        {
            get;
        }
        public GameplayBinding(ulong archetypeKey)
        {
            ArchetypeKey = archetypeKey;
        }
        public void WriteState(StateHash hash) => hash.Add(ArchetypeKey);
    }

    public sealed class GameplayBindingSerializer : IStableComponentSerializer<GameplayBinding>
    {
        public int TypeId => 21;
        public int Version => 1;
        public void Write(SimStateWriter writer, in GameplayBinding value) => writer.UInt64(value.ArchetypeKey);
        public GameplayBinding Read(SimStateReader reader) => new GameplayBinding(reader.UInt64());
    }
}
