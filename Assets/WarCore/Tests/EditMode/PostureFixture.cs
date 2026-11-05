using System.Collections.Generic;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    internal sealed class PostureFixture
    {
        internal readonly Simulation Sim;
        internal readonly ComponentType<Position> Positions;
        internal readonly ComponentType<Collider> Colliders;
        internal readonly ComponentType<Posture> Postures;
        internal readonly ComponentType<Movement> Movements;
        internal readonly PostureCatalog Catalog;
        internal readonly PostureGeometry Geometry;
        internal readonly PostureSystem System;
        internal readonly GridSpatialQuery Grid;
        internal readonly EntityId Unit;
        internal readonly EventTypeId TransitionType = new EventTypeRegistry().Register("posture-transition");
        internal PostureFixture(bool movement = false, int capacity = 128, PostureCatalog data = null)
        {
            Sim = new Simulation(7123, config: new SimConfig(4, 16, 16, capacity, capacity + 16));
            Positions = Sim.Entities.Register<Position>("position", new PositionSerializer());
            Colliders = Sim.Entities.Register<Collider>("collider", new ColliderSerializer());
            Postures = Sim.Entities.Register<Posture>("posture", new PostureSerializer());
            Movements = Sim.Entities.Register<Movement>("movement", new MovementSerializer());
            Catalog = data ?? Definitions();
            Geometry = new PostureGeometry(Sim.Entities, Postures, Catalog);
            var view = (SpatialQueryView)Sim.ConfigureSpatial(Positions, Colliders, SimFix.FromInt(2), capacity * 32, Geometry);
            Grid = new GridSpatialQuery(Sim.SpatialIndex, view.QueryContext);
            System = new PostureSystem(Sim.Entities, Postures, Positions, Colliders, Geometry, TransitionType, 10);
            Sim.Register("posture", System);
            Unit = Add(0, 0, 1);
            if (movement)
            {
                var nav = new NavProviderRegistry();
                nav.Register(MoveMode.Ground, new FlatGroundNavProvider());
                Sim.Register("movement", new MovementSystem(Sim.Entities, Positions, Movements, nav, 20, Colliders, Geometry));
                Sim.Entities.Set(Movements, Unit, new Movement(MoveMode.Ground, SimVec3.Metres(30, 0, 0)));
            }
        }

        internal static PostureCatalog Definitions() => new PostureCatalog(
            new PostureDef(1, SimFix.FromInt(3), SimFix.FromInt(1), SimFix.FromRaw(10), SimFix.FromInt(1)),
            new PostureDef(2, SimFix.Ratio(3, 2), SimFix.Ratio(3, 5), SimFix.FromRaw(-20), SimFix.Ratio(1, 2)),
            new PostureDef(3, SimFix.Ratio(1, 2), SimFix.Ratio(3, 10), SimFix.FromRaw(-30), SimFix.Ratio(1, 4)));

        internal EntityId Add(int x, int z, int posture)
        {
            EntityId entity = Sim.Entities.Create();
            Sim.Entities.Set(Positions, entity, new Position(new SimVec3(SimFix.FromInt(x), SimFix.Ratio(3, 2), SimFix.FromInt(z))));
            Sim.Entities.Set(Colliders, entity, new Collider(SimFix.Ratio(1, 4), SimFix.FromInt(3)));
            Sim.Entities.Set(Postures, entity, new Posture(posture));
            Sim.CompleteSpatialBoundary();
            return entity;
        }

        internal void Set(int posture)
        {
            Sim.Entities.Set(Postures, Unit, new Posture(posture));
            Sim.CompleteSpatialBoundary();
        }

        internal NaiveSpatialQuery Oracle(bool independentGeometry)
        {
            var bodies = new List<SpatialBody>();
            foreach (EntityId entity in Sim.Entities)
            {
                SimVec3 position = Sim.Entities.Get(Positions, entity).Value;
                Collider collider = Sim.Entities.Get(Colliders, entity);
                if (!independentGeometry)
                {
                    bodies.Add(Geometry.Body(entity, position, collider));
                    continue;
                }

                int id = Sim.Entities.TryGet(Postures, entity, out Posture posture) ? posture.CurrentId : 0;
                long height = id == 0 ? collider.Height.Raw : Catalog.Get(id).Height.Raw;
                // Independent integer formula keeps the foot fixed before rebuilding the capsule.
                long foot = position.Y.Raw - collider.Height.Raw / 2;
                var center = new SimVec3(position.X, SimFix.FromRaw(foot + height / 2), position.Z);
                bodies.Add(new SpatialBody(entity, center, new Collider(collider.Radius, SimFix.FromRaw(height))));
            }

            return new NaiveSpatialQuery(bodies);
        }
    }
}
