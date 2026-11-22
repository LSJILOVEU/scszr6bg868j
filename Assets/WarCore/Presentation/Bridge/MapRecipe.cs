using System;
using WarCore.Battle;
using WarCore.Content;
using WarCore.Content.MapTools;
using WarCore.Interim;

namespace WarCore.Presentation
{
    // A baked map description and its content pack -> the recipe of a runnable world.
    public static class MapRecipe
    {
        public static WorldRecipe From(MapBakeResult result, ContentPack pack, bool useUnitTypes = false)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            if (pack == null) throw new ArgumentNullException(nameof(pack));
            if (pack.Id != result.PackId)
                throw new ArgumentException("Map pack '" + result.PackId + "' does not match content pack '" + pack.Id + "'.", nameof(pack));
            MapSetup setup = result.Setup;
            WcMap map = WcMapReader.Read(result.WcMapBytes);
            bool capped = map.HasTerrain || map.Plates.Count > 0;
            SpawnAnchor[] bases = setup.Bases;
            int soldiers = setup.Soldiers.Count, extraTeams = ReserveTeams(setup, bases, pack, out int perTeam);
            var units = new UnitSpawn[soldiers + extraTeams * perTeam];
            ulong last = 0;
            for (int i = 0; i < soldiers; i++)
            {
                MapSoldier s = setup.Soldiers[i];
                units[i] = Spawn(pack, useUnitTypes, s.Key, s.Type, s.Squad, s.Team, s.XMm, s.ZMm, s.Reserve, s.Layer);
                if (capped) CheckStep(pack, s.Type, units[i]);
                if (s.Key > last) last = s.Key;
            }
            if (extraTeams > 0)
            {
                // RESPAWN-AI-2: match.reserveCount more reserves of match.reserveType for each team with a base, lower team
                // first, after the map's own units and keyed on from the map's highest key. A reserve has no place until it
                // is deployed, so its coordinates (0, 0, the ground) are never read; its squad is its team, as in map files.
                if (last > ulong.MaxValue - (ulong)(extraTeams * perTeam))
                    throw new ContentException("content pack '" + pack.Id + "' field 'reserveCount': no unit keys are left after the map's highest key " + last);
                string type = pack.Match.ReserveType;
                int n = soldiers;
                for (int team = 1; team <= MatchPack.MaxTeams; team++)
                {
                    if (!HasBase(bases, team)) continue;
                    for (int i = 0; i < perTeam; i++) units[n++] = Spawn(pack, useUnitTypes, ++last, type, team, team, 0, 0, true, 0);
                }
                for (int k = soldiers; k < n && capped; k++) CheckStep(pack, type, units[k]);
            }
            WorldRecipe recipe = new WorldRecipe(setup.Seed, MoverKind.InterimCrowd, units, Array.Empty<BridgeCommand>())
                .WithCombat(pack.BuildCombat());
            if (setup.ControlPoints.Length != 0) recipe = recipe.WithMatch(pack.BuildMatch(setup.ControlPoints, setup.Bases));
            if (map.HasTerrain) recipe = recipe.WithTerrainRules(pack);
            // Floor plates in a map file are supported: the file route opens them; a recipe built by hand still declares them.
            if (map.Plates.Count > 0) recipe = recipe.WithPlatesPreview();
            if (setup.CrowdPass == "teamIdle") recipe = recipe.WithCrowdPass(CrowdPassKind.TeamIdle);
            return recipe;
        }

        // With the switch off a map file's units do not read their unit types (speed, block, shape): they take the pack's
        // default speed (ContentPack.DefaultUnitSpeedMmPerSec) and only the weapon and the swing of their type.
        private static UnitSpawn Spawn(ContentPack pack, bool useUnitTypes, ulong key, string type, int squad, int team, long xMm, long zMm, bool reserve, int layer) =>
            useUnitTypes
                ? UnitSpawn.OfType(key, pack, type, squad, team, xMm, zMm, reserve, layer)
                : new UnitSpawn(key, 0, squad, xMm, zMm, ContentPack.DefaultUnitSpeedMmPerSec, team, pack.WeaponOfUnitType(type), reserve,
                    pack.MeleeOfUnitType(type), layer: layer);

        // HC-3: a world with terrain or floor plates moves a unit at most InterimLocomotion.MaxTerrainStepRaw a tick and refuses a
        // faster unit on its first walk. A recipe that puts a pack's unit on such a map checks the unit when it is built and
        // names the pack, the unit type and the speed. A recipe built by hand is still caught by the simulation.
        internal static void CheckStep(ContentPack pack, string unitTypeId, in UnitSpawn spawn)
        {
            if (WorldAssembly.SpawnSpeed(spawn).Raw > InterimLocomotion.MaxTerrainStepRaw)
                throw new ContentException("content pack '" + pack.Id + "' unit type '" + unitTypeId + "' field 'speed': " + spawn.SpeedMmPerSec
                    + " mm/s is faster than one step a tick allows in worlds with terrain or floor plates");
        }

        // RESPAWN-AI-2: how many teams get the pack's extra reserves and how many each. None on a map without control points:
        // such a world has no match rules and would place every reserve on the field at the start. A team without a base gets
        // none either: its reserves could never deploy, yet would count as alive for ever.
        private static int ReserveTeams(MapSetup setup, SpawnAnchor[] bases, ContentPack pack, out int perTeam)
        {
            perTeam = setup.ControlPoints.Length != 0 ? pack.Match.ReserveCount : 0;
            if (perTeam == 0) return 0;
            int teams = 0;
            for (int team = 1; team <= MatchPack.MaxTeams; team++) if (HasBase(bases, team)) teams++;
            return teams;
        }

        private static bool HasBase(SpawnAnchor[] bases, int team)
        {
            foreach (SpawnAnchor anchor in bases) if (anchor.Team == team) return true;
            return false;
        }
    }
}
