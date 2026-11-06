using WarCore.Interim;
using WarCore.Sim;

namespace WarCore.Presentation
{
    // The per-unit motion-limits component. Off unless the recipe opens it.
    internal sealed class MotionFeature : WorldFeature
    {
        internal static readonly MotionFeature Instance = new MotionFeature();

        public override string OptionId => WorldOptions.Motion.OptionId;

        public override bool IsActive(WorldAssembly a) => a.Recipe.Motion;

        public override void RegisterComponents(WorldAssembly a) =>
            a.MotionLimits = a.Sim.Entities.Register<MotionLimits>(MotionLimits.ComponentId, new MotionLimitsSerializer());

        public override void PlaceUnit(WorldAssembly a, int index, EntityId unit) =>
            a.Sim.Entities.Set(a.MotionLimits, unit,
                new MotionLimits(WorldAssembly.SpawnSpeed(a.Recipe.Units[index]), default, default, default));
    }
}
