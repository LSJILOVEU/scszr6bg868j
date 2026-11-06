using System;
using WarCore.Content;
using WarCore.Control;
using WarCore.Interim;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Presentation
{
    // The movement core: position, body, move intent and waypoint components, the bodies of walls and units, and the order and
    // locomotion systems. Always on.
    internal sealed class MovementFeature : WorldFeature
    {
        internal static readonly MovementFeature Instance = new MovementFeature();

        public override string OptionId => "world.movement";

        public override void Validate(WorldAssembly a)
        {
            WorldRecipe recipe = a.Recipe;
            if (recipe.Mover != MoverKind.Interim && recipe.Mover != MoverKind.InterimPath && recipe.Mover != MoverKind.InterimCrowd)
                throw new ArgumentOutOfRangeException("recipe", "Unsupported mover.");
            if (a.Path && a.MapBytes == null)
                throw new ArgumentException("Path and crowd movers require map bytes.", "mapBytes");
        }

        public override void RegisterComponents(WorldAssembly a)
        {
            EntityStore entities = a.Sim.Entities;
            a.Positions = entities.Register<Position>("interim.position", new PositionSerializer());
            a.Colliders = entities.Register<Collider>("interim.collider", new ColliderSerializer());
            a.Intents = entities.Register<MoveIntent>(MoveIntent.ComponentId, new MoveIntentSerializer());
            if (a.Path)
                a.Waypoints = entities.Register<MoveWaypoint>(MoveWaypoint.ComponentId, new MoveWaypointSerializer());
        }

        public override void PlaceWall(WorldAssembly a, WcMapWall wall, EntityId entity)
        {
            WcMap map = a.Map;
            var origin = new SimVec3(map.Min.X, map.FlatHeight, map.Min.Z);
            a.Sim.Entities.Set(a.Positions, entity, new Position(origin + wall.LocalCenter));
            a.Sim.Entities.Set(a.Colliders, entity, Collider.Box(wall.HalfX, wall.Height, wall.HalfZ,
                BoxYaw.Identity, layer: map.LayerMask(wall.LayerName), isStatic: true));
        }

        public override void PlaceUnit(WorldAssembly a, int index, EntityId unit)
        {
            UnitSpawn spawn = a.Recipe.Units[index];
            if (!a.IsDeployedAtStart(spawn))
                return;
            EntityStore entities = a.Sim.Entities;
            SimVec3 point = a.SpawnPoint(spawn);
            entities.Set(a.Positions, unit, new Position(point));
            entities.Set(a.Colliders, unit, new Collider(SimFix.Ratio(1, 4), SimFix.FromInt(1)));
            entities.Set(a.Intents, unit, new MoveIntent(point, WorldAssembly.SpawnSpeed(spawn), spawn.Squad, false));
            if (a.Path) entities.Set(a.Waypoints, unit, default);
        }

        // The locomotion needs the player control and the crowd pass rule, so the builder makes them first.
        public override void RegisterSystems(WorldAssembly a)
        {
            Simulation sim = a.Sim;
            PlayerControl controlled = a.Controlled;
            var locomotion = new InterimLocomotion(sim.Entities, a.WallMask,
                a.Path ? (a.Terrain != null ? a.Terrain.Nav : WcMapNavigation.Create(a.Map)) : null, a.Crowd,
                controlled == null ? null : new PlayerStep(sim, controlled), a.CrowdPass, a.Terrain);
            if (controlled != null) locomotion.Stop(controlled.Current);
            a.Locomotion = locomotion;
            sim.RegisterGameplay("interim.orders", new InterimOrders(sim.Entities, locomotion, a.Crowd ? WorldBuilder.CrowdSlotSpacing : default, controlled, a.Standing), SimSystemPhase.Requests);
            sim.RegisterGameplay("interim.locomotion", locomotion, SimSystemPhase.Movement);
        }
    }
}
