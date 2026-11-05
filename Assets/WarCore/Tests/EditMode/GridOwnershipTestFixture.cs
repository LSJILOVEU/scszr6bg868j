using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    // The legacy assembly seam stays visible: no fixture callback repairs an engine-owned index.
    internal sealed class GridOwnershipTestFixture
    {
        internal readonly Simulation Sim;
        internal EntityStore Store => Sim.Entities;
        internal readonly ComponentType<Position> Positions;
        internal readonly ComponentType<Collider> Colliders;
        internal readonly ComponentType<Movement> Movements;
        internal readonly GridSpatialQuery Grid;
        internal readonly int Capacity;

        internal GridOwnershipTestFixture(int capacity = 16, int memberships = 256)
        {
            Capacity = capacity;
            Sim = new Simulation(0x2BA1000, config: new SimConfig(entityCapacity: capacity));
            Positions = Store.Register<Position>("position", new PositionSerializer());
            Colliders = Store.Register<Collider>("collider", new ColliderSerializer());
            Movements = Store.Register<Movement>("movement", new MovementSerializer());
            var view = (SpatialQueryView)Sim.ConfigureSpatial(Positions, Colliders, SimFix.FromInt(8), memberships);
            Grid = new GridSpatialQuery(Sim.SpatialIndex, view.QueryContext);
        }

        internal EntityId CreateBody(SimVec3 position, int radius = 1, int height = 2)
        {
            EntityId entity = Store.Create();
            Store.Set(Positions, entity, new Position(position));
            Store.Set(Colliders, entity, new Collider(SimFix.FromInt(radius), SimFix.FromInt(height)));
            return entity;
        }

        internal MovementSystem AddMovement(INavProvider ground = null)
        {
            var navigation = new NavProviderRegistry();
            navigation.Register(MoveMode.Ground, ground ?? new FlatGroundNavProvider());
            var movement = new MovementSystem(Store, Positions, Movements, navigation, 20, Colliders);
            Sim.Register("movement", movement);
            return movement;
        }

        internal void InitializeIndex()
        {
            // Setup alone may seed the old explicit index; Step and Restore must maintain it themselves.
            Sim.CompleteSpatialBoundary();
        }

        internal NaiveSpatialQuery Naive()
        {
            var bodies = new List<SpatialBody>();
            foreach (EntityId entity in Store)
                if (Store.TryGet(Positions, entity, out Position position) && Store.TryGet(Colliders, entity, out Collider collider))
                    bodies.Add(new SpatialBody(entity, position.Value, collider));
            return new NaiveSpatialQuery(bodies);
        }

        internal static SpatialRequest Request(SpatialQueryKind kind, SimVec3 focus)
        {
            switch (kind)
            {
                case SpatialQueryKind.Sphere:
                    return SpatialQueries.Sphere(focus, SimFix.FromInt(1));
                case SpatialQueryKind.Cylinder:
                    return SpatialQueries.Cylinder(focus, SimFix.FromInt(1), SimFix.FromInt(2));
                case SpatialQueryKind.Sector:
                    return SpatialQueries.Sector(focus - SimVec3.Metres(4, 0, 0), SimFix.FromInt(8),
                        SimVec3.Metres(1, 0, 0), 600, SimFix.FromInt(32));
                case SpatialQueryKind.Capsule:
                    return SpatialQueries.Capsule(focus - SimVec3.Metres(2, 0, 0), focus + SimVec3.Metres(2, 0, 0), SimFix.FromInt(1));
                default:
                    return SpatialQueries.Ray(focus - SimVec3.Metres(64, 0, 0), SimVec3.Metres(1, 0, 0), SimFix.FromInt(128));
            }
        }

        internal static int Execute(ISpatialQuery query, in SpatialRequest request, EntityId[] ids, RayHit[] rays)
        {
            switch (request.Kind)
            {
                case SpatialQueryKind.Sphere:
                    return query.Sphere(request.Origin, request.Radius, request.LayerMask, ids);
                case SpatialQueryKind.Cylinder:
                    return query.Cylinder(request.Origin, request.Radius, request.HalfHeight, request.LayerMask, ids);
                case SpatialQueryKind.Sector:
                    return query.Sector(request.Origin, request.Radius, request.Direction, request.Cosine, request.HalfHeight, request.LayerMask, ids);
                case SpatialQueryKind.Capsule:
                    return query.Capsule(request.Origin, request.Origin + request.Direction, request.Radius, request.LayerMask, ids);
                default:
                    return query.Ray(request.Origin, request.Direction, request.Range, request.LayerMask, rays);
            }
        }

        internal static string Results(ISpatialQuery query, in SpatialRequest request, int capacity = 16)
        {
            var ids = new EntityId[capacity];
            var rays = new RayHit[capacity];
            int count = Execute(query, in request, ids, rays);
            var rows = new string[count];
            for (int i = 0; i < count; i++)
            {
                EntityId entity = request.Kind == SpatialQueryKind.Ray ? rays[i].Entity : ids[i];
                long distance = request.Kind == SpatialQueryKind.Ray ? rays[i].Distance.Raw : 0;
                rows[i] = entity.Id + ":" + entity.Generation + "@" + distance;
            }
            return "[" + string.Join(",", rows) + "]";
        }

        internal static void EqualResults(string expected, string actual, string reason)
        {
            Assert.That(actual, Is.EqualTo(expected), reason);
        }

        internal static void RequirePositionGuard(TestDelegate action)
        {
            Exception error = Assert.Catch<InvalidOperationException>(action,
                "A same-tick Position write after synchronization must be rejected in tests.");
            Assert.That(error.Message.ToLowerInvariant(), Does.Contain("position"), "The failure must identify the forbidden position write.");
        }
    }

    // Probes use the existing ordered-system seam until the product supplies explicit stage registration.
    internal sealed class GridPhaseProbeSystem : ISimSystem
    {
        private readonly Action<SimContext> action;
        public int Order { get; }

        internal GridPhaseProbeSystem(int order, Action<SimContext> action)
        {
            Order = order;
            this.action = action;
        }

        public void Step(SimContext context) => action(context);
        public void WriteState(StateHash hash) => hash.Add(Order);
    }
}
