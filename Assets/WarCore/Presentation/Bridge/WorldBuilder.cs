using System;
using System.Collections.Generic;
using WarCore.Content;
using WarCore.Battle;
using WarCore.Interim;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Presentation
{
    public static class WorldBuilder
    {
        // Slot gap 2 m: stopped units leave 1.5 m between their edges, so a latecomer can walk between them.
        internal static readonly SimFix CrowdSlotSpacing = SimFix.FromInt(2);

        internal static (long X, long Z) FormationOffsetMm(int rank, int count)
        {
            SimVec3 offset = InterimOrders.SlotOffset(rank, count, SimFix.FromInt(1));
            return (offset.X.Raw * 1000 / SimFix.Scale, offset.Z.Raw * 1000 / SimFix.Scale);
        }

        public static SimSession Create(WorldRecipe recipe) => Create(recipe, null);

        // Units stand on the map's flat floor; walls become static boxes on the layers the file names.
        public static SimSession Create(WorldRecipe recipe, byte[] mapBytes) => Build(recipe, mapBytes, false);

        internal static SimSession Build(WorldRecipe recipe, byte[] mapBytes, bool reverseAllocation)
        {
            if (recipe == null)
                throw new ArgumentNullException(nameof(recipe));
            var assembly = new WorldAssembly(recipe, mapBytes, reverseAllocation);
            FeatureTable.RunValidate(assembly);
            bool crowd = assembly.Crowd;
            if (recipe.Match != null && (recipe.Combat == null || !crowd))
                throw new ArgumentException("Battle requires combat and crowd movement.", nameof(recipe));
            if (recipe.PlayerKey.HasValue && (!crowd || recipe.Combat == null || recipe.Match == null))
                throw new ArgumentException("Player control requires InterimCrowd, Combat and Match.", nameof(recipe));
            WcMap map = mapBytes == null ? null : WcMapReader.Read(mapBytes);
            assembly.Map = map;
            FeatureTable.RunPrepare(assembly);
            long groundRaw = map == null ? 0 : map.FlatHeight.Raw;
            uint wallMask = 0;
            if (map != null)
                foreach (WcMapWall wall in map.Walls)
                    wallMask |= map.LayerMask(wall.LayerName);
            if ((wallMask & WorldAssembly.MoverLayer) != 0)
                throw new InvalidOperationException("Wall layers must not share the mover layer.");
            assembly.PinMap(map, groundRaw, wallMask);
            var content = new StableContentManifest(1, assembly.ContentKeys.ToArray());
            var placed = new List<PlacedCatalogEntry>();
            if (map != null)
                foreach (WcMapWall wall in map.Walls)
                    placed.Add(new PlacedCatalogEntry(wall.Chunk, wall.Placement));
            int firstUnit = placed.Count;
            foreach (UnitSpawn spawn in recipe.Units)
                placed.Add(new PlacedCatalogEntry(1, spawn.Key));
            PlacedCatalogEntry[] placements = placed.ToArray();
            var catalog = new PlacedCatalog(content, placements);
            // The map path sizes the entity table to walls plus units; the plain path keeps the M1 default.
            SimConfig config = map == null ? null : new SimConfig(entityCapacity: placements.Length);
            if (recipe.Combat != null) config = recipe.Combat.BuildConfig(placements.Length, recipe.Units.Length);
            // The match installer made the mode in Prepare, one fresh instance per world; it is bound once the match systems exist.
            MatchMode mode = assembly.Pinned.Mode;
            var sim = recipe.Match == null
                ? Simulation.CreateRetainedWorld(recipe.Seed, catalog, config, content: content,
                    gameplaySchema: recipe.Combat?.BuildSchema(recipe.Units.Length))
                : Simulation.CreateRetainedWorld(recipe.Seed, catalog, config, content: content, mode: mode, modeId: mode.RuleSetId,
                    gameplaySchema: recipe.Combat?.BuildSchema(recipe.Units.Length));
            assembly.Sim = sim;
            FeatureTable.RunComponents(assembly);
            assembly.PinComponents();
            AssemblyPins pins = assembly.Pinned;
            sim.ConfigureSpatial(pins.Positions, pins.Colliders, SimFix.FromInt(2));
            if (map != null)
            {
                for (int n = 0; n < map.Walls.Count; n++)
                {
                    WcMapWall wall = map.Walls[reverseAllocation ? map.Walls.Count - 1 - n : n];
                    EntityId entity = sim.Entities.Create(wall.Key);
                    FeatureTable.RunWall(assembly, wall, entity);
                }
            }
            var units = new EntityId[recipe.Units.Length];
            assembly.Units = units;
            for (int n = 0; n < units.Length; n++)
            {
                int i = reverseAllocation ? units.Length - 1 - n : n;
                units[i] = sim.Entities.Create(placements[firstUnit + i].Key);
                FeatureTable.RunUnit(assembly, i, units[i]);
            }
            // Player control, combat and the crowd pass rule, then the movement systems, then the systems built on movement.
            FeatureTable.RunConnect(assembly);
            assembly.PinConnected();
            FeatureTable.RunSystems(assembly);
            assembly.PinLocomotion();
            FeatureTable.RunRules(assembly);
            sim.CompleteSpatialBoundary();
            ComponentType<UnitFacing>? facings = recipe.Facing ? pins.Facings : (ComponentType<UnitFacing>?)null;
            var session = new SimSession(new SessionParts
            {
                Sim = sim, Units = units, Spawns = recipe.Units.ToArray(), Positions = pins.Positions, GroundRaw = groundRaw, Map = map,
                WallMask = wallMask, View = mapBytes == null ? null : ViewMap.Parse(mapBytes), Combat = pins.Combat,
                MatchPack = recipe.Match, Capture = assembly.Capture, Reinforce = assembly.Reinforce, Match = assembly.Match,
                MaxHealthRaw = recipe.Combat?.MaxHealthRaw ?? 0, Controlled = pins.Controlled, CombatPack = recipe.Combat,
                Facings = facings,
            });
            foreach (BridgeCommand command in recipe.Script)
                session.Enqueue(command);
            return session;
        }
    }
}
