using System;
using System.Numerics;
using NUnit.Framework;
using WarCore.Sim;
using WarCore.Tests.Oracle;

namespace WarCore.Tests
{
    public sealed class QueryModeNearestTests
    {
        private static SimVec3 Raw(long x, long y, long z) => new SimVec3(SimFix.FromRaw(x), SimFix.FromRaw(y), SimFix.FromRaw(z));

        [Test]
        public void SameRawBucketUsesIdEvenWhenTrueDistancesDiffer()
        {
            var fixture = new QueryModeFixture(4);
            EntityId farther = fixture.Add(Raw(10, 0, 1), 5, 10);
            fixture.Add(Raw(10, 0, 0), 5, 10);
            for (int kind = 0; kind < 5; kind++)
            {
                var request = new SpatialRequest((SpatialQueryKind)kind, default, SimFix.FromRaw(kind == 4 ? 0 : 40),
                    Raw(kind == 3 ? 30 : 1, 0, 0), SimFix.FromRaw(40), SimFix.FromRaw(40), 600);
                fixture.Check(request, default, 1, 2);
                var hits = new SpatialHit[2];
                fixture.Grid.AllCapped(in request, 2, hits);
                Assert.That(hits[0].Entity, Is.EqualTo(farther));
                Assert.That(hits[0].Distance.Raw, Is.EqualTo(5));
                Assert.That(hits[1].Distance.Raw, Is.EqualTo(5));
            }
        }

        [Test]
        public void SameDdaEventMustVisitAllNeighboursInTheWinningRawBucket()
        {
            var fixture = new QueryModeFixture(10);
            EntityId expected = fixture.Add(Raw(10, 10, 0));
            fixture.Add(Raw(10, 9, 0), 0, 2);
            fixture.Add(Raw(20, 20, 0));
            var request = SpatialQueries.Ray(default, Raw(1, 1, 0), SimFix.FromRaw(40));
            fixture.Check(request, default, 3, 3);
            Assert.That(fixture.Grid.Nearest(in request, out SpatialHit hit), Is.True);
            Assert.That(hit.Entity, Is.EqualTo(expected));
            Assert.That(hit.Distance.Raw, Is.EqualTo(14));
            Assert.That(fixture.Grid.LastCandidateCount, Is.EqualTo(2));
        }

        [Test]
        public void DistinctDdaEventsInOneRawBucketCannotStopAtTheFirstHit()
        {
            var fixture = new QueryModeFixture(1);
            EntityId expected = fixture.Add(Raw(2, 2, 0), 1, 2);
            fixture.Add(Raw(1, 0, 0), 0, 2);
            var request = SpatialQueries.Ray(default, Raw(8, 7, 0), SimFix.FromRaw(5));
            fixture.Check(request, default, 2, 2);
            Assert.That(fixture.Grid.Nearest(in request, out SpatialHit hit), Is.True);
            Assert.That(hit.Entity, Is.EqualTo(expected));
            Assert.That(hit.Distance.Raw, Is.EqualTo(1));
            Assert.That(fixture.Grid.LastCandidateCount, Is.EqualTo(2));
        }

        [Test]
        public void LargeBodyInAnEarlyCellDoesNotHideANearerHitInALaterCell()
        {
            var fixture = new QueryModeFixture();
            EntityId expected = fixture.Add(SimVec3.Metres(5, 0, 0));
            fixture.Add(Raw(8 * SimFix.Scale, 0, 59 * SimFix.Scale / 10), 6 * SimFix.Scale, 12 * SimFix.Scale);
            var request = SpatialQueries.Ray(default, SimVec3.Metres(1, 0, 0), SimFix.FromInt(16));
            Assert.That(fixture.Grid.Any(in request), Is.True);
            Assert.That(fixture.Grid.LastCandidateCount, Is.EqualTo(1));
            fixture.Check(request, default, 2, 2);
            fixture.Grid.Nearest(in request, out SpatialHit hit);
            Assert.That(hit.Entity, Is.EqualTo(expected));
            Assert.That(hit.Distance.Raw, Is.EqualTo(5 * SimFix.Scale));
            Assert.That(fixture.Grid.LastCandidateCount, Is.EqualTo(2));
        }

        [Test]
        public void LargeBodySurfaceAndOriginInsideWinBeforeTheirCenters()
        {
            var fixture = new QueryModeFixture();
            EntityId expected = fixture.Add(SimVec3.Metres(8, 0, 0), 7 * SimFix.Scale, 14 * SimFix.Scale);
            fixture.Add(SimVec3.Metres(2, 0, 0));
            var request = SpatialQueries.Ray(default, SimVec3.Metres(1, 0, 0), SimFix.FromInt(16));
            fixture.Check(request, default, 2, 2);
            fixture.Grid.Nearest(in request, out SpatialHit hit);
            Assert.That(hit.Entity, Is.EqualTo(expected));
            Assert.That(hit.Distance.Raw, Is.EqualTo(SimFix.Scale));
            var inside = SpatialQueries.Ray(SimVec3.Metres(2, 0, 0), SimVec3.Metres(1, 0, 0), default);
            fixture.Check(inside, default, 2, 2);
            fixture.Grid.Nearest(in inside, out hit);
            Assert.That(hit.Entity, Is.EqualTo(expected));
            Assert.That(hit.Distance.Raw, Is.Zero);
        }

        [Test]
        public void NearestStopsDdaOnlyAfterTheEntireWinningBucket()
        {
            var fixture = new QueryModeFixture();
            for (int i = 0; i < 32; i++)
                fixture.Add(SimVec3.Metres(1 + i * 3, 0, 0));
            var request = SpatialQueries.Ray(default, SimVec3.Metres(1, 0, 0), SimFix.FromInt(100));
            var all = new SpatialHit[32];
            fixture.Grid.AllCapped(in request, 32, all);
            long allWalks = fixture.Counters.WalkCalls;
            int allCandidates = fixture.Grid.LastCandidateCount;
            fixture.Grid.Nearest(in request, out SpatialHit hit);
            QueryModeFixture.Same(hit, all[0]);
            Assert.That(fixture.Counters.WalkCalls, Is.LessThan(allWalks));
            Assert.That(fixture.Grid.LastCandidateCount, Is.EqualTo(1));
            TestContext.WriteLine("Nearest DDA: walks={0}/{1}; candidates={2}/{3} (nearest/full); same-event and same-raw later-event adversaries also verified.",
                fixture.Counters.WalkCalls, allWalks, fixture.Grid.LastCandidateCount, allCandidates);
        }

        [Test]
        public void LowerBoundCrossProductsMatchBigIntegerAtEdgesAndAcrossTwentyThousandSamples()
        {
            const long limit = 1L << 30;
            var random = new Random(0x3B128);
            long[] edges = { 0, 1, 2, limit / 2, limit - 1, limit };
            int cases = 0;
            foreach (long n in edges)
                foreach (long d in edges)
                    foreach (long best in edges)
                    {
                        if (d == 0)
                            continue;
                        CheckBound((ulong)n, (ulong)d, 3UL * (ulong)limit * (ulong)limit, best, limit);
                        cases++;
                    }
            for (int i = 0; i < 20000; i++)
            {
                long d = random.Next(1, (int)limit + 1);
                long n = random.Next(0, (int)limit + 1);
                long best = random.Next(0, (int)limit + 1);
                ulong direction2 = (ulong)(d * d) + (ulong)random.Next(0, (int)limit) * (ulong)limit;
                CheckBound((ulong)n, (ulong)d, direction2, best, limit);
                cases++;
            }
            CheckBound(5, 1, 1, 4, limit);
            CheckBound(5, 1, 1, 5, limit);
            TestContext.WriteLine("Nearest bound: seed=0x3B128; BigInteger comparisons={0}; equality at best+1 stops; same bucket continues; mismatches=0.", cases + 2);
        }

        private static void CheckBound(ulong n, ulong d, ulong direction2, long best, long range)
        {
            BigInteger squared = (BigInteger)n * n * direction2 / ((BigInteger)d * d);
            bool expected = best < range && QueryModeOracle.Sqrt(squared) > best;
            var next = new RayCellEvent(0, 0, 0, n, d);
            Assert.That(SpatialModeMath.BeyondBest(in next, direction2, best, range), Is.EqualTo(expected));
        }
    }
}
