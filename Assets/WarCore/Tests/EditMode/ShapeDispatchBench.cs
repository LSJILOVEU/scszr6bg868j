using System;
using System.Diagnostics;
using System.Globalization;
using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    // Block 3a before/after report for the all-capsule path. Uses only pre-3a APIs so the same file
    // runs on the base commit. Report only: no timing assertions.
    [Category("M0c3a")]
    [Category("Manual")]
    [Explicit("Report-only timing; run by name on a quiet machine.")]
    public sealed class ShapeDispatchBench
    {
        private const int Bodies = 4000;
        private const int Requests = 512;
        private const int Warmup = 8;
        private const int Batches = 61;
        private static long sink;

        private sealed class Scene
        {
            internal readonly EntityStore Store = new EntityStore(Bodies + 1);
            internal readonly ComponentType<Position> Positions;
            internal readonly ComponentType<Collider> Colliders;
            internal readonly GridSpatialQuery Grid;
            internal readonly EntityId[] Entities = new EntityId[Bodies];

            internal Scene()
            {
                Positions = Store.Register<Position>("position");
                Colliders = Store.Register<Collider>("collider");
                Grid = new GridSpatialQuery(Store, Positions, Colliders, SimFix.FromInt(4), Bodies + 1, 1 << 16);
                var random = new Random(0x3A0B3E);
                for (int i = 0; i < Bodies; i++)
                {
                    EntityId entity = Store.Create();
                    long radius = (1 + random.Next(4)) * SimFix.Scale / 4;
                    Store.Set(Positions, entity, new Position(new SimVec3(SimFix.FromRaw(random.Next(-200, 201) * SimFix.Scale + random.Next(65536)),
                        SimFix.FromRaw(random.Next(-2, 3) * SimFix.Scale), SimFix.FromRaw(random.Next(-200, 201) * SimFix.Scale + random.Next(65536)))));
                    Store.Set(Colliders, entity, new Collider(SimFix.FromRaw(radius), SimFix.FromRaw(radius * 2 + random.Next(2) * SimFix.Scale), 1u << random.Next(3)));
                    Grid.Upsert(entity);
                    Entities[i] = entity;
                }
                Grid.Synchronize();
            }
        }

        private static SpatialRequest[] RequestsOf(SpatialQueryKind kind, int seed)
        {
            var random = new Random(seed);
            var result = new SpatialRequest[Requests];
            for (int i = 0; i < result.Length; i++)
            {
                SimVec3 origin = SimVec3.Metres(random.Next(-200, 201), random.Next(-1, 2), random.Next(-200, 201));
                SimVec3 direction = SimVec3.Metres(random.Next(-8, 9), 0, random.Next(-8, 9));
                if (direction.X.Raw == 0 && direction.Z.Raw == 0)
                    direction = SimVec3.Metres(1, 0, 0);
                switch (kind)
                {
                    case SpatialQueryKind.Sphere:
                        result[i] = SpatialQueries.Sphere(origin, SimFix.FromInt(6));
                        break;
                    case SpatialQueryKind.Sector:
                        result[i] = SpatialQueries.Sector(origin, SimFix.FromInt(12), direction, 500, SimFix.FromInt(3));
                        break;
                    default:
                        result[i] = SpatialQueries.Ray(origin, direction + SimVec3.Metres(0, random.Next(-1, 2), 0), SimFix.FromInt(48));
                        break;
                }
            }
            return result;
        }

        private static void Report(string name, Func<long> batch, int operations)
        {
            for (int i = 0; i < Warmup; i++)
                sink = unchecked(sink + batch());
            var elapsed = new long[Batches];
            for (int i = 0; i < Batches; i++)
            {
                long start = Stopwatch.GetTimestamp();
                sink = unchecked(sink + batch());
                elapsed[i] = Stopwatch.GetTimestamp() - start;
            }
            Array.Sort(elapsed);
            double scale = 1000000000.0 / Stopwatch.Frequency / operations;
            TestContext.WriteLine("3a-bench {0}: ops/batch={1}; batches={2}; median={3} ns/op; p95={4} ns/op", name, operations, Batches,
                (elapsed[Batches / 2] * scale).ToString("F1", CultureInfo.InvariantCulture),
                (elapsed[(Batches * 95 - 1) / 100] * scale).ToString("F1", CultureInfo.InvariantCulture));
        }

        [Test]
        public void AllCapsuleQueriesAndTickEndSynchronize()
        {
            var scene = new Scene();
            GridSpatialQuery grid = scene.Grid;
            var hits = new SpatialHit[16];
            SpatialRequest[] rays = RequestsOf(SpatialQueryKind.Ray, 1);
            SpatialRequest[] spheres = RequestsOf(SpatialQueryKind.Sphere, 2);
            SpatialRequest[] sectors = RequestsOf(SpatialQueryKind.Sector, 3);
            TestContext.WriteLine("3a-bench scene: capsules={0}; area=400x400 m; cell=4 m; requests/kind={1}; runtime={2}; Stopwatch.Frequency={3}",
                Bodies, Requests, System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription, Stopwatch.Frequency);
            Report("ray.any", () =>
            {
                long count = 0;
                for (int i = 0; i < rays.Length; i++)
                    count += grid.Any(in rays[i]) ? 1 : 0;
                return count;
            }, Requests);
            Report("ray.nearest", () =>
            {
                long count = 0;
                for (int i = 0; i < rays.Length; i++)
                    count += grid.Nearest(in rays[i], out SpatialHit hit) ? hit.Distance.Raw : 0;
                return count;
            }, Requests);
            Report("ray.all16", () =>
            {
                long count = 0;
                for (int i = 0; i < rays.Length; i++)
                    count += grid.AllCapped(in rays[i], 16, hits).TotalHits;
                return count;
            }, Requests);
            Report("sphere.all16", () =>
            {
                long count = 0;
                for (int i = 0; i < spheres.Length; i++)
                    count += grid.AllCapped(in spheres[i], 16, hits).TotalHits;
                return count;
            }, Requests);
            Report("sector.all16", () =>
            {
                long count = 0;
                for (int i = 0; i < sectors.Length; i++)
                    count += grid.AllCapped(in sectors[i], 16, hits).TotalHits;
                return count;
            }, Requests);
            // One layer change per tick forces the copy path: every live body is rebuilt with WithLayer/WithStatic.
            int tick = 0;
            Report("tick-end.synchronize", () =>
            {
                for (int i = 0; i < 8; i++)
                {
                    EntityId entity = scene.Entities[tick++ % Bodies];
                    Collider current = scene.Store.Get(scene.Colliders, entity);
                    scene.Store.Get(scene.Colliders, entity) = current.WithLayer(current.Layer ^ 8u);
                    grid.Synchronize();
                }
                return tick;
            }, 8);
            TestContext.WriteLine("3a-bench checksum={0}", sink);
        }
    }
}
