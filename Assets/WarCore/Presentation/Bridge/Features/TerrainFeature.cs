using System;
using WarCore.Content;
using WarCore.Interim;
using WarCore.Sim;

namespace WarCore.Presentation
{
    // Terrain: the tables derived from a map with a height field or a surface table, the progress component of movers on it,
    // and the check that no unit is placed where its footprint would stand on blocked ground. On for such maps and for every map
    // with floor plates (a plate world always moves on the terrain branch, even over flat ground).
    internal sealed class TerrainFeature : WorldFeature
    {
        internal static readonly TerrainFeature Instance = new TerrainFeature();

        public override string OptionId => "world.terrain";

        public override bool IsActive(WorldAssembly a) => a.Map != null && (a.Map.HasTerrain || a.Map.Plates.Count > 0);

        public override void Prepare(WorldAssembly a)
        {
            // Order matters (the tests pin it): mover kind, then a missing pack, then the derivation.
            if (!a.Path)
                throw new NotSupportedException("Terrain maps (height field or surface table) need a path or crowd mover.");
            if (a.Recipe.TerrainPack == null && a.Map.HasTerrain)
                throw new ArgumentException("A terrain map needs a content pack with terrain rules (WorldRecipe.WithTerrainRules).", "recipe");
            // A map with plates only (no height field, no surface table) and no pack takes the default rules, as its layered graph does.
            a.Terrain = a.Recipe.TerrainPack != null ? TerrainDerivation.Derive(a.Map, a.Recipe.TerrainPack)
                : TerrainDerivation.Derive(a.Map, null, a.Map.ContentId);
            // The terrain digest names the exact (map, rules) pair, so a snapshot cannot be restored under other heights or rules.
            a.ContentKeys.Add(new StableContentKey("terrain.tables", a.Terrain.Digest));
        }

        public override void RegisterComponents(WorldAssembly a) =>
            a.Sim.Entities.Register<MoveProgress>(MoveProgress.ComponentId, new MoveProgressSerializer());

        public override void PlaceUnit(WorldAssembly a, int index, EntityId unit)
        {
            UnitSpawn spawn = a.Recipe.Units[index];
            // A terrain world never places a unit where its footprint touches blocked ground or leaves the map. A unit placed on a
            // plate stands above that ground: the plate installer checks its cell.
            if (a.IsDeployedAtStart(spawn) && spawn.Layer == 0)
            {
                SimVec3 point = a.SpawnPoint(spawn);
                if (!a.Terrain.Nav.SegmentClear(point, point, InterimLocomotion.UnitFootprint))
                    throw new ArgumentException("Unit spawn " + spawn.Key + " is not on standable ground in a terrain world.", "recipe");
            }
        }
    }
}
