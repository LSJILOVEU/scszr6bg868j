using System;
using WarCore.Content;
using WarCore.Content.MapTools;

namespace WarCore.Presentation
{
    // A baked map description and its content pack -> the recipe of a runnable world.
    public static class MapRecipe
    {
        // Legacy maps omit unit-type speed and must retain their original state hashes.
        private const long SpeedMmPerSec = 6000;

        public static WorldRecipe From(MapBakeResult result, ContentPack pack, bool useUnitTypes = false)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            if (pack == null) throw new ArgumentNullException(nameof(pack));
            if (pack.Id != result.PackId)
                throw new ArgumentException("Map pack '" + result.PackId + "' does not match content pack '" + pack.Id + "'.", nameof(pack));
            MapSetup setup = result.Setup;
            var units = new UnitSpawn[setup.Soldiers.Count];
            for (int i = 0; i < units.Length; i++)
            {
                MapSoldier s = setup.Soldiers[i];
                if (useUnitTypes)
                    units[i] = UnitSpawn.OfType(s.Key, pack, s.Type, s.Squad, s.Team, s.XMm, s.ZMm, s.Reserve);
                else
                    units[i] = new UnitSpawn(s.Key, 0, s.Squad, s.XMm, s.ZMm, SpeedMmPerSec, s.Team,
                        pack.WeaponOfUnitType(s.Type), s.Reserve, pack.MeleeOfUnitType(s.Type));
            }
            WorldRecipe recipe = new WorldRecipe(setup.Seed, MoverKind.InterimCrowd, units, Array.Empty<BridgeCommand>())
                .WithCombat(pack.BuildCombat());
            if (setup.ControlPoints.Length != 0) recipe = recipe.WithMatch(pack.BuildMatch(setup.ControlPoints, setup.Bases));
            if (WcMapReader.Read(result.WcMapBytes).HasTerrain) recipe = recipe.WithTerrainRules(pack);
            if (setup.CrowdPass == "teamIdle") recipe = recipe.WithCrowdPass(CrowdPassKind.TeamIdle);
            return recipe;
        }
    }
}
