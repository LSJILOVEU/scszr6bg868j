using System;
using WarCore.Content;
using WarCore.Interim;
using WarCore.Sim;

namespace WarCore.Presentation
{
    // Terrain: the tables derived from a map with a height field or a surface table, the progress component of movers on it,
    // and the check that no unit is placed where its footprint would stand on blocked ground. On only for such maps.
    internal sealed class TerrainFeature : WorldFeature
    {
        internal static readonly TerrainFeature Instance = new TerrainFeature();

        public override string OptionId => "world.terrain";

        public override bool IsActive(WorldAssembly a) => a.Map != null && a.Map.HasTerrain;

        public override void Prepare(WorldAssembly a)
        {
            // Order matters (the tests pin it): mover kind, then the player, then a missing pack, then the derivation.
            if (!a.Path)
                throw new NotSupportedException("Terrain maps (height field or surface table) need a path or crowd mover.");
            if (a.Recipe.PlayerKey.HasValue)
                throw new NotSupportedException("Player control on terrain maps is not supported before T1c.");
            if (a.Recipe.TerrainPack == null)
                throw new ArgumentException("A terrain map needs a content pack with terrain rules (WorldRecipe.WithTerrainRules).", "recipe");
            a.Terrain = TerrainDerivation.Derive(a.Map, a.Recipe.TerrainPack);
            // The terrain digest names the exact (map, rules) pair, so a snapshot cannot be restored under other heights or rules.
            a.ContentKeys.Add(new StableContentKey("terrain.tables", a.Terrain.Digest));
        }

        public override void RegisterComponents(WorldAssembly a) =>
            a.Sim.Entities.Register<MoveProgress>(MoveProgress.ComponentId, new MoveProgressSerializer());

        public override void PlaceUnit(WorldAssembly a, int index, EntityId unit)
        {
            UnitSpawn spawn = a.Recipe.Units[index];
            // A terrain world never places a unit where its footprint touches blocked ground or leaves the map.
            if (a.IsDeployedAtStart(spawn))
            {
                SimVec3 point = a.SpawnPoint(spawn);
                if (!a.Terrain.Nav.SegmentClear(point, point, InterimLocomotion.UnitFootprint))
                    throw new ArgumentException("Unit spawn " + spawn.Key + " is not on standable ground in a terrain world.", "recipe");
            }
        }
    }
}
