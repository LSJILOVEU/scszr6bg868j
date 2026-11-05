using NUnit.Framework;
using WarCore.Sim;
using WarCore.Tests.Oracle;

namespace WarCore.Tests
{
    public sealed class DdaCollectorTests
    {
        [Test]
        public void CandidateSetEqualsOccupiedOracleCellsAndOutputsMatchNaive()
        {
            const ulong seed = 0xD1C011EC;
            const long size = 3 * SimFix.Scale;
            var random = new A1MathInputs(seed);
            var store = new EntityStore(64);
            var positions = store.Register<Position>("position");
            var colliders = store.Register<Collider>("collider");
            var grid = new GridSpatialQuery(store, positions, colliders, SimFix.FromRaw(size), 64, 2048);
            var bodies = new SpatialBody[64];
            for (int i = 0; i < bodies.Length; i++)
            {
                EntityId id = store.Create();
                SimVec3 position = A2SpatialInputs.V(Signed(ref random, 5) * size, Signed(ref random, 2) * size, Signed(ref random, 5) * size);
                var collider = new Collider(SimFix.FromRaw(size / 4), SimFix.FromRaw(size));
                store.Set(positions, id, new Position(position));
                store.Set(colliders, id, collider);
                bodies[i] = new SpatialBody(id, position, collider);
            }
            grid.Rebuild();
            var naive = new NaiveSpatialQuery(bodies);
            var expectedHits = new RayHit[64];
            var actualHits = new RayHit[64];
            int candidates = 0;
            for (int i = 0; i < 256; i++)
            {
                SimVec3 origin = A2SpatialInputs.V(Signed(ref random, 4) * size, 0, Signed(ref random, 4) * size);
                SimVec3 direction = A2SpatialInputs.V(Signed(ref random, 2), i % 3 == 0 ? 0 : Signed(ref random, 2), Signed(ref random, 2));
                if (direction.Equals(default(SimVec3)))
                    direction = A2SpatialInputs.V(1, 0, 0);
                long range = i % 16 == 0 ? 0 : size * 4;
                var cells = RayCellOracle.Enumerate(origin.X.Raw, origin.Y.Raw, origin.Z.Raw,
                    direction.X.Raw, direction.Y.Raw, direction.Z.Raw, range, size);
                int expectedCandidates = 0;
                foreach (SpatialBody body in bodies)
                {
                    long minX = Floor(body.Position.X.Raw - body.Collider.Radius.Raw, size);
                    long maxX = Floor(body.Position.X.Raw + body.Collider.Radius.Raw, size);
                    long minY = Floor(body.Position.Y.Raw - body.Collider.Height.Raw / 2, size);
                    long maxY = Floor(body.Position.Y.Raw + body.Collider.Height.Raw / 2, size);
                    long minZ = Floor(body.Position.Z.Raw - body.Collider.Radius.Raw, size);
                    long maxZ = Floor(body.Position.Z.Raw + body.Collider.Radius.Raw, size);
                    foreach (RayCellOracle.Entry cell in cells)
                    {
                        if (cell.X < minX || cell.X > maxX || cell.Y < minY || cell.Y > maxY || cell.Z < minZ || cell.Z > maxZ)
                            continue;
                        expectedCandidates++;
                        break;
                    }
                }
                int actual = grid.Ray(origin, direction, SimFix.FromRaw(range), actualHits);
                int expected = naive.Ray(origin, direction, SimFix.FromRaw(range), expectedHits);
                Assert.That(grid.LastCandidateCount, Is.EqualTo(expectedCandidates), "Candidate set: " + i);
                Assert.That(actual, Is.EqualTo(expected), "Hit count: " + i);
                for (int hit = 0; hit < expected; hit++)
                {
                    Assert.That(actualHits[hit].Entity, Is.EqualTo(expectedHits[hit].Entity));
                    Assert.That(actualHits[hit].Distance.Raw, Is.EqualTo(expectedHits[hit].Distance.Raw));
                }
                candidates += expectedCandidates;
            }
            Assert.That(candidates, Is.GreaterThan(0));
            Assert.That(candidates, Is.LessThan(256 * 64));
            TestContext.WriteLine("D1 collector: seed=0x{0:X}; 256 rays x 64 bodies; candidates={1}; exact oracle-cell union ratio=1.0; ordered naive outputs identical.", seed, candidates);
        }

        [Test]
        public void LongRayAtWorldLimitTraversesFineGridWithoutAllEntityFallback()
        {
            var store = new EntityStore(2);
            var positions = store.Register<Position>("position");
            var colliders = store.Register<Collider>("collider");
            var grid = new GridSpatialQuery(store, positions, colliders, SimFix.FromInt(3), 2, 64);
            for (int i = 0; i < 2; i++)
            {
                EntityId id = store.Create();
                store.Set(positions, id, new Position(SimVec3.Metres(0, i == 0 ? 0 : 500, 0)));
                store.Set(colliders, id, new Collider(default, default));
                grid.Upsert(id);
            }
            var hits = new RayHit[2];
            Assert.That(grid.Ray(SimVec3.Metres(-16384, 0, 0), SimVec3.Metres(16384, 0, 0), SimFix.FromInt(16384), hits), Is.EqualTo(1));
            Assert.That(hits[0].Distance.Raw, Is.EqualTo(1L << 30));
            Assert.That(grid.LastCandidateCount, Is.EqualTo(1));
        }

        [Test]
        public void ExactReferenceSeparatesTangentFromOneRawOutsideAndChecksEndCaps()
        {
            var body = new SpatialBody(new EntityId(0, 1), default, new Collider(SimFix.FromRaw(5), SimFix.FromRaw(18)));
            Assert.That(Exact(body, -10, 0, 5, 1, 0, 0, 10), Is.True);
            Assert.That(Exact(body, -10, 0, 6, 1, 0, 0, 10), Is.False);
            Assert.That(Exact(body, -10, 0, 5, 1, 0, 0, 9), Is.False);
            Assert.That(Exact(body, 0, 20, 0, 0, -1, 0, 11), Is.True);
            Assert.That(Exact(body, 0, 20, 0, 0, -1, 0, 10), Is.False);
            Assert.That(Exact(body, 0, -20, 0, 0, 1, 0, 11), Is.True);
            Assert.That(Exact(body, 0, -20, 0, 0, -1, 0, 100), Is.False);
            Assert.That(Exact(body, 0, 0, 0, 1, 1, 1, 0), Is.True);
        }

        private static bool Exact(SpatialBody body, long x, long y, long z, long dx, long dy, long dz, long range)
        {
            var ray = new SpatialRequest(SpatialQueryKind.Ray, A2SpatialInputs.V(x, y, z), default,
                A2SpatialInputs.V(dx, dy, dz), SimFix.FromRaw(range), default, 0);
            return ExactRayCapsule.Hits(in body, in ray);
        }

        private static long Signed(ref A1MathInputs random, long limit) => (long)(random.Next() % (ulong)(2 * limit + 1)) - limit;
        private static long Floor(long value, long size) => value >= 0 ? value / size : (value - size + 1) / size;
    }
}
