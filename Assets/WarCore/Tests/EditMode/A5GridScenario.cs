using System;
using System.Collections.Generic;
using WarCore.Sim;

namespace WarCore.Tests
{
    internal sealed class A5GridScenario
    {
        internal const int Capacity = 8;
        internal readonly EntityStore Store = new EntityStore(Capacity);
        internal readonly ComponentType<Position> Positions;
        internal readonly ComponentType<Collider> Colliders;
        internal readonly GridSpatialQuery Grid;

        internal A5GridScenario()
        {
            Positions = Store.Register<Position>("position");
            Colliders = Store.Register<Collider>("collider");
            Grid = NewGrid();
        }

        internal GridSpatialQuery NewGrid() => new GridSpatialQuery(Store, Positions, Colliders, SimFix.FromInt(8), Capacity, 1024);

        internal EntityId CreateBody(SimVec3 position, int radius, int height)
        {
            EntityId entity = Store.Create();
            SetBody(entity, position, radius, height);
            return entity;
        }

        internal void SetBody(EntityId entity, SimVec3 position, int radius, int height)
        {
            Store.Set(Positions, entity, new Position(position));
            Store.Set(Colliders, entity, new Collider(SimFix.FromInt(radius), SimFix.FromInt(height)));
            Grid.Upsert(entity);
        }

        internal NaiveSpatialQuery Naive()
        {
            var bodies = new List<SpatialBody>();
            foreach (EntityId entity in Store)
                if (Store.TryGet(Positions, entity, out Position position) && Store.TryGet(Colliders, entity, out Collider collider))
                    bodies.Add(new SpatialBody(entity, position.Value, collider));
            return new NaiveSpatialQuery(bodies);
        }

        internal static SpatialQueryKind[] QueryKinds() => new[]{SpatialQueryKind.Sphere, SpatialQueryKind.Sector, SpatialQueryKind.Capsule, SpatialQueryKind.Ray};

        internal readonly struct QueryResult
        {
            internal readonly EntityId[] Entities;
            internal readonly long[] Distances;

            internal QueryResult(EntityId[] entities, long[] distances)
            {
                Entities = entities;
                Distances = distances;
            }

            internal bool SameAs(QueryResult other)
            {
                bool same = Entities.Length == other.Entities.Length;
                int common = Entities.Length < other.Entities.Length ? Entities.Length : other.Entities.Length;
                for (int i = 0; i < common; i++)
                    same &= Entities[i].Equals(other.Entities[i]) && Distances[i] == other.Distances[i];
                return same;
            }

            public override string ToString()
            {
                var rows = new string[Entities.Length];
                for (int i = 0; i < rows.Length; i++)
                    rows[i] = Entities[i].Id + ":" + Entities[i].Generation + "@" + Distances[i];
                return "[" + string.Join(",", rows) + "]";
            }
        }

        internal static QueryResult Query(ISpatialQuery query, SpatialQueryKind kind, SimVec3 focus)
        {
            var entities = new EntityId[Capacity];
            var distances = new long[Capacity];
            SimVec3 start = focus - SimVec3.Metres(32, 0, 0);
            int count;
            switch (kind)
            {
                case SpatialQueryKind.Sphere:
                    count = query.Sphere(focus, SimFix.FromInt(24), entities);
                    break;
                case SpatialQueryKind.Sector:
                    count = query.Sector(start, SimFix.FromInt(64), SimVec3.Metres(1, 0, 0), 600, SimFix.FromInt(16), entities);
                    break;
                case SpatialQueryKind.Capsule:
                    count = query.Capsule(start, focus + SimVec3.Metres(32, 0, 0), SimFix.FromInt(4), entities);
                    break;
                case SpatialQueryKind.Ray:
                    var hits = new RayHit[Capacity];
                    count = query.Ray(start, SimVec3.Metres(1, 0, 0), SimFix.FromInt(64), hits);
                    for (int i = 0; i < count; i++)
                    {
                        entities[i] = hits[i].Entity;
                        distances[i] = hits[i].Distance.Raw;
                    }

                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }

            Array.Resize(ref entities, count);
            Array.Resize(ref distances, count);
            return new QueryResult(entities, distances);
        }
    }
}
