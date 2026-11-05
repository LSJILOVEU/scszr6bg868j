using System;
using System.Reflection;
using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    [Category("Step2bA")]
    public sealed class GridContextRegressionTests
    {
        // Reflection keeps the red phase executable before the approved Bind seam exists.
        internal static ISpatialQueryModes Bind(GridSpatialQuery index, GridQueryContext context)
        {
            MethodInfo method = index.GetType().GetMethod("Bind", new[]{typeof(GridQueryContext)});
            Assert.That(method, Is.Not.Null, "Missing Bind(GridQueryContext): two callers must share one index, not construct two grids.");
            object view = method.Invoke(index, new object[]{context});
            Assert.That(view, Is.InstanceOf<ISpatialQueryModes>(), "A bound view must retain both legacy and mode query APIs.");
            return (ISpatialQueryModes)view;
        }

        private static GridOwnershipTestFixture Scene()
        {
            var scene = new GridOwnershipTestFixture();
            scene.CreateBody(default, 16, 40);
            scene.CreateBody(SimVec3.Metres(4, 0, 0));
            scene.InitializeIndex();
            return scene;
        }

        internal static void Begin(GridQueryContext context)
        {
            typeof(GridQueryContext).GetMethod("Begin", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(context, null);
        }

        internal static bool Visit(GridQueryContext context, int slot)
        {
            return (bool)typeof(GridQueryContext).GetMethod("Visit", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(context, new object[]{slot});
        }

        internal static void ForceWrap(GridQueryContext context)
        {
            typeof(GridQueryContext).GetField("stamp", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(context, uint.MaxValue);
        }

        internal static string ContextState(GridQueryContext context)
        {
            return string.Join("|", QueryReadOnlyTests.CaptureObject(context));
        }

        [Test]
        public void _2bA_IndexDoesNotOwnContextStampOrLastQueryState()
        {
            GridIndexRegressionAudit.NoQueryFields(typeof(GridSpatialIndex));
        }

        [Test]
        public void _2bA_TwoContextsInterleaveQueriesOnOneIndex()
        {
            var scene = Scene();
            var first = new GridQueryContext(16);
            var second = new GridQueryContext(16);
            ISpatialQueryModes a = Bind(scene.Grid, first);
            ISpatialQueryModes b = Bind(scene.Grid, second);
            SpatialRequest request = SpatialQueries.Sphere(default, SimFix.FromInt(32));
            string expected = GridOwnershipTestFixture.Results(scene.Naive(), in request);
            string[] index = QueryReadOnlyTests.Capture(scene.Grid);
            for (int turn = 0; turn < 4; turn++)
            {
                string secondBefore = ContextState(second);
                GridOwnershipTestFixture.EqualResults(expected, GridOwnershipTestFixture.Results(a, in request), "First caller output.");
                Assert.That(ContextState(second), Is.EqualTo(secondBefore), "First caller changed the second context.");
                string firstBefore = ContextState(first);
                Assert.That(b.Nearest(in request, out SpatialHit nearest), Is.True);
                Assert.That(nearest.Entity, Is.EqualTo(new EntityId(0, 1)));
                Assert.That(ContextState(first), Is.EqualTo(firstBefore), "Second caller changed the first context.");
            }
            GridIndexRegressionAudit.Unchanged(index, scene.Grid);
        }

        [Test]
        public void _2bA_NestedQueryLeavesOuterDeduplicationSessionIntact()
        {
            var scene = Scene();
            var outer = new GridQueryContext(16);
            var inner = new GridQueryContext(16);
            ISpatialQueryModes outerView = Bind(scene.Grid, outer);
            ISpatialQueryModes innerView = Bind(scene.Grid, inner);
            SpatialRequest request = SpatialQueries.Sphere(default, SimFix.FromInt(32));
            Assert.That(outerView.Any(in request), Is.True);
            Begin(outer);
            Assert.That(Visit(outer, 0), Is.True);
            string suspended = ContextState(outer);
            string[] index = QueryReadOnlyTests.Capture(scene.Grid);
            var output = new SpatialHit[16];
            Assert.That(innerView.AllCapped(in request, 16, output).TotalHits, Is.EqualTo(2));
            Assert.That(ContextState(outer), Is.EqualTo(suspended), "Inner query must not reset a suspended outer session.");
            Assert.That(Visit(outer, 0), Is.False, "Outer deduplication must still reject an already visited slot.");
            Assert.That(Visit(outer, 1), Is.True, "Outer session must still accept its unvisited slot.");
            GridIndexRegressionAudit.Unchanged(index, scene.Grid);
        }

        [Test]
        public void _2bA_ContextWrapClearsOnlyItsOwnArray()
        {
            var scene = Scene();
            var first = new GridQueryContext(16);
            var second = new GridQueryContext(16);
            Begin(first);
            Begin(second);
            Assert.That(Visit(first, 0), Is.True);
            Assert.That(Visit(second, 1), Is.True);
            string secondBefore = ContextState(second);
            string[] index = QueryReadOnlyTests.Capture(scene.Grid);
            ForceWrap(first);
            Begin(first);
            Assert.That((uint)GridIndexRegressionAudit.Field(first, "stamp"), Is.EqualTo(1u));
            Assert.That((uint[])GridIndexRegressionAudit.Field(first, "seen"), Is.EqualTo(new uint[16]));
            Assert.That(Visit(first, 0), Is.True, "A wrapped context must not suppress a previous stamp-one visit.");
            Assert.That(Visit(first, 0), Is.False);
            Assert.That(ContextState(second), Is.EqualTo(secondBefore));
            GridIndexRegressionAudit.Unchanged(index, scene.Grid);
        }

        [Test]
        public void _2bA_WrappedContextQueriesSameIndexWithoutResettingOtherCaller()
        {
            var scene = Scene();
            var first = new GridQueryContext(16);
            var second = new GridQueryContext(16);
            ISpatialQueryModes a = Bind(scene.Grid, first);
            ISpatialQueryModes b = Bind(scene.Grid, second);
            SpatialRequest request = SpatialQueries.Sphere(default, SimFix.FromInt(32));
            string expected = GridOwnershipTestFixture.Results(scene.Naive(), in request);
            GridOwnershipTestFixture.EqualResults(expected, GridOwnershipTestFixture.Results(b, in request), "Seed second caller.");
            string secondBefore = ContextState(second);
            ForceWrap(first);
            GridOwnershipTestFixture.EqualResults(expected, GridOwnershipTestFixture.Results(a, in request), "Wrapped first caller.");
            Assert.That(ContextState(second), Is.EqualTo(secondBefore));
            GridOwnershipTestFixture.EqualResults(expected, GridOwnershipTestFixture.Results(b, in request), "Second caller after wrap.");
        }
    }
}
