using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;
namespace WarCore.Tests
{
    public static class M0c2Fixture
    {
        public static Simulation Create(ulong rules = 0)
            => Simulation.CreateRetainedWorld(123,new PlacedCatalog(new StableContentManifest(0)),
                gameplaySchema:new GameplaySchema(rules));
    }
}
