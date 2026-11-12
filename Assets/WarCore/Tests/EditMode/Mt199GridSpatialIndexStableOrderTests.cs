using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    [TestFixture, Category("StableQueryOptimization"), Category("StableIdentity")]
    public sealed class Mt199GridSpatialIndexStableOrderTests
    {
        [Test]
        public void LegacyAuditAcceptsMissingStableOrder()
        {
            var fixture = new QueryModeFixture(capacity:3, memberships:16);
            Assert.DoesNotThrow(fixture.Grid.Index.ValidateStableQueryOrder);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void MixedPartitionAuditAcceptsUnmaintainedOrder(bool stationary)
        {
            var index = StableIndex(stationary, mixed:true);
            Assert.DoesNotThrow(() =>
            {
                index.Rebuild();
                index.ValidateStableQueryOrder();
            });
            Assert.That(index.LargeBodyCount, Is.EqualTo(2));
            var query = new GridSpatialQuery(index, new GridQueryContext(3));
            var hits = new SpatialHit[3];
            var result = query.AllCapped(SpatialQueries.Sphere(default, SimFix.FromInt(8)), 3, hits);
            Assert.That(result.TotalHits, Is.EqualTo(3));
            Assert.That(result.Written, Is.EqualTo(3));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void PureBypassAuditRejectsInvalidMember(bool stationary)
        {
            var index = StableIndex(stationary);
            index.Rebuild();
            Assert.DoesNotThrow(index.ValidateStableQueryOrder);
            var state = (SpatialGridState)GridIndexRegressionAudit.Field(index, "active");
            var partition = stationary ? state.Static : state.Unit;
            partition.OrderedLargeIds[0] = -1;
            var error = Assert.Throws<SimBoundaryException>(index.ValidateStableQueryOrder);
            Assert.That(error.Message, Is.EqualTo("Stable query order contains an invalid bypass member."));
        }

        private static GridSpatialIndex StableIndex(bool stationary, bool mixed = false)
        {
            var store = new EntityStore(3, IdentityMode.StableWorld);
            var positions = store.Register<Position>("position");
            var colliders = store.Register<Collider>("collider");
            // Two bypass members cannot form a valid order in an unmaintained zero-filled array.
            for (int i = 0; i < (mixed ? 3 : 2); i++)
            {
                EntityId entity = store.Create(StableEntityKey.Placed(1, (ulong)i));
                store.Set(positions, entity, new Position(default));
                store.Set(colliders, entity, new Collider(SimFix.FromInt(i == 2 ? 0 : 2),
                    SimFix.FromInt(i == 2 ? 0 : 4), 1, isStatic:stationary));
            }
            return new GridSpatialIndex(store, positions, colliders, SimFix.FromInt(1),
                maxEntities:3, maxMemberships:16, maxCells:16, maxCellsPerBody:1);
        }
    }
}
