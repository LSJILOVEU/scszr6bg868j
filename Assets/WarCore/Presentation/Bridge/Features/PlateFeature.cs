using System;
using System.Collections.Generic;
using WarCore.Battle;
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

        // Before any feature runs: a spawn or match anchor above the ground needs a map with plates, and reserves deploy on the
        // ground.
        internal static void CheckSpawnLayers(WorldRecipe recipe, WcMap map)
        {
            bool plates = map != null && map.Plates.Count > 0;
            if (!plates && recipe.Match != null)
                foreach ((string name, int layer, SimVec3 _) in Anchors(recipe.Match))
                    if (layer != 0)
                        throw new ArgumentException("Match " + name + " names layer " + layer + " on a map without floor plates.", nameof(recipe));
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
            if (a.Recipe.Match != null) CheckAnchors(a.Recipe.Match, a.Map, a.Plates.LayerCount);
            // The graph's digest names the exact plates, ground and rules, so a snapshot cannot be restored under other plates.
            a.ContentKeys.Add(new StableContentKey("plates.nav", nav.Digest));
            // The graph's digest leaves out the plates' thickness; the line tests read it (ML-9), so the plate field has its own key.
            a.ContentKeys.Add(new StableContentKey("plates.field", a.Plates.Field.Digest()));
        }

        // Every match anchor on a plate names a plate of this map and stands on a solid cell of it (MapCells, as the bake);
        // whether the cell is walkable is decided at each deployment.
        private static void CheckAnchors(MatchPack match, WcMap map, int layerCount)
        {
            foreach ((string name, int layer, SimVec3 at) in Anchors(match))
            {
                if (layer == 0) continue;
                if (layer >= layerCount)
                    throw new ArgumentException("Match " + name + " names layer " + layer + " of a map with " + (layerCount - 1) + " plates.", "recipe");
                if (!MapCells.OnSolidPlate(map, layer, at.X, at.Z))
                    throw new ArgumentException("Match " + name + " is not on a solid cell of layer " + layer + ".", "recipe");
            }
        }

        private static IEnumerable<(string, int, SimVec3)> Anchors(MatchPack match)
        {
            ControlPointDef[] points = match.Points.ToArray();
            foreach (ControlPointDef point in points)
                if (point.HasSpawn) yield return ("spawn of point " + point.Id, point.SpawnLayer, point.Spawn);
            // ML-9b: a point's centre on a plate.
            foreach (ControlPointDef point in points) yield return ("centre of point " + point.Id, point.Layer, point.Center);
            SpawnAnchor[] bases = match.Bases.ToArray();
            for (int i = 0; i < bases.Length; i++) yield return ("base " + i, bases[i].Layer, bases[i].Point);
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
