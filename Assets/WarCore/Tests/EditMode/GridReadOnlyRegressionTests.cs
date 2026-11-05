using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    [Category("Step2bA")]
    public sealed class GridReadOnlyRegressionTests
    {
        public static IEnumerable<TestCaseData> QueryCases()
        {
            foreach (SpatialQueryKind kind in Enum.GetValues(typeof(SpatialQueryKind)))
                for (int mode = 0; mode < 4; mode++)
                    yield return new TestCaseData(kind, mode);
        }

        [TestCaseSource(nameof(QueryCases))]
        public void _2bA_R21_QueryPreservesMembershipOccupancyAndAllIndexFields(SpatialQueryKind kind, int mode)
        {
            var scene = new GridOwnershipTestFixture();
            scene.CreateBody(default);
            EntityId dead = scene.CreateBody(default, 16, 40);
            scene.CreateBody(SimVec3.Metres(1, 0, 0));
            scene.InitializeIndex();
            scene.Store.Destroy(dead);
            Assert.That(scene.Store.IsAlive(dead), Is.False);
            ulong authority = Fixtures.Hash(scene.Store);
            string[] physical = QueryReadOnlyTests.Capture(scene.Grid);
            string[] logical = GridIndexRegressionAudit.Logical(scene.Grid);
            int memberships = scene.Grid.MembershipCount;
            SpatialRequest request = GridOwnershipTestFixture.Request(kind, default);
            uint stamp = (uint)GridIndexRegressionAudit.Field(scene.Grid.QueryContext, "stamp");
            if (mode == 0)
            {
                GridOwnershipTestFixture.EqualResults(GridOwnershipTestFixture.Results(scene.Naive(), in request),
                    GridOwnershipTestFixture.Results(scene.Grid, in request), "Dead generations must be filtered.");
            }
            else if (mode == 1)
                Assert.That(scene.Grid.Any(in request), Is.True);
            else if (mode == 2)
            {
                Assert.That(scene.Grid.Nearest(in request, out SpatialHit hit), Is.True);
                Assert.That(scene.Store.IsAlive(hit.Entity), Is.True);
            }
            else
            {
                var hits = new SpatialHit[16];
                Assert.That(scene.Grid.AllCapped(in request, 16, hits).TotalHits, Is.EqualTo(2));
                Assert.That(scene.Store.IsAlive(hits[0].Entity) && scene.Store.IsAlive(hits[1].Entity), Is.True);
            }
            Assert.That(Fixtures.Hash(scene.Store), Is.EqualTo(authority));
            Assert.That(scene.Grid.MembershipCount, Is.EqualTo(memberships));
            GridIndexRegressionAudit.Unchanged(physical, scene.Grid);
            GridIndexRegressionAudit.Equal(logical, GridIndexRegressionAudit.Logical(scene.Grid), "Every occupied cell must remain unchanged.");
            Assert.That((uint)GridIndexRegressionAudit.Field(scene.Grid.QueryContext, "stamp"), Is.EqualTo(stamp + 1),
                "Only caller scratch advances its stamp; it is not part of index equality.");
        }

        [Test]
        public void _2bA_R21_ColliderlessReuseCannotResurrectCachedGeneration()
        {
            var scene = new GridOwnershipTestFixture();
            EntityId victim = scene.CreateBody(default, 16, 40);
            scene.InitializeIndex();
            scene.Sim.Register("destroy", new GridPhaseProbeSystem(10, context => context.Entities.Destroy(victim)));
            scene.Sim.Step(default);
            EntityId replacement = scene.Store.Create();
            Assert.That(replacement.Id, Is.EqualTo(victim.Id), "Reuse is allowed after the completed tick.");
            Assert.That(replacement.Generation, Is.Not.EqualTo(victim.Generation));
            scene.Store.Set(scene.Positions, replacement, new Position(default));
            EntityId pending = scene.CreateBody(default, 16, 40);
            scene.Sim.CompleteSpatialBoundary();
            scene.Store.Destroy(pending);
            Assert.That(scene.Grid.MembershipCount, Is.GreaterThan(0), "The reuse guard must also encounter a pending dead membership.");
            string[] before = QueryReadOnlyTests.Capture(scene.Grid);
            SpatialRequest request = SpatialQueries.Sphere(default, SimFix.FromInt(64));
            GridOwnershipTestFixture.EqualResults("[]", GridOwnershipTestFixture.Results(scene.Grid, in request), "No collider means no hit.");
            GridIndexRegressionAudit.Unchanged(before, scene.Grid);
        }

        [Test]
        public void _2bA_DeathIsImmediateButQueriesDoNotRemoveMemberships()
        {
            var scene = new GridOwnershipTestFixture();
            EntityId victim = scene.CreateBody(default);
            scene.InitializeIndex();
            scene.Sim.Register("death-probe", new GridPhaseProbeSystem(10, context =>
            {
                context.Entities.Destroy(victim);
                Assert.That(context.Entities.IsAlive(victim), Is.False);
                string[] before = QueryReadOnlyTests.Capture(scene.Grid);
                SpatialRequest request = SpatialQueries.Sphere(default, SimFix.FromInt(2));
                GridOwnershipTestFixture.EqualResults("[]", GridOwnershipTestFixture.Results(scene.Grid, in request), "Death must be visible immediately.");
                GridIndexRegressionAudit.Unchanged(before, scene.Grid);
            }));
            scene.Sim.Step(default);
        }
    }
}
