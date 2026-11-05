using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    // Keep the original match untouched; this separate scene freezes the added collider state.
    internal static class LayerHashFixture
    {
        internal const ulong IncludedHash = 0x0140D322A4CC7742UL;
        internal const ulong IgnoredHash = 0x0E9DC76A059D3C2FUL;

        internal static Simulation Create(ulong seed)
        {
            Simulation sim = Fixtures.Create(seed);
            var colliders = sim.Entities.Register<Collider>("collider", new ColliderSerializer());
            var shape = new Collider(SimFix.Ratio(1, 4), SimFix.FromInt(3), 0x80000001u);
            sim.Entities.Set(colliders, new EntityId(0, 1), shape);
            for (int i = 1; i <= 2; i++)
            {
                EntityId entity = sim.Entities.Create();
                sim.Entities.Set(entity, new Position(SimVec3.Metres(i * 4, 0, 0)));
                sim.Entities.Set(colliders, entity, shape.WithLayer(i == 1 ? 0x80000000u : uint.MaxValue));
            }
            return sim;
        }

        internal static ulong Hash(Simulation sim, bool ignoreLayers)
        {
            var hash = new StateHash(ignoreLayers);
            sim.WriteState(hash);
            return hash.Value;
        }
    }
}
