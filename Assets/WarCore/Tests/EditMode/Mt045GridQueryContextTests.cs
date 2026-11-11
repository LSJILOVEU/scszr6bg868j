using System;
using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class Mt045GridQueryContextTests
    {
        [TestCase(1)]
        [TestCase(16)]
        public void ConstructorAllocatesSortKeysForEverySlot(int capacity)
        {
            var context = new GridQueryContext(capacity);
            var keys = (ulong[])GridIndexRegressionAudit.Field(context, "SortKeys");
            Assert.That(keys.Length, Is.EqualTo(capacity));
        }

        [Test]
        public void AnyClearsTruncatedHitsAfterCappedQuery()
        {
            var fixture = new QueryModeFixture(capacity: 2);
            fixture.Add(default);
            fixture.Add(SimVec3.Metres(1, 0, 0));
            SpatialRequest request = SpatialQueries.Sphere(default, SimFix.FromInt(2));

            Assert.That(fixture.Grid.AllCapped(in request, 0, Array.Empty<SpatialHit>()).TotalHits, Is.EqualTo(2));
            Assert.That(fixture.Grid.QueryContext.TruncatedHits, Is.EqualTo(2));
            Assert.That(fixture.Grid.Any(in request), Is.True);
            Assert.That(fixture.Grid.QueryContext.TruncatedHits, Is.EqualTo(0));
        }
    }
}
