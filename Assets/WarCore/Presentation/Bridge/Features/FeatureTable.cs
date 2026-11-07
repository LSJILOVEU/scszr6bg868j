using System.Collections.Generic;
using WarCore.Content;
using WarCore.Sim;

namespace WarCore.Presentation
{
    // The built-in installers, one ordered table per hook. A feature missing from a table never runs that hook, so a row left out
    // changes the world. Features a recipe adds with WorldRecipe.With run after the built-in ones of the same hook, in the order added.
    // Movement comes before combat in component registration, and combat (made in Connect) before movement in system
    // registration; component order is not part of a world's state, system dependencies are. Connect and RegisterRules are the
    // hooks of IWorldStageFeature: an extension that does not implement it is not visited by them at all.
    internal static class FeatureTable
    {
        internal static readonly IWorldFeature[] Validate = { MovementFeature.Instance };
        internal static readonly IWorldFeature[] Prepare = { TerrainFeature.Instance, MatchFeature.Instance };
        internal static readonly IWorldFeature[] Components =
        {
            MovementFeature.Instance, TerrainFeature.Instance, FacingFeature.Instance, MotionFeature.Instance, StandingFeature.Instance,
            CombatFeature.Instance,
        };
        internal static readonly IWorldFeature[] Walls = { MovementFeature.Instance };
        // Facing and motion are written before the terrain check, the body after it, as the unit loop always did.
        // Combat binds the unit and writes its combatant state after the body exists.
        internal static readonly IWorldFeature[] Units =
            { FacingFeature.Instance, MotionFeature.Instance, TerrainFeature.Instance, MovementFeature.Instance, CombatFeature.Instance };
        // The player control first (combat keeps it), then combat, then the crowd pass rule (it reads both).
        internal static readonly IWorldStageFeature[] Connect = { PlayerFeature.Instance, CombatFeature.Instance, CrowdPassFeature.Instance };
        internal static readonly IWorldFeature[] Systems = { MovementFeature.Instance, FacingFeature.Instance };
        // After the movement systems: the match needs the locomotion; the player systems need it and the mode.
        internal static readonly IWorldStageFeature[] Rules = { MatchFeature.Instance, BlockAiFeature.Instance, PlayerFeature.Instance };

        private static IEnumerable<IWorldFeature> Active(IWorldFeature[] table, WorldAssembly assembly)
        {
            foreach (IWorldFeature feature in table)
                if (feature.IsActive(assembly))
                    yield return feature;
            foreach (IWorldFeature feature in assembly.Extensions)
                if (feature.IsActive(assembly))
                    yield return feature;
        }

        private static IEnumerable<IWorldStageFeature> Staged(IWorldStageFeature[] table, WorldAssembly assembly)
        {
            foreach (IWorldStageFeature feature in table)
                if (feature.IsActive(assembly))
                    yield return feature;
            foreach (IWorldFeature feature in assembly.Extensions)
                if (feature is IWorldStageFeature staged && staged.IsActive(assembly))
                    yield return staged;
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

        internal static void RunConnect(WorldAssembly assembly)
        {
            foreach (IWorldStageFeature feature in Staged(Connect, assembly)) feature.Connect(assembly);
        }

        internal static void RunRules(WorldAssembly assembly)
        {
            foreach (IWorldStageFeature feature in Staged(Rules, assembly)) feature.RegisterRules(assembly);
        }
    }
}
