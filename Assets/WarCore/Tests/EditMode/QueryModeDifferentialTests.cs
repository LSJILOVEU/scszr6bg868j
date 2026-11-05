using System;
using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class QueryModeDifferentialTests
    {
        [TestCase(0x3B2001)]
        [TestCase(0x3B2002)]
        public void AllModesAndShapesMatchIndependentOracleAcrossTwoHundredChangingSteps(int seed)
        {
            var random = new Random(seed);
            var fixture = new QueryModeFixture();
            for (int i = 0; i < 72; i++)
                fixture.Add(Point(random, i), Radius(i), 2 * Radius(i) + (i % 4) * SimFix.Scale + 1, Layer(i));
            int comparisons = 0;
            for (int step = 0; step < 200; step++)
            {
                int slot = step % fixture.Bodies.Count;
                EntityId entity = fixture.Bodies[slot].Entity;
                if (step % 17 == 0)
                {
                    fixture.Store.Destroy(entity);
                    SpatialRequest deadCheck = SpatialQueries.Sphere(fixture.Bodies[slot].Position, SimFix.FromInt(12));
                    fixture.Check(deadCheck, default, 7, 3);
                    fixture.Store.RecycleDestroyed();
                    EntityId replacement = fixture.Store.Create();
                    Assert.That(replacement.Id, Is.EqualTo(slot));
                    entity = replacement;
                }
                fixture.Set(entity, Point(random, step), Radius(step), 2 * Radius(step) + (step % 4) * SimFix.Scale + 1, Layer(step));
                int layerSlot = (step + 13) % fixture.Bodies.Count;
                EntityId layerEntity = fixture.Bodies[layerSlot].Entity;
                fixture.Store.Get(fixture.Colliders, layerEntity) = fixture.Store.Get(fixture.Colliders, layerEntity).WithLayer(Layer(step + 7));
                SimVec3 origin = Point(random, step);
                SimVec3 custom = step % 2 == 0 ? SimVec3.Metres(16384, -16384, 16384) : Point(random, step + 1);
                uint mask = step % 7 == 0 ? 0 : step % 3 == 0 ? uint.MaxValue : Layer(step + 1) | Layer(step + 2);
                for (int kind = 0; kind < 5; kind++)
                {
                    SpatialRequest request = Request((SpatialQueryKind)kind, origin, step, mask);
                    fixture.Check(request, request.Origin, step % 19, step % 11);
                    fixture.Check(request, custom, step % 19, step % 11);
                    comparisons += 2;
                }
            }
            TestContext.WriteLine("Mode differential: seed=0x{0:X}; steps=200; bodies=72; shapes=5; mode sets={1}; large bodies, cell edges, world corners, layer-only changes, death/reuse; mismatches=0.", seed, comparisons);
        }

        private static long Radius(int i) => i % 9 == 0 ? 4 * SimFix.Scale : i % 3 == 0 ? 0 : SimFix.Scale / 4;
        private static uint Layer(int i) => i % 5 == 0 ? 0 : i % 5 == 1 ? 0x80000000u : i % 5 == 2 ? 1u : i % 5 == 3 ? 3u : uint.MaxValue;

        private static SimVec3 Point(Random random, int i)
        {
            long center = i % 3 == 0 ? -16374 : i % 3 == 1 ? 16374 : 0;
            long x = center * SimFix.Scale + random.Next(-3, 4) * 3L * SimFix.Scale + random.Next(-1, 2);
            long y = center * SimFix.Scale + random.Next(-3, 4) * 3L * SimFix.Scale + random.Next(-1, 2);
            long z = center * SimFix.Scale + random.Next(-3, 4) * 3L * SimFix.Scale + random.Next(-1, 2);
            return new SimVec3(SimFix.FromRaw(x), SimFix.FromRaw(y), SimFix.FromRaw(z));
        }

        private static SpatialRequest Request(SpatialQueryKind kind, SimVec3 origin, int step, uint mask)
        {
            SimFix radius = SimFix.FromInt(step % 4 == 0 ? 0 : 16);
            SimVec3 direction = SimVec3.Metres(step % 2 == 0 ? 3 : -3, 0, step % 3 - 1);
            switch (kind)
            {
                case SpatialQueryKind.Sphere:
                    return SpatialQueries.Sphere(origin, radius, mask);
                case SpatialQueryKind.Cylinder:
                    return SpatialQueries.Cylinder(origin, radius, SimFix.FromInt(step % 9), mask);
                case SpatialQueryKind.Sector:
                    return SpatialQueries.Sector(origin, radius, direction, step % 3 == 0 ? -1000 : 600, SimFix.FromInt(step % 9), mask);
                case SpatialQueryKind.Capsule:
                    return SpatialQueries.Capsule(origin, Clamp(origin + direction), radius, mask);
                default:
                    return SpatialQueries.Ray(origin, new SimVec3(direction.X, SimFix.FromInt(step % 3 - 1), direction.Z), SimFix.FromInt(32), mask);
            }
        }

        private static SimVec3 Clamp(SimVec3 point)
        {
            const long limit = 1L << 30;
            return new SimVec3(SimFix.FromRaw(Math.Max(-limit, Math.Min(limit, point.X.Raw))),
                SimFix.FromRaw(Math.Max(-limit, Math.Min(limit, point.Y.Raw))),
                SimFix.FromRaw(Math.Max(-limit, Math.Min(limit, point.Z.Raw))));
        }

        [Test]
        public void AllSevenHundredThirtyThreeFrozenVectorsAlsoPassEveryNewMode()
        {
            var inputs = new A2SpatialInputs(0x3B733);
            VectorJson rows = VectorJson.Load("spatial-narrowphase-vectors.json")["cases"];
            Assert.That(rows.Count, Is.EqualTo(733));
            for (int i = 0; i < rows.Count; i++)
            {
                A2SpatialInputs.Sample sample = inputs.Vector(rows[i]);
                long size = Math.Max(8 * SimFix.Scale, sample.Body.Collider.Height.Raw);
                var fixture = new QueryModeFixture(size, 1, 64);
                fixture.Add(sample.Body.Position, sample.Body.Collider.Radius.Raw, sample.Body.Collider.Height.Raw);
                fixture.Check(sample.Request, sample.Request.Origin, 1, 1);
                Assert.That(fixture.Grid.Any(in sample.Request), Is.EqualTo(rows[i]["expectHit"].Boolean), sample.Source);
                if (sample.Request.Kind == SpatialQueryKind.Ray && rows[i]["expectHit"].Boolean)
                {
                    fixture.Grid.Nearest(in sample.Request, out SpatialHit hit);
                    Assert.That(hit.Distance.Raw, Is.EqualTo(rows[i]["expectDistanceRaw"].Integer), sample.Source);
                }
            }
            TestContext.WriteLine("New modes: all 733 frozen geometry vectors; Any/Nearest/AllCapped; independent full-sort oracle; frozen ray raw distances unchanged.");
        }

        [Test]
        public void WarmModesAllocateZeroBytesIncludingTruncationAndEmptyBuffers()
        {
            var windows = new AllocationWindow();
            do
            {
                var fixture = new QueryModeFixture();
                for (int i = 0; i < 32; i++)
                    fixture.Add(SimVec3.Metres(i % 15 + 1, 0, 0), SimFix.Scale / 2, 2 * SimFix.Scale, 0x80000001u);
                var requests = new SpatialRequest[5];
                for (int i = 0; i < 5; i++)
                    requests[i] = QueryModeFixture.Request((SpatialQueryKind)i, 0x80000000u);
                var output = new SpatialHit[17];
                var meter = new AllocationMeter();
                long checksum = 0;
                for (int i = 0; i < 64; i++)
                    checksum += RunWarm(fixture.Grid, requests, output);
                windows.Begin(meter);
                for (int i = 0; i < 256; i++)
                    checksum += RunWarm(fixture.Grid, requests, output);
                AllocationSample allocated = windows.End();
                Assert.That(checksum, Is.GreaterThan(0));
                if (!windows.NeedsRetry)
                    AllocationAssertions.Zero(allocated);
                TestContext.WriteLine("New modes warm: 256 rounds * 5 shapes * 7 mode/reference/buffer paths; {0}; checksum={1}.", allocated.Describe(), checksum);
            }
            while (windows.NeedsRetry);
        }

        private static long RunWarm(ISpatialQueryModes query, SpatialRequest[] requests, SpatialHit[] output)
        {
            long sum = 0;
            var reference = SimVec3.Metres(16, 4, -8);
            foreach (SpatialRequest request in requests)
            {
                if (query.Any(in request))
                    sum++;
                if (query.Nearest(in request, out SpatialHit nearest))
                    sum += nearest.Distance.Raw;
                if (query.Nearest(in request, reference, out nearest))
                    sum += nearest.Distance.Raw;
                sum += query.AllCapped(in request, 512, output).TotalHits;
                sum += query.AllCapped(in request, reference, 7, output).Written;
                sum += query.AllCapped(in request, 0, output).TotalHits;
                sum += query.AllCapped(in request, 512, Span<SpatialHit>.Empty).TotalHits;
            }
            return sum;
        }
    }
}
