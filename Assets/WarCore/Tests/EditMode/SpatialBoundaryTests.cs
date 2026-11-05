using System;
using System.Reflection;
using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    [Category("Step2bA")]
    public sealed class SpatialBoundaryTests
    {
        [Test]
        public void GenerationExhaustionRejectsDestroyBeforeAnyLifecycleChange()
        {
            var store = new EntityStore(1);
            store.Create();
            var generations = (ulong[])typeof(EntityStore).GetField("generations", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(store);
            generations[0] = ulong.MaxValue;
            var entity = new EntityId(0, ulong.MaxValue);
            Assert.Throws<SimBoundaryException>(() => store.Destroy(entity));
            Assert.That(store.IsAlive(entity), Is.True);
            Assert.That(store.PendingDestroyCount, Is.Zero);
            Assert.That(generations[0], Is.EqualTo(ulong.MaxValue));
        }

        [Test]
        public void CapacityArithmeticRejectsOverflowBeforeMultiplicationOrAllocation()
        {
            Assert.That(EntityStore.GrownCapacity(1), Is.EqualTo(2));
            Assert.That(EntityStore.GrownCapacity(int.MaxValue / 2), Is.EqualTo(int.MaxValue - 1));
            Assert.Throws<SimBoundaryException>(() => EntityStore.GrownCapacity(int.MaxValue / 2 + 1));
            Assert.Throws<SimBoundaryException>(() => EntityStore.GrownCapacity(int.MaxValue));
            var store = new EntityStore(1);
            var positions = store.Register<Position>("position");
            var colliders = store.Register<Collider>("collider");
            Assert.Throws<SimBoundaryException>(() => new GridSpatialQuery(store, positions, colliders, SimFix.FromRaw(1), 1, int.MaxValue));
        }

        [Test]
        public void SimQueryBindingExposesNoMutationSurfaceAndChecksContextCapacity()
        {
            var scene = new GridOwnershipTestFixture();
            Assert.That(scene.Sim.SpatialQuery, Is.Not.InstanceOf<GridSpatialQuery>());
            Assert.That(scene.Sim.SpatialQuery.GetType().GetMethod("Upsert"), Is.Null);
            Assert.That(scene.Sim.SpatialQuery.GetType().GetMethod("Rebuild"), Is.Null);
            Assert.Throws<ArgumentException>(() => scene.Sim.BindSpatialQuery(new GridQueryContext(1)));
            var context = new GridQueryContext(scene.Capacity);
            var view = (SpatialQueryView)scene.Sim.BindSpatialQuery(context);
            Assert.That(view.QueryContext, Is.SameAs(context));
            Assert.That(view.Index, Is.SameAs(scene.Sim.SpatialIndex));
            GridIndexRegressionAudit.NoQueryFields(view.Index.GetType());
        }

        [Test]
        public void InvalidRestorationDoesNotRebuildBeforeFinalPayloadValidation()
        {
            var scene = new GridOwnershipTestFixture();
            EntityId entity = scene.CreateBody(default);
            byte[] saved = scene.Sim.SaveSnapshot();
            scene.Store.Set(scene.Positions, entity, new Position(SimVec3.Metres(32, 0, 0)));
            scene.Sim.CompleteSpatialBoundary();
            string[] laterIndex = QueryReadOnlyTests.Capture(scene.Grid);
            var corrupt = new byte[saved.Length + 1];
            Array.Copy(saved, corrupt, saved.Length);
            Assert.Throws<SimSnapshotException>(() => scene.Sim.RestoreSnapshot(corrupt));
            GridIndexRegressionAudit.Unchanged(laterIndex, scene.Grid);
            Assert.Throws<InvalidOperationException>(() => scene.Sim.Step(default));
        }

        [Test]
        public void ChangedSystemPhaseIsDetectedByHashAndSnapshotManifest()
        {
            var a = new GridOwnershipTestFixture();
            var b = new GridOwnershipTestFixture();
            a.Sim.Register("pulse", new PulseSystem(), SimSystemPhase.Requests);
            b.Sim.Register("pulse", new PulseSystem(), SimSystemPhase.Queries);
            Assert.That(a.Sim.StateHash(), Is.Not.EqualTo(b.Sim.StateHash()));
            Assert.Throws<SimSnapshotException>(() => b.Sim.RestoreSnapshot(a.Sim.SaveSnapshot()));
        }

        [Test]
        public void BoundRayContextsKeepTheirDdaScratchIndependent()
        {
            var scene = new GridOwnershipTestFixture();
            scene.CreateBody(default, 16, 40);
            scene.InitializeIndex();
            var a = new GridQueryContext(scene.Capacity);
            var b = new GridQueryContext(scene.Capacity);
            ISpatialQueryModes first = scene.Sim.BindSpatialQuery(a);
            ISpatialQueryModes second = scene.Sim.BindSpatialQuery(b);
            SpatialRequest ray = GridOwnershipTestFixture.Request(SpatialQueryKind.Ray, default);
            string expected = GridOwnershipTestFixture.Results(scene.Naive(), in ray);
            GridOwnershipTestFixture.EqualResults(expected, GridOwnershipTestFixture.Results(first, in ray), "First DDA output.");
            string[] scratch = QueryReadOnlyTests.CaptureObject(a);
            GridOwnershipTestFixture.EqualResults(expected, GridOwnershipTestFixture.Results(second, in ray), "Second DDA output.");
            CollectionAssert.AreEqual(scratch, QueryReadOnlyTests.CaptureObject(a));
            Assert.That(second.Nearest(in ray, out SpatialHit hit), Is.True);
            Assert.That(hit.Distance.Raw, Is.EqualTo(48 * SimFix.Scale));
            CollectionAssert.AreEqual(scratch, QueryReadOnlyTests.CaptureObject(a));
        }
    }
}
