using System;
using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    [TestFixture, Category("StableIdentity")]
    public sealed class StableQueryTests
    {
        private static SimVec3 V(int x, int y, int z) => new SimVec3(SimFix.FromInt(x), SimFix.FromInt(y), SimFix.FromInt(z));

        [TestCase(false,false), TestCase(true,false), TestCase(false,true), TestCase(true,true)]
        public void EqualDistancesUseUnsignedStableKeysInEveryQueryMode(bool reverse, bool bypass)
        {
            var sim = new WarCore.Sim.Sim(1,config:new SimConfig(entityCapacity:16,spatialMaxCellsPerBody:bypass ? 1 : 64),identityMode:IdentityMode.StableWorld);
            var entities = sim.Entities;
            var positions = entities.Register<Position>("p", new PositionSerializer());
            var colliders = entities.Register<Collider>("c", new ColliderSerializer());
            var keys = new StableEntityKey[8];
            for (int i = 0; i < keys.Length; i++)
                keys[i] = StableEntityKey.Placed(1, (ulong)i);
            var expected = new ulong[keys.Length];
            for (int i = 0; i < keys.Length; i++)
            {
                var key = keys[reverse ? keys.Length - i - 1 : i];
                EntityId entity = entities.Create(key);
                entities.Set(positions, entity, new Position(V(0, 0, 0)));
                entities.Set(colliders, entity, new Collider(SimFix.FromInt(1), SimFix.FromInt(2), 1, isStatic: i % 2 == 0));
                expected[i] = key.Id.Value;
            }
            Array.Sort(expected);
            var grid = sim.ConfigureSpatial(positions,colliders,SimFix.FromInt(1));
            sim.CompleteWarmup();
            Assert.That(sim.SpatialIndex.LargeBodyCount,Is.EqualTo(bypass ? 8 : 0));
            var requests = new[] {
                SpatialQueries.Sphere(default, SimFix.FromInt(4)),
                SpatialQueries.Cylinder(default, SimFix.FromInt(4), SimFix.FromInt(4)),
                SpatialQueries.Sector(default, SimFix.FromInt(4), V(1,0,0), -1000, SimFix.FromInt(4)),
                SpatialQueries.Capsule(default, V(4,0,0), SimFix.FromInt(1)),
                SpatialQueries.Ray(default, V(1,0,0), SimFix.FromInt(4)) };
            var hits = new SpatialHit[8];
            var full = new EntityId[8];
            var rays = new RayHit[8];
            foreach (var request in requests)
            {
                Assert.That(grid.Any(request), Is.True);
                Assert.That(grid.Nearest(request, out SpatialHit nearest), Is.True);
                Assert.That(entities.StableOf(nearest.Entity).Value, Is.EqualTo(expected[0]), "nearest");
                foreach (int k in new[] {0, 1, 512})
                {
                    SpatialQueryResult result = grid.AllCapped(request, k, hits);
                    Assert.That(result.TotalHits, Is.EqualTo(8));
                    Assert.That(result.Written, Is.EqualTo(Math.Min(k,8)));
                    for (int i = 0; i < result.Written; i++)
                        Assert.That(entities.StableOf(hits[i].Entity).Value, Is.EqualTo(expected[i]), "capped");
                }
                Assert.That(grid.AllCapped(request, 512, Span<SpatialHit>.Empty).TotalHits, Is.EqualTo(8));
            }
            Assert.That(grid.Sphere(default, SimFix.FromInt(4), full), Is.EqualTo(8));
            Assert.That(grid.Ray(default, V(1,0,0), SimFix.FromInt(4), rays), Is.EqualTo(8));
            for (int i = 0; i < 8; i++)
            {
                Assert.That(entities.StableOf(full[i]).Value, Is.EqualTo(expected[i]), "full range");
                Assert.That(entities.StableOf(rays[i].Entity).Value, Is.EqualTo(expected[i]), "full ray");
            }
        }
    }
}
