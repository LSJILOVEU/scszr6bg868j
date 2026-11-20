using WarCore.Battle;
using WarCore.Interim;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Presentation
{
    // ML-9b: what the match systems read of the plates on a map with floor plates: a unit's layer (its vertical state; a unit
    // without one is on the ground) and a layer's walkable top for guard slots. Null on a map without plates.
    internal sealed class PlateLayers : IUnitLayers, ISlotFloor
    {
        private readonly Simulation sim;
        private readonly ComponentType<VerticalState> verticals;
        private readonly PlateWorld plates;

        private PlateLayers(Simulation sim, ComponentType<VerticalState> verticals, PlateWorld plates)
        {
            this.sim = sim; this.verticals = verticals; this.plates = plates;
        }

        internal static PlateLayers Of(WorldAssembly a) =>
            a.Plates != null ? new PlateLayers(a.Sim, a.Verticals, a.Plates) : null;

        public int LayerOf(EntityId unit) => sim.Entities.TryGet(verticals, unit, out VerticalState state) ? state.Layer : 0;

        public bool TryFloor(int layer, SimFix x, SimFix z, out SimFix y) => plates.TryFloor(layer, x, z, true, out y);
    }
}
