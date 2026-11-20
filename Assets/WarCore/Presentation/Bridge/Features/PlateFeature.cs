using System;
using WarCore.Content;
using WarCore.Interim;
using WarCore.Sim;

namespace WarCore.Presentation
{
    // Floor plates: the layered graph of a map with plates, every unit's vertical state, layer numbers on the input, and the
    // movers' plate binding. On only for such maps, which the builder still refuses unless the recipe opens the internal preview.
    internal sealed class PlateFeature : WorldFeature
    {
        internal static readonly PlateFeature Instance = new PlateFeature();

        public override string OptionId => "world.plates";

        public override bool IsActive(WorldAssembly a) => a.Map != null && a.Map.Plates.Count > 0;

        // Before any feature runs: a spawn above the ground needs a map with plates, and reserves deploy on the ground.
        internal static void CheckSpawnLayers(WorldRecipe recipe, WcMap map)
        {
            bool plates = map != null && map.Plates.Count > 0;
            foreach (UnitSpawn spawn in recipe.Units)
            {
                if (spawn.Layer == 0) continue;
                if (!plates)
                    throw new ArgumentException("Unit spawn " + spawn.Key + " names layer " + spawn.Layer + " on a map without floor plates.", nameof(recipe));
                if (spawn.Reserve)
                    throw new ArgumentException("Reserve unit " + spawn.Key + " names layer " + spawn.Layer + "; reserves deploy on the ground.", nameof(recipe));
            }
        }

        public override void Prepare(WorldAssembly a)
        {
            if (!a.Path)
                throw new NotSupportedException("Plate maps need a path or crowd mover.");
            ContentPack pack = a.Recipe.TerrainPack;
            LayeredNav nav = pack != null ? WcMapLayeredNav.Build(a.Map, pack) : WcMapLayeredNav.Build(a.Map, null, a.Map.ContentId);
            a.Plates = new PlateWorld(a.Map, nav);
            // The graph's digest names the exact plates, ground and rules, so a snapshot cannot be restored under other plates.
            a.ContentKeys.Add(new StableContentKey("plates.nav", nav.Digest));
        }

        public override void RegisterComponents(WorldAssembly a)
        {
            a.Verticals = a.Sim.Entities.Register<VerticalState>(VerticalState.ComponentId, new VerticalStateSerializer());
            a.Sim.EnableInputLayers();
        }

        // Every unit has a vertical state from the start, a reserve unit too (on the ground until it is deployed). A unit placed
        // on a plate stands on a walkable cell of it; its footprint is not checked per plate yet.
        public override void PlaceUnit(WorldAssembly a, int index, EntityId unit)
        {
            UnitSpawn spawn = a.Recipe.Units[index];
            if (spawn.Layer > 0)
            {
                if (spawn.Layer >= a.Plates.LayerCount)
                    throw new ArgumentException("Unit spawn " + spawn.Key + " names layer " + spawn.Layer + " of a map with "
                        + (a.Plates.LayerCount - 1) + " plates.", "recipe");
                SimVec3 point = SimSession.Target(spawn.XMm, spawn.ZMm);
                if (!a.Plates.TryFloor(spawn.Layer, point.X, point.Z, true, out _))
                    throw new ArgumentException("Unit spawn " + spawn.Key + " is not on a walkable cell of layer " + spawn.Layer + ".", "recipe");
            }
            a.Sim.Entities.Set(a.Verticals, unit, new VerticalState(spawn.Layer, 0));
        }

        // The plate world moves on the terrain branch: its floors are the ground (height field, or the flat height units spawn at) and
        // each plate's top field; the layered graph routes and decides every change of layer.
        public override void RegisterSystems(WorldAssembly a) =>
            a.Locomotion.BindPlates(a.Verticals, a.Plates.Nav, a.Plates.Tops(), a.Map.FlatHeight, a.Sim.FootAnchoredLayers);
    }
}
