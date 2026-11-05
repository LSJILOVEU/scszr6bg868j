using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class SpatialQueryTests
    {
        private static SimFix F(long value) => SimFix.FromInt(value);
        private static SimVec3 V(long x, long z) => SimVec3.Metres(x, 0, z);
        private static SpatialBody Point(int id, int x, int z) => new SpatialBody(new EntityId(id, 1), V(x, z), default);
        private static SpatialBody Body(int id, int x, int y, int z, int radius, int height) => new SpatialBody(new EntityId(id, 1), SimVec3.Metres(x, y, z), new Collider(F(radius), F(height)));
        internal static GridSpatialQuery Grid(IEnumerable<SpatialBody> source, int cellSize = 8)
        {
            SpatialBody[] bodies = source.ToArray();
            int capacity = bodies.Length == 0 ? 1 : bodies.Max(b => b.Entity.Id) + 1;
            var store = new EntityStore(capacity);
            var positions = store.Register<Position>("position");
            var colliders = store.Register<Collider>("collider");
            for (int i = 0; i < capacity; i++)
                store.Create();
            foreach (SpatialBody body in bodies)
            {
                store.Set(positions, body.Entity, new Position(body.Position));
                store.Set(colliders, body.Entity, body.Collider);
            }

            var grid = new GridSpatialQuery(store, positions, colliders, F(cellSize), capacity, 16384);
            grid.Rebuild();
            return grid;
        }

        private static int[] Sphere(ISpatialQuery query, SimVec3 center, int radius)
        {
            var output = new EntityId[512];
            return output.Take(query.Sphere(center, F(radius), output)).Select(e => e.Id).ToArray();
        }

        private static int[] Sector(ISpatialQuery query, SimVec3 center, int radius, SimVec3 direction, int cosine, int height = 0)
        {
            var output = new EntityId[512];
            return output.Take(query.Sector(center, F(radius), direction, cosine, F(height), output)).Select(e => e.Id).ToArray();
        }

        private static int[] Capsule(ISpatialQuery query, SimVec3 start, SimVec3 end, int radius)
        {
            var output = new EntityId[512];
            return output.Take(query.Capsule(start, end, F(radius), output)).Select(e => e.Id).ToArray();
        }

        private static int[] Ray(ISpatialQuery query, SimVec3 origin, SimVec3 direction, int range)
        {
            var output = new RayHit[512];
            return output.Take(query.Ray(origin, direction, F(range), output)).Select(e => e.Entity.Id).ToArray();
        }

        [Test]
        public void CircleIncludesBoundaryAndBehindButExcludesOutsideAndSupportsEmpty()
        {
            var query = new NaiveSpatialQuery(new[]{Point(8, 3, 4), Point(2, -5, 0), Point(4, 0, 0), Point(1, 5, 1)});
            Assert.That(Sphere(query, default, 5), Is.EqualTo(new[]{2, 4, 8}));
            Assert.That(Sphere(query, default, 0), Is.EqualTo(new[]{4}));
            Assert.That(Sphere(query, V(20, 20), 1), Is.Empty);
        }

        [Test]
        public void SectorIncludesRadialAndAngularBoundariesAndRejectsBehindAndEmpty()
        {
            var query = new NaiveSpatialQuery(new[]{Point(8, 3, 4), Point(2, -5, 0), Point(4, 0, 0), Point(1, 2, 4), Point(7, 5, 0), Point(9, 6, 0)});
            Assert.That(Sector(query, default, 5, V(1, 0), 600), Is.EqualTo(new[]{4, 7, 8}));
            Assert.That(Sector(query, V(20, 20), 1, V(1, 0), 600), Is.Empty);
        }

        [Test]
        public void SectorSupportsHalfPlaneWideAndFullAngles()
        {
            var query = new NaiveSpatialQuery(new[]{Point(3, -3, 4), Point(2, -5, 0), Point(1, 0, 5), Point(4, 5, 0)});
            Assert.That(Sector(query, default, 5, V(1, 0), 0), Is.EqualTo(new[]{1, 4}));
            Assert.That(Sector(query, default, 5, V(1, 0), -600), Is.EqualTo(new[]{1, 3, 4}));
            Assert.That(Sector(query, default, 5, V(1, 0), -1000), Is.EqualTo(new[]{1, 2, 3, 4}));
            Assert.That(Sector(query, default, 5, V(1, 0), 1000), Is.EqualTo(new[]{4}));
        }

        [Test]
        public void CapsuleIncludesSideAndEndCapsRejectsBehindCapAndSupportsEmpty()
        {
            var query = new NaiveSpatialQuery(new[]{Point(8, 2, 1), Point(4, -1, 0), Point(2, 6, 0), Point(3, -2, 0), Point(1, 2, 2), Point(9, 0, 0)});
            Assert.That(Capsule(query, default, V(5, 0), 1), Is.EqualTo(new[]{2, 4, 8, 9}));
            Assert.That(Capsule(query, V(20, 20), V(30, 20), 1), Is.Empty);
            Assert.That(Capsule(query, default, default, 1), Is.EqualTo(new[]{4, 9}));
            Assert.That(Capsule(query, default, V(5, 0), 0), Is.EqualTo(new[]{9}));
        }

        [Test]
        public void RayIncludesOriginAndRangeBoundaryRejectsBehindAndOffLineAndSupportsEmpty()
        {
            var query = new NaiveSpatialQuery(new[]{Point(8, 5, 0), Point(4, -1, 0), Point(2, 6, 0), Point(3, 2, 1), Point(1, 2, 0), Point(9, 0, 0)});
            Assert.That(Ray(query, default, V(20, 0), 5), Is.EqualTo(new[]{9, 1, 8}));
            Assert.That(Ray(query, V(20, 20), V(1, 0), 1), Is.Empty);
            Assert.That(Ray(query, default, V(1, 0), 0), Is.EqualTo(new[]{9}));
        }

        [Test]
        public void QueriesHandleDiagonalGeometryAndMaximumCoordinatesWithoutOverflow()
        {
            var diagonal = new NaiveSpatialQuery(new[]{Point(1, 3, 4), Point(2, -3, -4), Point(3, 6, 8)});
            Assert.That(Ray(diagonal, default, V(3, 4), 5), Is.EqualTo(new[]{1}));
            Assert.That(Capsule(diagonal, default, V(3, 4), 0), Is.EqualTo(new[]{1}));
            var maximum = Grid(new[]{Point(1, 16384, 16384), Point(2, -16384, -16384)}, 256);
            Assert.That(Capsule(maximum, V(16380, 16380), V(16384, 16384), 0), Is.EqualTo(new[]{1}));
            Assert.That(Capsule(maximum, V(-16384, -16384), V(-16380, -16380), 0), Is.EqualTo(new[]{2}));
            Assert.That(Sector(maximum, default, 16384, V(16384, 16384), 600), Is.Empty);
            Assert.Throws<ArgumentOutOfRangeException>(() => Capsule(maximum, V(-16384, -16384), V(16384, 16384), 0));
        }

        [Test]
        public void InputOrderAndCallerMutationDoNotAffectAnyQuery()
        {
            var source = new List<SpatialBody>{Point(4, 4, 0), Point(1, 1, 0), Point(3, 3, 0)};
            var a = new NaiveSpatialQuery(source);
            source.Reverse();
            var b = new NaiveSpatialQuery(source);
            source.Clear();
            Assert.That(Sphere(a, default, 5), Is.EqualTo(new[]{1, 3, 4}));
            Assert.That(Sphere(a, default, 5), Is.EqualTo(Sphere(b, default, 5)));
            Assert.That(Sector(a, default, 5, V(1, 0), 0), Is.EqualTo(Sector(b, default, 5, V(1, 0), 0)));
            Assert.That(Capsule(a, default, V(5, 0), 0), Is.EqualTo(Capsule(b, default, V(5, 0), 0)));
            Assert.That(Ray(a, default, V(1, 0), 5), Is.EqualTo(Ray(b, default, V(1, 0), 5)));
        }

        [Test]
        public void InvalidGeometryAndDuplicateHandlesAreRejected()
        {
            var query = new NaiveSpatialQuery(new SpatialBody[0]);
            Assert.Throws<ArgumentOutOfRangeException>(() => Sphere(query, default, -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => Capsule(query, default, default, 16385));
            Assert.Throws<ArgumentException>(() => Ray(query, default, default, 1));
            Assert.Throws<ArgumentException>(() => Sector(query, default, 1, default, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => Sector(query, default, 1, V(1, 0), 1001));
            Assert.Throws<ArgumentException>(() => new NaiveSpatialQuery(new[]{Point(1, 0, 0), Point(1, 2, 0)}));
            Assert.Throws<ArgumentOutOfRangeException>(() => Sphere(query, V(16385, 0), 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Collider(F(2), F(3)));
        }

        [Test]
        public void HeightSeparationSideHitsCapHitsAndDistanceTiesHaveHandComputedAnswers()
        {
            var grid = Grid(new[]{Body(5, 5, 0, 3, 5, 12), Body(2, 5, 0, -3, 5, 12), Body(8, 0, 30, 0, 1, 4)});
            var hits = new RayHit[10];
            int count = grid.Ray(default, V(1, 0), F(10), hits);
            Assert.That(count, Is.EqualTo(2));
            Assert.That(hits[0].Entity.Id, Is.EqualTo(2));
            Assert.That(hits[1].Entity.Id, Is.EqualTo(5));
            Assert.That(hits[0].Distance, Is.EqualTo(F(1)));
            Assert.That(hits[1].Distance, Is.EqualTo(F(1)));
            Assert.That(Sphere(grid, default, 3), Has.No.Member(8));
            Assert.That(Sector(grid, default, 10, V(1, 0), 0, 3), Has.No.Member(8));
            Assert.That(Capsule(grid, default, V(10, 0), 1), Has.No.Member(8));
            count = grid.Ray(SimVec3.Metres(0, 20, 0), SimVec3.Metres(0, 1, 0), F(10), hits);
            Assert.That(count, Is.EqualTo(1));
            Assert.That(hits[0].Entity.Id, Is.EqualTo(8));
            Assert.That(hits[0].Distance, Is.EqualTo(F(8)));
        }

        [Test]
        public void KilometreQueriesAndLargeCollidersHaveIndependentKnownAnswers()
        {
            var grid = Grid(new[]{Body(1, 1000, 0, 0, 10, 40), Body(2, 5000, 0, 300, 500, 1400), Body(3, -2000, 0, 0, 20, 80), Body(4, 4000, 500, 0, 10, 40)}, 256);
            Assert.That(Sphere(grid, default, 990), Is.EqualTo(new[]{1}));
            Assert.That(Sphere(grid, default, 989), Is.Empty);
            Assert.That(Sector(grid, default, 5000, V(1, 0), 1000, 0), Is.EqualTo(new[]{1, 2}));
            Assert.That(Capsule(grid, default, V(5000, 0), 0), Is.EqualTo(new[]{1, 2}));
            var hits = new RayHit[10];
            Assert.That(grid.Ray(default, V(1, 0), F(5000), hits), Is.EqualTo(2));
            Assert.That(hits[0].Distance, Is.EqualTo(F(990)));
            Assert.That(hits[1].Distance, Is.EqualTo(F(4600)));
            var boundaries = Grid(new[]{Body(1, 16384, 0, 0, 8, 16), Body(2, -16384, 0, 0, 8, 16), Point(3, 8, 0)});
            Assert.That(Sphere(boundaries, V(16375, 0), 1), Is.EqualTo(new[]{1}));
            Assert.That(Sphere(boundaries, V(-16375, 0), 1), Is.EqualTo(new[]{2}));
            Assert.That(Sphere(boundaries, default, 8), Is.EqualTo(new[]{3}));
        }

        [Test]
        public void TwoHundredSeededScenesMatchAllQueriesIncludingOrderAndDistances()
        {
            var random = new Random(918731);
            var expected = new EntityId[300];
            var actual = new EntityId[300];
            var expectedHits = new RayHit[300];
            var actualHits = new RayHit[300];
            for (int scene = 0; scene < 200; scene++)
            {
                int count = scene == 0 ? 0 : scene == 199 ? 300 : random.Next(301);
                var bodies = new SpatialBody[count];
                for (int i = 0; i < count; i++)
                {
                    int radius = i % 23 == 0 ? 12 : random.Next(3);
                    bodies[i] = Body(i, random.Next(-100, 101), random.Next(-20, 21), random.Next(-100, 101), radius, radius * 2 + random.Next(9));
                }

                var grid = Grid(bodies);
                var naive = new NaiveSpatialQuery(bodies);
                SimVec3 origin = SimVec3.Metres(random.Next(-30, 31), random.Next(-10, 11), random.Next(-30, 31));
                SimVec3 end = origin + SimVec3.Metres(random.Next(-40, 41), random.Next(-10, 11), random.Next(-40, 41));
                SimVec3 direction = V(random.Next(1, 11), random.Next(-10, 11));
                SimFix radiusQ = F(random.Next(71));
                int cosine = random.Next(-1000, 1001);
                int n = naive.Sphere(origin, radiusQ, expected);
                Assert.That(grid.Sphere(origin, radiusQ, actual), Is.EqualTo(n), "Sphere scene " + scene);
                Assert.That(actual.Take(n), Is.EqualTo(expected.Take(n)));
                n = naive.Cylinder(origin, radiusQ, F(10), expected);
                Assert.That(grid.Cylinder(origin, radiusQ, F(10), actual), Is.EqualTo(n), "Cylinder scene " + scene);
                Assert.That(actual.Take(n), Is.EqualTo(expected.Take(n)));
                n = naive.Sector(origin, radiusQ, direction, cosine, F(10), expected);
                Assert.That(grid.Sector(origin, radiusQ, direction, cosine, F(10), actual), Is.EqualTo(n), "Sector scene " + scene);
                Assert.That(actual.Take(n), Is.EqualTo(expected.Take(n)));
                n = naive.Capsule(origin, end, F(3), expected);
                Assert.That(grid.Capsule(origin, end, F(3), actual), Is.EqualTo(n), "Capsule scene " + scene);
                Assert.That(actual.Take(n), Is.EqualTo(expected.Take(n)));
                direction += SimVec3.Metres(0, random.Next(-3, 4), 0);
                n = naive.Ray(origin, direction, F(100), expectedHits);
                Assert.That(grid.Ray(origin, direction, F(100), actualHits), Is.EqualTo(n), "Ray scene " + scene);
                for (int i = 0; i < n; i++)
                {
                    Assert.That(actualHits[i].Entity, Is.EqualTo(expectedHits[i].Entity));
                    Assert.That(actualHits[i].Distance, Is.EqualTo(expectedHits[i].Distance));
                }
            }
        }

        [Test]
        public void GridUpdatesRemovalReuseDeduplicationAndLargeEmptyQueryPruning()
        {
            var store = new EntityStore(3);
            var positions = store.Register<Position>("p");
            var colliders = store.Register<Collider>("c");
            EntityId id = store.Create();
            store.Set(positions, id, new Position(V(0, 0)));
            store.Set(colliders, id, new Collider(F(12), F(30)));
            var grid = new GridSpatialQuery(store, positions, colliders, F(8), 3, 512);
            grid.Rebuild();
            Assert.That(grid.MembershipCount, Is.GreaterThan(1));
            Assert.That(Sphere(grid, default, 0), Is.EqualTo(new[]{id.Id}));
            store.Get(positions, id).Value = V(10000, 10000);
            grid.Upsert(id);
            Assert.That(Sphere(grid, default, 5000), Is.Empty);
            Assert.That(grid.LastCandidateCount, Is.Zero);
            Assert.That(grid.LastVisitedCells, Is.LessThan(20));
            Assert.That(Sphere(grid, V(10000, 10000), 0), Is.EqualTo(new[]{id.Id}));
            store.Destroy(id);
            grid.Upsert(id);
            EntityId fresh = store.Create();
            store.Set(positions, fresh, new Position(default));
            store.Set(colliders, fresh, new Collider(F(1), F(2)));
            grid.Upsert(fresh);
            Assert.That(grid.Remove(id), Is.False);
            Assert.That(Sphere(grid, default, 0), Is.EqualTo(new[]{fresh.Id}));
            Assert.Throws<ArgumentException>(() => grid.Sphere(default, F(1), new EntityId[0]));
        }
    }
}
