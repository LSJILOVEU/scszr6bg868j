using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Sim;
using WarCore.Tests.Oracle;

namespace WarCore.Tests
{
    internal sealed class QueryModeFixture
    {
        internal readonly EntityStore Store;
        internal readonly ComponentType<Position> Positions;
        internal readonly ComponentType<Collider> Colliders;
        internal readonly GridSpatialQuery Grid;
        internal readonly List<SpatialBody> Bodies = new List<SpatialBody>();
        internal readonly SpatialQueryDiagnostics Counters = new SpatialQueryDiagnostics();

        internal QueryModeFixture(long cellRaw = 3 * SimFix.Scale, int capacity = 768, int memberships = 16384)
        {
            Store = new EntityStore(capacity);
            Positions = Store.Register<Position>("position");
            Colliders = Store.Register<Collider>("collider");
            Grid = new GridSpatialQuery(Store, Positions, Colliders, SimFix.FromRaw(cellRaw), capacity, memberships);
            Grid.Diagnostics = Counters;
        }

        internal EntityId Add(SimVec3 position, long radiusRaw = 0, long heightRaw = 0, uint layer = 1)
        {
            EntityId entity = Store.Create();
            Set(entity, position, radiusRaw, heightRaw, layer);
            return entity;
        }

        internal void Set(EntityId entity, SimVec3 position, long radiusRaw, long heightRaw, uint layer)
            => Set(entity, position, new Collider(SimFix.FromRaw(radiusRaw), SimFix.FromRaw(heightRaw), layer));

        internal EntityId Add(SimVec3 position, Collider collider)
        {
            EntityId entity = Store.Create();
            Set(entity, position, collider);
            return entity;
        }

        internal void Set(EntityId entity, SimVec3 position, Collider collider)
        {
            Store.Set(Positions, entity, new Position(position));
            Store.Set(Colliders, entity, collider);
            Grid.Upsert(entity);
            var body = new SpatialBody(entity, position, collider);
            if (entity.Id == Bodies.Count)
                Bodies.Add(body);
            else
                Bodies[entity.Id] = body;
        }

        internal List<SpatialHit> Oracle(SpatialRequest request, SimVec3 reference)
            => QueryModeOracle.Query(Bodies, Store, Colliders, request, reference);

        internal static SpatialRequest Request(SpatialQueryKind kind, uint mask = uint.MaxValue)
        {
            SimVec3 forward = SimVec3.Metres(1, 0, 0);
            SimFix reach = SimFix.FromInt(16);
            switch (kind)
            {
                case SpatialQueryKind.Sphere:
                    return SpatialQueries.Sphere(default, reach, mask);
                case SpatialQueryKind.Cylinder:
                    return SpatialQueries.Cylinder(default, reach, reach, mask);
                case SpatialQueryKind.Sector:
                    return SpatialQueries.Sector(default, reach, forward, 600, reach, mask);
                case SpatialQueryKind.Capsule:
                    return SpatialQueries.Capsule(default, SimVec3.Metres(16, 0, 0), SimFix.FromInt(4), mask);
                default:
                    return SpatialQueries.Ray(default, forward, reach, mask);
            }
        }

        internal static void Same(SpatialHit actual, SpatialHit expected)
        {
            Assert.That(actual.Entity, Is.EqualTo(expected.Entity));
            Assert.That(actual.Distance.Raw, Is.EqualTo(expected.Distance.Raw));
        }

        internal void Check(SpatialRequest request, SimVec3 reference, int k, int length)
        {
            List<SpatialHit> expected = Oracle(request, reference);
            Assert.That(Grid.Any(in request), Is.EqualTo(expected.Count != 0));
            bool found = Grid.Nearest(in request, reference, out SpatialHit nearest);
            Assert.That(found, Is.EqualTo(expected.Count != 0));
            Same(nearest, found ? expected[0] : default);
            var buffer = new SpatialHit[length];
            SpatialQueryResult result = Grid.AllCapped(in request, reference, k, buffer);
            int written = System.Math.Min(expected.Count, System.Math.Min(k, length));
            Assert.That(result.Written, Is.EqualTo(written));
            Assert.That(result.TotalHits, Is.EqualTo(expected.Count));
            Assert.That(result.Truncated, Is.EqualTo(expected.Count > written));
            Assert.That(Grid.LastTruncatedHits, Is.EqualTo(expected.Count - written));
            for (int i = 0; i < written; i++)
                Same(buffer[i], expected[i]);
        }
    }
}
