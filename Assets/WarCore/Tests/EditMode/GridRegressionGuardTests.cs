using System;
using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    [Category("Step2bA")]
    public sealed class GridRegressionGuardTests
    {
        [Test]
        public void _2bA_ReadOnlyGuardReportsAQueryThatRemovesMemberships()
        {
            var scene = new GridOwnershipTestFixture();
            EntityId victim = scene.CreateBody(default, 16, 40);
            scene.InitializeIndex();
            string[] before = QueryReadOnlyTests.Capture(scene.Grid);
            var badQuery = new RemovingQuery(scene.Grid, victim);
            Assert.That(badQuery.Sphere(default, SimFix.FromInt(1), new EntityId[16]), Is.EqualTo(1));
            AssertionException error = Assert.Throws<AssertionException>(() => GridIndexRegressionAudit.Unchanged(before, scene.Grid));
            Assert.That(error.Message, Does.Contain("Query changed index state"));
        }

        [Test]
        public void _2bA_LogicalIndexGuardReportsMissingLargeBodyCells()
        {
            var scene = new GridOwnershipTestFixture();
            EntityId large = scene.CreateBody(default, 16, 40);
            scene.InitializeIndex();
            Assert.That(scene.Grid.MembershipCount, Is.EqualTo(1));
            Assert.That(scene.Grid.Index.LargeBodyCount, Is.EqualTo(1));
            string[] expected = GridIndexRegressionAudit.Logical(scene.Grid);
            scene.Grid.Remove(large);
            Assert.Throws<AssertionException>(() => GridIndexRegressionAudit.Equal(expected, GridIndexRegressionAudit.Logical(scene.Grid), "Missing large memberships."));
        }

        [Test]
        public void _2bA_DifferentialGuardReportsAQueryThatDropsRealHits()
        {
            var scene = new GridOwnershipTestFixture();
            EntityId victim = scene.CreateBody(default);
            scene.InitializeIndex();
            SpatialRequest request = SpatialQueries.Sphere(default, SimFix.FromInt(1));
            string expected = GridOwnershipTestFixture.Results(scene.Naive(), in request);
            scene.Grid.Remove(victim);
            string wrong = GridOwnershipTestFixture.Results(scene.Grid, in request);
            Assert.That(expected, Is.Not.EqualTo("[]"));
            Assert.Throws<AssertionException>(() => GridOwnershipTestFixture.EqualResults(expected, wrong, "Injected missing hit."));
        }

        [Test]
        public void _2bA_QueryStateFieldGuardReportsInjectedContextAndStamp()
        {
            Assert.Throws<AssertionException>(() => GridIndexRegressionAudit.NoQueryFields(typeof(QueryStateInIndex)));
            GridIndexRegressionAudit.NoQueryFields(typeof(NoQueryState));
        }

        [Test]
        public void _2bA_OwnershipSurfaceGuardReportsMissingReadInterface()
        {
            Assert.Throws<AssertionException>(() => GridIndexRegressionAudit.SimHasQuerySurface(typeof(NoQueryState)));
            GridIndexRegressionAudit.SimHasQuerySurface(typeof(ReadSurface));
        }

        [Test]
        public void _2bA_PositionGuardProbeDistinguishesRejectionFromSilentWrite()
        {
            var scene = new GridOwnershipTestFixture();
            EntityId entity = scene.CreateBody(default);
            GridOwnershipTestFixture.RequirePositionGuard(() => throw new SimBoundaryException("Position write after synchronization."));
            Assert.Throws<AssertionException>(() => GridOwnershipTestFixture.RequirePositionGuard(() =>
                scene.Store.Get(scene.Positions, entity).Value = SimVec3.Metres(32, 0, 0)));
            Assert.That(scene.Store.Get(scene.Positions, entity).Value, Is.EqualTo(SimVec3.Metres(32, 0, 0)), "The deliberate violating write must actually run.");
        }

        [Test]
        public void _2bA_ContextIsolationGuardReportsAnInjectedResetOfOtherCaller()
        {
            var second = new GridQueryContext(16);
            GridContextRegressionTests.Begin(second);
            GridContextRegressionTests.Visit(second, 3);
            string expected = GridContextRegressionTests.ContextState(second);
            // This models the forbidden shared-context implementation, using the real context reset path.
            GridContextRegressionTests.Begin(second);
            Assert.Throws<AssertionException>(() => Assert.That(GridContextRegressionTests.ContextState(second), Is.EqualTo(expected)));
        }

        private sealed class QueryStateInIndex
        {
            public readonly GridQueryContext Context = new GridQueryContext(16);
            public uint stamp = 1;
            public int LastCount = 1;
        }

        private sealed class NoQueryState
        {
            public int Memberships = 1;
        }

        private sealed class ReadSurface
        {
            public ISpatialQuery Queries => null;
        }

        private sealed class RemovingQuery : SpatialQueryBase
        {
            private readonly GridSpatialQuery grid;
            private readonly EntityId victim;

            internal RemovingQuery(GridSpatialQuery grid, EntityId victim)
            {
                this.grid = grid;
                this.victim = victim;
            }

            protected override int Execute(SpatialRequest request, Span<EntityId> entities, Span<RayHit> hits)
            {
                int count = grid.Sphere(request.Origin, request.Radius, entities);
                grid.Remove(victim);
                return count;
            }
        }
    }
}
