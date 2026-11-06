using WarCore.Interim;
using WarCore.Sim;

namespace WarCore.Presentation
{
    // The per-unit facing component. Off unless the recipe opens it; a world without it is byte-identical to one built before it existed.
    internal sealed class FacingFeature : WorldFeature
    {
        internal static readonly FacingFeature Instance = new FacingFeature();

        public override string OptionId => WorldOptions.Facing.OptionId;

        public override bool IsActive(WorldAssembly a) => a.Recipe.Facing;

        public override void RegisterComponents(WorldAssembly a) =>
            a.Facings = a.Sim.Entities.Register<UnitFacing>(UnitFacing.ComponentId, new UnitFacingSerializer());

        public override void PlaceUnit(WorldAssembly a, int index, EntityId unit) =>
            a.Sim.Entities.Set(a.Facings, unit, UnitFacing.Default);

        // The locomotion turns units as it moves them, so it learns the component once it exists.
        public override void RegisterSystems(WorldAssembly a) => a.Locomotion.BindFacing(a.Facings);
    }
}
