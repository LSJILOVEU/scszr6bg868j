using System;
using WarCore.Battle;
using WarCore.Interim;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Presentation
{
    // The block AI: units that hold a block when a swing comes at them. On when the recipe asks for it; needs combat with
    // block specs and the facing component, and is registered after the match systems.
    internal sealed class BlockAiFeature : WorldStageFeature
    {
        internal static readonly BlockAiFeature Instance = new BlockAiFeature();

        public override string OptionId => "world.block-ai";

        public override bool IsActive(WorldAssembly a) => a.Recipe.BlockAi;

        public override void RegisterRules(WorldAssembly a)
        {
            WorldRecipe recipe = a.Recipe;
            AssemblyPins pins = a.Pinned;
            if (pins.Combat == null || !recipe.Combat.HasBlock || !recipe.Facing)
                throw new ArgumentException("The block AI needs combat with block specs and facing.", nameof(recipe));
            a.Sim.RegisterGameplay("battle.block-ai", new BlockAiSystem(pins.Combat, new FacingSource(a.Sim, pins.Facings), pins.Controlled), SimSystemPhase.Requests);
        }
    }

    internal sealed class FacingSource : IUnitFacings
    {
        private readonly Simulation sim;
        private readonly ComponentType<UnitFacing> facings;
        internal FacingSource(Simulation sim, ComponentType<UnitFacing> facings) { this.sim = sim; this.facings = facings; }
        public bool TryFacing(StableId unit, out SimVec3 direction)
        {
            direction = default;
            if (!sim.Entities.TryResolve(unit, out EntityRef found) || !sim.Entities.TryGet(facings, (EntityId)found, out UnitFacing value)) return false;
            direction = new SimVec3(value.X, default, value.Z);
            return true;
        }
    }
}
