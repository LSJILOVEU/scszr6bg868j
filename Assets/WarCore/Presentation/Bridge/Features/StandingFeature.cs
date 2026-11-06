using WarCore.Interim;
using WarCore.Sim;

namespace WarCore.Presentation
{
    // The per-unit standing-order component (hold, follow, move in progress). Off unless the recipe opens it.
    // The orders system that carries the orders out is part of the movement core and reads the component type from here.
    internal sealed class StandingFeature : WorldFeature
    {
        internal static readonly StandingFeature Instance = new StandingFeature();

        public override string OptionId => WorldOptions.Standing.OptionId;

        public override bool IsActive(WorldAssembly a) => a.Recipe.Standing;

        public override void RegisterComponents(WorldAssembly a) =>
            a.Standing = a.Sim.Entities.Register<SquadStanding>(SquadStanding.ComponentId, new SquadStandingSerializer());
    }
}
