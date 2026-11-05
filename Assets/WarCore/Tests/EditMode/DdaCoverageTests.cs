using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Sim;
using WarCore.Tests.Oracle;

namespace WarCore.Tests
{
    public sealed class DdaCoverageTests
    {
        private sealed class Scene
        {
            internal readonly EntityStore Store = new EntityStore(1);
            internal readonly ComponentType<Position> Positions;
            internal readonly ComponentType<Collider> Colliders;
            internal readonly GridSpatialQuery Grid;
            internal readonly EntityId Entity;
            internal readonly RayHit[] Output = new RayHit[1];

            internal Scene(long size)
            {
                Positions = Store.Register<Position>("position");
                Colliders = Store.Register<Collider>("collider");
                Entity = Store.Create();
                Grid = new GridSpatialQuery(Store, Positions, Colliders, SimFix.FromRaw(size), 1, 512);
            }

            internal int Query(in SpatialBody body, in SpatialRequest ray)
            {
                Store.Set(Positions, Entity, new Position(body.Position));
                Store.Set(Colliders, Entity, body.Collider);
                Grid.Upsert(Entity);
                return Grid.Ray(ray.Origin, ray.Direction, ray.Range, Output);
            }
        }

        [Test]
        public void ThirtySixThousandExactHitsSurviveDdaIncludingMirrorsAndTranslations()
        {
            const ulong seed = 0xD1C0FE01;
            var random = new A1MathInputs(seed);
            var scenes = new Dictionary<long, Scene>();
            var groups = new int[12];
            int confirmed = 0;
            int misses = 0;
            for (int i = 0; i < 12000; i++)
            {
                int group = i % groups.Length;
                Build(ref random, i, out SpatialBody body, out SpatialRequest ray, out long size);
                if (!scenes.TryGetValue(size, out Scene scene))
                {
                    scene = new Scene(size);
                    scenes.Add(size, scene);
                }
                bool baselineHit = false;
                long baselineDistance = 0;
                for (int transform = 0; transform < 3; transform++)
                {
                    SimVec3 offset = transform == 2 ? A2SpatialInputs.V(size, size, size) : default;
                    SimVec3 position = Transform(body.Position, transform, offset);
                    SimVec3 origin = Transform(ray.Origin, transform, offset);
                    SimVec3 direction = transform == 1 ? A2SpatialInputs.V(-ray.Direction.X.Raw, -ray.Direction.Y.Raw, -ray.Direction.Z.Raw) : ray.Direction;
                    var transformedBody = new SpatialBody(body.Entity, position, body.Collider);
                    var transformedRay = new SpatialRequest(SpatialQueryKind.Ray, origin, default, direction, ray.Range, default, 0);
                    Assert.That(ExactRayCapsule.Hits(in transformedBody, in transformedRay), Is.True, "Generator must supply true hits: " + i);
                    confirmed++;
                    groups[group]++;
                    int found = scene.Query(in transformedBody, in transformedRay);
                    if (scene.Grid.LastCandidateCount != 1)
                        misses++;
                    Assert.That(found, Is.EqualTo(1), "Exact hit lost after candidate collection: " + i);
                    Assert.That(scene.Output[0].Entity, Is.EqualTo(scene.Entity));
                    Assert.That(OracleSpatialGeometry.Intersects(transformedBody, transformedRay, out SimFix exactDistance), Is.True);
                    Assert.That(scene.Output[0].Distance.Raw, Is.EqualTo(exactDistance.Raw), "Frozen raw distance: " + i);
                    if (transform == 0)
                    {
                        baselineHit = found != 0;
                        baselineDistance = found == 0 ? 0 : scene.Output[0].Distance.Raw;
                    }
                    else
                    {
                        Assert.That(found != 0, Is.EqualTo(baselineHit), "Mirror/translation hit invariance: " + i);
                        if (found != 0)
                            Assert.That(scene.Output[0].Distance.Raw, Is.EqualTo(baselineDistance), "Mirror/translation raw distance: " + i);
                    }
                }
            }
            Assert.That(confirmed, Is.EqualTo(36000));
            Assert.That(misses, Is.Zero);
            foreach (int count in groups)
                Assert.That(count, Is.EqualTo(3000));
            TestContext.WriteLine("D1 exact coverage: seed=0x{0:X}; 12000 generated rays x original/mirror/translated = {1} independently confirmed hits; false-negative candidates={2}; each of 12 groups=3000.", seed, confirmed, misses);
            TestContext.WriteLine("Groups: tangent, face start, edge start, corner/tie, zero axes, negative direction, range=0, range=16384m, inside, endpoint tangent, non-axis 3:4 tangent, world/height limits. Odd capsule heights included; exact reference uses BigInteger quadratic signs, no rounded roots.");
        }

        internal static SimVec3 Transform(SimVec3 value, int transform, SimVec3 offset)
            => transform == 1 ? A2SpatialInputs.V(-value.X.Raw, -value.Y.Raw, -value.Z.Raw) : value + offset;

        internal static void Build(ref A1MathInputs random, int index, out SpatialBody body, out SpatialRequest ray, out long size)
        {
            const long scale = SimFix.Scale;
            const long world = 1L << 30;
            int group = index % 12;
            size = (index & 1) == 0 ? 3 * scale : 8 * scale;
            long ox = ((long)(random.Next() % 33) - 16) * size + (long)(random.Next() % (ulong)size);
            long oy = ((long)(random.Next() % 5) - 2) * size + (long)(random.Next() % (ulong)size);
            long oz = ((long)(random.Next() % 33) - 16) * size + (long)(random.Next() % (ulong)size);
            if (group >= 1 && group <= 3)
                ox = 0;
            if (group >= 2 && group <= 3)
                oy = 0;
            if (group == 3)
                oz = 0;
            long radius = 1 + (long)(random.Next() % (ulong)(scale / 2));
            long spine = (long)(random.Next() % (ulong)scale);
            long travel = 1 + (long)(random.Next() % (ulong)(8 * scale));
            long speed = index % 7 == 0 ? world : 1 + (long)(random.Next() % (ulong)(world / 4));
            SimVec3 origin = A2SpatialInputs.V(ox, oy, oz);
            SimVec3 direction = A2SpatialInputs.V(speed, 0, 0);
            SimVec3 center = A2SpatialInputs.V(ox + travel, oy, oz + radius);
            long range = travel * 2;
            if (group == 2 || group == 3)
            {
                radius = 0;
                spine = 0;
                direction = A2SpatialInputs.V(speed, -speed, group == 3 ? speed : 0);
                center = A2SpatialInputs.V(ox + travel, oy - travel, oz + (group == 3 ? travel : 0));
            }
            if (group == 4)
            {
                direction = A2SpatialInputs.V(0, speed, 0);
                center = A2SpatialInputs.V(ox, oy + travel, oz);
            }
            if (group == 5)
            {
                direction = A2SpatialInputs.V(-speed, 0, 0);
                center = A2SpatialInputs.V(ox - travel, oy, oz + radius);
            }
            if (group == 6 || group == 8)
            {
                center = A2SpatialInputs.V(ox + radius / 2, oy, oz);
                range = group == 6 ? 0 : travel;
                direction = A2SpatialInputs.V(speed, -speed, speed);
            }
            if (group == 7)
            {
                size = 2048 * scale;
                origin = A2SpatialInputs.V(-world, 0, -world);
                center = A2SpatialInputs.V(-world / 2, 0, -world + radius);
                range = world;
            }
            if (group == 9)
                range = travel;
            if (group == 10)
            {
                long normal = 1 + (long)(random.Next() % (ulong)(scale / 10));
                speed = 1 + (long)(random.Next() % (ulong)(world / 4));
                radius = 5 * normal;
                direction = A2SpatialInputs.V(3 * speed, 0, 4 * speed);
                center = A2SpatialInputs.V(ox + 3 * travel + 4 * normal, oy, oz + 4 * travel - 3 * normal);
                range = 5 * travel;
            }
            if (group == 11)
            {
                size = world / 4;
                radius = world / 4;
                spine = world - radius;
                origin = A2SpatialInputs.V(-world / 2, -world / 2, -world / 2);
                center = A2SpatialInputs.V(-world / 4, -world / 2, -world / 2);
                direction = A2SpatialInputs.V(world, world - 1, -world);
                range = world;
            }
            long height = 2 * (radius + spine) + (group == 11 ? 0 : index & 1);
            body = new SpatialBody(new EntityId(0, 1), center, new Collider(SimFix.FromRaw(radius), SimFix.FromRaw(height)));
            ray = new SpatialRequest(SpatialQueryKind.Ray, origin, default, direction, SimFix.FromRaw(range), default, 0);
        }

        [Test]
        public void BoundaryBiasedRandomRaysRetainFrozenOracleHitAndRawDistance()
        {
            var generator = new A2SpatialInputs(0xD1A2F002);
            var scene = new Scene(32768 * SimFix.Scale);
            int hits = 0;
            for (int i = 0; i < 6000; i++)
            {
                A2SpatialInputs.Sample sample = generator.Random(SpatialQueryKind.Ray, i);
                bool expected = OracleSpatialGeometry.Intersects(sample.Body, sample.Request, out SimFix distance);
                int found = scene.Query(in sample.Body, in sample.Request);
                Assert.That(found, Is.EqualTo(expected ? 1 : 0), "Ray " + i);
                if (expected)
                {
                    hits++;
                    Assert.That(scene.Output[0].Distance.Raw, Is.EqualTo(distance.Raw), "Ray " + i);
                }
            }
            Assert.That(hits, Is.GreaterThan(0));
            Assert.That(hits, Is.LessThan(6000));
            TestContext.WriteLine("D1 frozen-geometry differential: seed=0xD1A2F002; rays=6000; hits={0}; local/world/edge/tangent+-1; zero ordered id/raw-distance mismatches.", hits);
        }
    }
}
