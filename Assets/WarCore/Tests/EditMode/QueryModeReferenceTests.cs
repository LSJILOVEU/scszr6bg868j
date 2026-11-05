using System;
using NUnit.Framework;
using WarCore.Sim;
using WarCore.Tests.Oracle;

namespace WarCore.Tests
{
    public sealed class QueryModeReferenceTests
    {
        [TestCase(SpatialQueryKind.Sphere)]
        [TestCase(SpatialQueryKind.Cylinder)]
        [TestCase(SpatialQueryKind.Sector)]
        [TestCase(SpatialQueryKind.Capsule)]
        [TestCase(SpatialQueryKind.Ray)]
        public void DefaultAndCustomReferenceOnlyChangeRangeOrdering(SpatialQueryKind kind)
        {
            var fixture = new QueryModeFixture();
            EntityId far = fixture.Add(SimVec3.Metres(12, 0, 0), SimFix.Scale, 4 * SimFix.Scale);
            EntityId near = fixture.Add(SimVec3.Metres(2, 0, 0), SimFix.Scale, 4 * SimFix.Scale);
            var request = QueryModeFixture.Request(kind);
            var output = new SpatialHit[2];
            fixture.Grid.Nearest(in request, out SpatialHit first);
            Assert.That(first.Entity, Is.EqualTo(near));
            fixture.Grid.Nearest(in request, request.Origin, out SpatialHit explicitDefault);
            QueryModeFixture.Same(first, explicitDefault);
            SpatialQueryResult initial = fixture.Grid.AllCapped(in request, 2, output);
            QueryModeFixture.Same(output[0], first);
            fixture.Check(request, request.Origin, 2, 2);
            SimVec3 reference = SimVec3.Metres(16384, 0, 0);
            fixture.Check(request, reference, 1, 2);
            fixture.Grid.Nearest(in request, reference, out SpatialHit custom);
            Assert.That(custom.Entity, Is.EqualTo(kind == SpatialQueryKind.Ray ? near : far));
            SpatialQueryResult changed = fixture.Grid.AllCapped(in request, reference, 2, output);
            Assert.That(changed.TotalHits, Is.EqualTo(initial.TotalHits));
            QueryModeFixture.Same(output[0], custom);
        }

        [TestCase(0, 0, 0)]
        [TestCase(0, 3, 0)]
        [TestCase(0, -3, 0)]
        [TestCase(0, 4, 1)]
        [TestCase(0, -4, 1)]
        [TestCase(3, 2, 2)]
        public void CapsuleReferenceDistanceUsesSpineGapAndClampsInsideToZero(int x, int y, long expected)
        {
            var fixture = new QueryModeFixture();
            fixture.Add(default, SimFix.Scale, 6 * SimFix.Scale);
            var request = SpatialQueries.Sphere(default, SimFix.FromInt(4));
            SimVec3 reference = SimVec3.Metres(x, y, 0);
            fixture.Check(request, reference, 1, 1);
            fixture.Grid.Nearest(in request, reference, out SpatialHit hit);
            Assert.That(hit.Distance.Raw, Is.EqualTo(expected * SimFix.Scale));
        }

        [Test]
        public void ReferenceDistanceUses128BitSumAtOppositeWorldCornersAndFloorSqrt()
        {
            const long limit = 1L << 30;
            SimVec3 low = SimVec3.Metres(-16384, -16384, -16384);
            SimVec3 high = SimVec3.Metres(16384, 16384, 16384);
            var fixture = new QueryModeFixture(8 * SimFix.Scale);
            fixture.Add(low);
            var request = SpatialQueries.Sphere(low, default);
            fixture.Check(request, high, 1, 1);
            fixture.Grid.Nearest(in request, high, out SpatialHit hit);
            Assert.That(hit.Distance.Raw, Is.EqualTo(QueryModeOracle.Distance(low, 0, 0, high)));
            Assert.That(hit.Distance.Raw, Is.GreaterThan(3 * limit));
            Assert.That(hit.Distance.Raw, Is.LessThan(1L << 32));
            long floor = hit.Distance.Raw;
            var squared = (System.Numerics.BigInteger)12 * limit * limit;
            Assert.That((System.Numerics.BigInteger)floor * floor <= squared, Is.True);
            Assert.That((System.Numerics.BigInteger)(floor + 1) * (floor + 1) > squared, Is.True);
        }

        [Test]
        public void NegativeSpineHalfLengthDegeneratesToSphereWithoutChangingColliderValidation()
        {
            var point = SimVec3.Metres(0, 3, 0);
            Assert.That(SpatialModeMath.ReferenceDistance(default, SimFix.Scale, SimFix.Scale, point).Raw,
                Is.EqualTo(2 * SimFix.Scale));
            Assert.That(SpatialModeMath.ReferenceDistance(default, SimFix.Scale, SimFix.Scale, default).Raw, Is.Zero);
            Assert.Throws<ArgumentOutOfRangeException>(() => new Collider(SimFix.FromInt(1), SimFix.FromInt(1)));
        }

        [Test]
        public void BoundaryBiasedReferenceArithmeticMatchesIndependentBigInteger()
        {
            const long limit = 1L << 30;
            var random = new Random(0x3B401);
            long[] edges = { -limit, -limit + 1, -1, 0, 1, limit - 1, limit };
            for (int i = 0; i < 20000; i++)
            {
                var center = new SimVec3(Raw(random, edges), Raw(random, edges), Raw(random, edges));
                var point = new SimVec3(Raw(random, edges), Raw(random, edges), Raw(random, edges));
                long radius = random.Next(0, (int)limit + 1);
                long height = i % 3 == 0 ? radius : 2 * radius + random.Next(0, (int)(limit - radius + 1));
                long expected = QueryModeOracle.Distance(center, radius, height, point);
                Assert.That(SpatialModeMath.ReferenceDistance(center, radius, height, point).Raw, Is.EqualTo(expected));
            }
            TestContext.WriteLine("Reference distance: seed=0x3B401; 20000 boundary-biased BigInteger cases including negative spine; opposite-corner sum=12*2^60 (>long.MaxValue); mismatches=0.");
        }

        private static SimFix Raw(Random random, long[] edges)
            => SimFix.FromRaw(random.Next(2) == 0 ? edges[random.Next(edges.Length)] : random.Next(-(1 << 30), 1 << 30));
    }
}
