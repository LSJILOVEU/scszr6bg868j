using System;
using System.Collections.Generic;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    internal sealed class LayerQueryFixture
    {
        internal readonly Simulation Sim;
        internal readonly ComponentType<Position> Positions;
        internal readonly ComponentType<Collider> Colliders;
        internal readonly GridSpatialQuery Grid;
        internal readonly EntityId[] Entities = new EntityId[128];
        internal readonly EntityId[] ExpectedEntities = new EntityId[128];
        internal readonly RayHit[] Rays = new RayHit[128];
        internal readonly RayHit[] ExpectedRays = new RayHit[128];

        internal LayerQueryFixture()
        {
            Sim = new Simulation(0x3A001, config: new SimConfig(4, 16, 16, 128, 16));
            Positions = Sim.Entities.Register<Position>("position", new PositionSerializer());
            Colliders = Sim.Entities.Register<Collider>("collider", new ColliderSerializer());
            Grid = new GridSpatialQuery(Sim.Entities, Positions, Colliders, SimFix.FromInt(3), 128, 8192);
        }

        internal EntityId Add(SimVec3 position, Collider collider)
        {
            EntityId entity = Sim.Entities.Create();
            Sim.Entities.Set(Positions, entity, new Position(position));
            Sim.Entities.Set(Colliders, entity, collider);
            Grid.Upsert(entity);
            return entity;
        }

        internal void Populate()
        {
            int[] x = { 6, 2, 2, 4, 0, 5, 3 };
            uint[] layers = { 1, 2, 0x80000000, 3, 0x80000002, uint.MaxValue, 0 };
            for (int i = 0; i < x.Length; i++)
                Add(SimVec3.Metres(x[i], 0, 0), new Collider(SimFix.Ratio(1, 4), SimFix.FromInt(2), layers[i]));
        }

        internal NaiveSpatialQuery Oracle()
        {
            var bodies = new List<SpatialBody>();
            foreach (EntityId entity in Sim.Entities)
                bodies.Add(new SpatialBody(entity, Sim.Entities.Get(Positions, entity).Value, Sim.Entities.Get(Colliders, entity)));
            return new NaiveSpatialQuery(bodies);
        }

        internal static SpatialRequest Request(SpatialQueryKind kind)
        {
            SimVec3 direction = SimVec3.Metres(kind == SpatialQueryKind.Capsule ? 8 : 1, 0, 0);
            return new SpatialRequest(kind, default, SimFix.FromInt(8), direction, SimFix.FromInt(8), SimFix.FromInt(4), 600);
        }

        internal static int Run(ISpatialQuery query, SpatialRequest request, uint mask, Span<EntityId> entities, Span<RayHit> rays, bool legacy = false)
        {
            switch (request.Kind)
            {
                case SpatialQueryKind.Sphere:
                    return legacy ? query.Sphere(request.Origin, request.Radius, entities)
                        : query.Sphere(request.Origin, request.Radius, mask, entities);
                case SpatialQueryKind.Cylinder:
                    return legacy ? query.Cylinder(request.Origin, request.Radius, request.HalfHeight, entities)
                        : query.Cylinder(request.Origin, request.Radius, request.HalfHeight, mask, entities);
                case SpatialQueryKind.Sector:
                    return legacy ? query.Sector(request.Origin, request.Radius, request.Direction, request.Cosine, request.HalfHeight, entities)
                        : query.Sector(request.Origin, request.Radius, request.Direction, request.Cosine, request.HalfHeight, mask, entities);
                case SpatialQueryKind.Capsule:
                    return legacy ? query.Capsule(request.Origin, request.Origin + request.Direction, request.Radius, entities)
                        : query.Capsule(request.Origin, request.Origin + request.Direction, request.Radius, mask, entities);
                case SpatialQueryKind.Ray:
                    return legacy ? query.Ray(request.Origin, request.Direction, request.Range, rays)
                        : query.Ray(request.Origin, request.Direction, request.Range, mask, rays);
                default:
                    throw new ArgumentOutOfRangeException(nameof(request));
            }
        }
    }
}
