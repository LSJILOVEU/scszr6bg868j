using System;
using System.Collections.Generic;
using WarCore.Content;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    // Configurable assembly for full snapshot incompatibility and physical allocation-order tests.
    internal sealed class M4TestWorld
    {
        internal readonly Simulation Sim;
        internal readonly InterimLocomotion Locomotion;
        internal readonly ComponentType<Position> Positions;
        internal readonly ComponentType<MoveIntent> Intents;
        internal readonly ComponentType<MoveWaypoint> Waypoints;
        internal readonly int Count;

        internal M4TestWorld(WcMap map, UnitSpawn[] units, bool reverse = false, bool crowd = true, int spacing = 2)
        {
            Count = units.Length;
            var content = new StableContentManifest(1);
            var entries = new List<PlacedCatalogEntry>();
            uint mask = 0;
            foreach (var wall in map.Walls) { entries.Add(new PlacedCatalogEntry(wall.Chunk, wall.Placement)); mask |= map.LayerMask(wall.LayerName); }
            foreach (var unit in units) entries.Add(new PlacedCatalogEntry(1, unit.Key));
            Sim = Simulation.CreateRetainedWorld(41, new PlacedCatalog(content, entries.ToArray()),
                new SimConfig(entityCapacity: entries.Count), content: content);
            Positions = Sim.Entities.Register<Position>("interim.position", new PositionSerializer());
            var colliders = Sim.Entities.Register<Collider>("interim.collider", new ColliderSerializer());
            Intents = Sim.Entities.Register<MoveIntent>(MoveIntent.ComponentId, new MoveIntentSerializer());
            Waypoints = Sim.Entities.Register<MoveWaypoint>(MoveWaypoint.ComponentId, new MoveWaypointSerializer());
            Sim.ConfigureSpatial(Positions, colliders, SimFix.FromInt(2));
            Locomotion = new InterimLocomotion(Sim.Entities, mask, WcMapNavigation.Create(map), crowd);
            Sim.RegisterGameplay("interim.orders", new InterimOrders(Sim.Entities, Locomotion, SimFix.FromInt(spacing)), SimSystemPhase.Requests);
            Sim.RegisterGameplay("interim.locomotion", Locomotion, SimSystemPhase.Movement);
            foreach (var wall in map.Walls)
            {
                var id = Sim.Entities.Create(wall.Key);
                Sim.Entities.Set(Positions, id, new Position(new SimVec3(map.Min.X, map.FlatHeight, map.Min.Z) + wall.LocalCenter));
                Sim.Entities.Set(colliders, id, Collider.Box(wall.HalfX, wall.Height, wall.HalfZ, BoxYaw.Identity,
                    layer: map.LayerMask(wall.LayerName), isStatic: true));
            }
            for (int i = 0; i < Count; i++)
            {
                var spawn = units[reverse ? Count - 1 - i : i];
                var id = Sim.Entities.Create(StableEntityKey.Placed(1, spawn.Key));
                var point = M3TestWorld.Point(spawn.XMm, spawn.ZMm);
                Sim.Entities.Set(Positions, id, new Position(point));
                Sim.Entities.Set(colliders, id, new Collider(SimFix.Ratio(1, 4), SimFix.FromInt(1)));
                Sim.Entities.Set(Intents, id, new MoveIntent(point, SimFix.Ratio(spawn.SpeedMmPerSec, 30000), spawn.Squad, false));
                Sim.Entities.Set(Waypoints, id, default);
            }
            Sim.CompleteSpatialBoundary();
        }

        internal EntityId Unit(int i) => Sim.Entities.Resolve(new EntityLink(StableEntityKey.Placed(1, (ulong)i + 1).Id));
        internal SimVec3 Point(int i) => Sim.Entities.Get(Positions, Unit(i)).Value;
        internal MoveIntent Intent(int i) => Sim.Entities.Get(Intents, Unit(i));
        internal SimVec3[] Points()
        {
            var points = new SimVec3[Count];
            for (int i = 0; i < Count; i++) points[i] = Point(i);
            return points;
        }
        internal static InputFrame Command(int squad, long x, long z) => new InputFrame(default, default,
            squad: new SquadCommand(SquadCommandKind.MoveTo, squad, M3TestWorld.Point(x, z), default));
        internal void Order(int i, long x, long z) => Locomotion.Order(Unit(i), M3TestWorld.Point(x, z));
    }
}
