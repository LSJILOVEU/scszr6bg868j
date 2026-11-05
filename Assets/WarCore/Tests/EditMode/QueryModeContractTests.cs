using System;
using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class QueryModeContractTests
    {
        [TestCase(SpatialQueryKind.Sphere)]
        [TestCase(SpatialQueryKind.Cylinder)]
        [TestCase(SpatialQueryKind.Sector)]
        [TestCase(SpatialQueryKind.Capsule)]
        [TestCase(SpatialQueryKind.Ray)]
        public void AllCappedCountsEveryUniqueHitAndKeepsSortedPrefixForEveryCapAndBuffer(SpatialQueryKind kind)
        {
            var fixture = new QueryModeFixture();
            for (int i = 0; i < 620; i++)
                fixture.Add(new SimVec3(SimFix.FromRaw(3 * SimFix.Scale + (620 - i) * 17), default, default),
                    SimFix.Scale / 2, 2 * SimFix.Scale, i % 2 == 0 ? 1u : 0x80000000u);
            var request = QueryModeFixture.Request(kind);
            int[] caps = { 0, 1, 511, 512, 513, 620, 900, int.MaxValue };
            int[] lengths = { 0, 1, 7, 513, 700 };
            var expected = fixture.Oracle(request, request.Origin);
            Assert.That(expected.Count, Is.EqualTo(620));
            foreach (int k in caps)
                foreach (int length in lengths)
                {
                    var output = new SpatialHit[length + 2];
                    var marker = new SpatialHit(new EntityId(999, 9), SimFix.FromRaw(123));
                    Array.Fill(output, marker);
                    SpatialQueryResult result = fixture.Grid.AllCapped(in request, k, output.AsSpan(1, length));
                    int written = Math.Min(620, Math.Min(k, length));
                    Assert.That(result.Written, Is.EqualTo(written));
                    Assert.That(result.TotalHits, Is.EqualTo(620));
                    Assert.That(result.Truncated, Is.EqualTo(620 > written));
                    Assert.That(fixture.Grid.LastTruncatedHits, Is.EqualTo(620 - written));
                    for (int i = 0; i < written; i++)
                        QueryModeFixture.Same(output[1 + i], expected[i]);
                    QueryModeFixture.Same(output[0], marker);
                    for (int i = written + 1; i < output.Length; i++)
                        QueryModeFixture.Same(output[i], marker);
                    Assert.That(fixture.Grid.LastCandidateCount, Is.EqualTo(620));
                }
            Assert.That(fixture.Counters.DuplicateSkips, Is.GreaterThan(0));
            TestContext.WriteLine("Mode caps {0}: 620 exact hits; K=0/1/511/512/513/620/900/int.MaxValue; lengths=0/1/7/513/700; unique sorted prefixes and untouched tails verified.", kind);
        }

        [TestCase(SpatialQueryKind.Sphere)]
        [TestCase(SpatialQueryKind.Cylinder)]
        [TestCase(SpatialQueryKind.Sector)]
        [TestCase(SpatialQueryKind.Capsule)]
        [TestCase(SpatialQueryKind.Ray)]
        public void AnyExitsTraversalAtFirstPreciseHitAndZeroMaskDoesNoWork(SpatialQueryKind kind)
        {
            var fixture = new QueryModeFixture();
            for (int i = 0; i < 32; i++)
                fixture.Add(SimVec3.Metres(i % 15 + 1, 0, 0), SimFix.Scale / 4, SimFix.Scale);
            SpatialRequest request = QueryModeFixture.Request(kind);
            fixture.Grid.AllCapped(in request, 0, Span<SpatialHit>.Empty);
            long allWalks = fixture.Counters.WalkCalls;
            long allMembers = fixture.Counters.LeafMembers;
            Assert.That(fixture.Grid.Any(in request), Is.True);
            Assert.That(fixture.Grid.LastNarrowPhaseCount, Is.EqualTo(1));
            Assert.That(fixture.Grid.LastCandidateCount, Is.EqualTo(1));
            Assert.That(fixture.Counters.WalkCalls, Is.LessThan(allWalks));
            Assert.That(fixture.Counters.LeafMembers, Is.LessThan(allMembers));
            TestContext.WriteLine("Any {0}: walks={1}/{2}; leaf members={3}/{4}; precise=1 (Any/full).", kind,
                fixture.Counters.WalkCalls, allWalks, fixture.Counters.LeafMembers, allMembers);
            var zero = QueryModeFixture.Request(kind, 0);
            fixture.Check(zero, default, 512, 1);
            Assert.That(fixture.Grid.LastVisitedCells, Is.Zero);
            Assert.That(fixture.Counters.WalkCalls, Is.Zero);
            Assert.That(fixture.Counters.LeafMembers, Is.Zero);
            var absent = QueryModeFixture.Request(kind, 2);
            fixture.Check(absent, default, 512, 1);
            Assert.That(fixture.Grid.LastCandidateCount, Is.Zero);
            var empty = new QueryModeFixture();
            empty.Check(request, default, 512, 1);
        }

        [TestCase(SpatialQueryKind.Sphere)]
        [TestCase(SpatialQueryKind.Cylinder)]
        [TestCase(SpatialQueryKind.Sector)]
        [TestCase(SpatialQueryKind.Capsule)]
        [TestCase(SpatialQueryKind.Ray)]
        public void DeathReuseLayerChangesAndStampWrapCannotLeakOrDuplicateHits(SpatialQueryKind kind)
        {
            var fixture = new QueryModeFixture();
            EntityId dead = fixture.Add(SimVec3.Metres(2, 0, 0), 2 * SimFix.Scale, 6 * SimFix.Scale);
            EntityId alive = fixture.Add(SimVec3.Metres(5, 0, 0), SimFix.Scale, 4 * SimFix.Scale);
            fixture.Store.Destroy(dead);
            int membershipCount = fixture.Grid.MembershipCount;
            SpatialRequest request = QueryModeFixture.Request(kind);
            fixture.Check(request, default, 512, 10);
            Assert.That(fixture.Grid.MembershipCount, Is.EqualTo(membershipCount));
            fixture.Store.RecycleDestroyed();
            EntityId reused = fixture.Store.Create();
            Assert.That(reused.Id, Is.EqualTo(dead.Id));
            fixture.Check(request, default, 512, 10);
            fixture.Set(reused, SimVec3.Metres(3, 0, 0), SimFix.Scale, 4 * SimFix.Scale, 1);
            fixture.Store.Get(fixture.Colliders, alive) = fixture.Store.Get(fixture.Colliders, alive).WithLayer(0);
            typeof(GridQueryContext).GetField("stamp", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .SetValue(fixture.Grid.QueryContext, uint.MaxValue);
            fixture.Check(request, default, 1, 10);
            fixture.Store.Get(fixture.Colliders, reused) = fixture.Store.Get(fixture.Colliders, reused).WithLayer(0x80000000u);
            var high = QueryModeFixture.Request(kind, 0x80000000u);
            fixture.Check(high, default, 1, 10);
        }

        [Test]
        public void InvalidInputsAreExplicitlyRejectedBeforeArithmeticOrTraversal()
        {
            var fixture = new QueryModeFixture();
            var request = SpatialQueries.Sphere(default, default, 0);
            Assert.Throws<ArgumentOutOfRangeException>(() => fixture.Grid.AllCapped(in request, -1, Array.Empty<SpatialHit>()));
            Assert.That(fixture.Counters.WalkCalls, Is.Zero);
            var outside = new SimVec3(SimFix.FromRaw(long.MinValue), default, default);
            Assert.Throws<ArgumentOutOfRangeException>(() => SpatialQueries.Capsule(outside, default, default));
            Assert.Throws<ArgumentOutOfRangeException>(() => SpatialQueries.Capsule(default, outside, default));
            Assert.Throws<ArgumentOutOfRangeException>(() => fixture.Grid.Nearest(in request, outside, out _));
            Assert.Throws<ArgumentOutOfRangeException>(() => fixture.Grid.AllCapped(in request, outside, 0, Array.Empty<SpatialHit>()));
            Assert.Throws<ArgumentOutOfRangeException>(() => SpatialQueries.Sphere(default, SimFix.FromRaw(-1)));
            Assert.Throws<ArgumentOutOfRangeException>(() => SpatialQueries.Cylinder(default, default, SimFix.FromRaw(-1)));
            Assert.Throws<ArgumentOutOfRangeException>(() => SpatialQueries.Ray(default, SimVec3.Metres(1, 0, 0), SimFix.FromInt(16385)));
            Assert.Throws<ArgumentException>(() => SpatialQueries.Ray(default, default, default));
            Assert.Throws<ArgumentException>(() => SpatialQueries.Sector(default, default, default, 0, default));
            Assert.Throws<ArgumentException>(() => SpatialQueries.Sector(default, default, SimVec3.Metres(1, 1, 0), 0, default));
            Assert.Throws<ArgumentOutOfRangeException>(() => SpatialQueries.Sector(default, default, SimVec3.Metres(1, 0, 0), 1001, default));
            Assert.Throws<ArgumentOutOfRangeException>(() => SpatialQueries.Capsule(SimVec3.Metres(-16384, 0, 0), SimVec3.Metres(1, 0, 0), default));
        }
    }
}
