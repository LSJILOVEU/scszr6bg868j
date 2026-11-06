using System.Collections.Generic;
using WarCore.Content;
using WarCore.Sim;

namespace WarCore.Presentation
{
    // The built-in installers, one ordered table per hook. A feature missing from a table never runs that hook, so a row left out
    // changes the world. Features a recipe adds with WorldRecipe.With run after the built-in ones of the same hook, in the order added.
    // Movement comes before combat in component registration and combat before movement in system registration (F2b adds the
    // combat rows); component order is not part of a world's state, system dependencies are.
    internal static class FeatureTable
    {
        internal static readonly IWorldFeature[] Validate = { MovementFeature.Instance };
        internal static readonly IWorldFeature[] Prepare = { TerrainFeature.Instance };
        internal static readonly IWorldFeature[] Components =
            { MovementFeature.Instance, TerrainFeature.Instance, FacingFeature.Instance, MotionFeature.Instance, StandingFeature.Instance };
        internal static readonly IWorldFeature[] Walls = { MovementFeature.Instance };
        // Facing and motion are written before the terrain check, the body after it, as the unit loop always did.
        internal static readonly IWorldFeature[] Units =
            { FacingFeature.Instance, MotionFeature.Instance, TerrainFeature.Instance, MovementFeature.Instance };
        internal static readonly IWorldFeature[] Systems = { MovementFeature.Instance, FacingFeature.Instance };

        private static IEnumerable<IWorldFeature> Active(IWorldFeature[] table, WorldAssembly assembly)
        {
            foreach (IWorldFeature feature in table)
                if (feature.IsActive(assembly))
                    yield return feature;
            foreach (IWorldFeature feature in assembly.Extensions)
                if (feature.IsActive(assembly))
                    yield return feature;
        }

        internal static void RunValidate(WorldAssembly assembly)
        {
            foreach (IWorldFeature feature in Active(Validate, assembly)) feature.Validate(assembly);
        }

        internal static void RunPrepare(WorldAssembly assembly)
        {
            foreach (IWorldFeature feature in Active(Prepare, assembly)) feature.Prepare(assembly);
        }

        internal static void RunComponents(WorldAssembly assembly)
        {
            foreach (IWorldFeature feature in Active(Components, assembly)) feature.RegisterComponents(assembly);
        }

        internal static void RunWall(WorldAssembly assembly, WcMapWall wall, EntityId entity)
        {
            foreach (IWorldFeature feature in Active(Walls, assembly)) feature.PlaceWall(assembly, wall, entity);
        }

        internal static void RunUnit(WorldAssembly assembly, int index, EntityId unit)
        {
            foreach (IWorldFeature feature in Active(Units, assembly)) feature.PlaceUnit(assembly, index, unit);
        }

        internal static void RunSystems(WorldAssembly assembly)
        {
            foreach (IWorldFeature feature in Active(Systems, assembly)) feature.RegisterSystems(assembly);
        }
    }
}
