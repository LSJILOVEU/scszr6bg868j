using System;
using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    [Category("Step2bA")]
    public sealed class SpatialPipelineTests
    {
        [Test]
        public void OutOfTickDestroyThenSaveUsesTheSameBoundaryAndRestoresReusableId()
        {
            var scene = new GridOwnershipTestFixture();
            EntityId dead = scene.CreateBody(default, 16, 40);
            scene.InitializeIndex();
            scene.Store.Destroy(dead);
            Assert.That(scene.Store.PendingDestroyCount, Is.EqualTo(1));
            Assert.That(scene.Grid.MembershipCount, Is.EqualTo(1));
            Assert.That(scene.Grid.Index.LargeBodyCount, Is.EqualTo(1));
            byte[] saved = scene.Sim.SaveSnapshot();
            Assert.That(scene.Store.PendingDestroyCount, Is.Zero);
            Assert.That(scene.Grid.MembershipCount, Is.Zero);
            EntityId replacement = scene.Store.Create();
            Assert.That(replacement, Is.EqualTo(new EntityId(dead.Id, dead.Generation + 1)));
            scene.Sim.RestoreSnapshot(saved);
            Assert.That(scene.Store.Create(), Is.EqualTo(replacement));
        }

        [Test]
        public void UnchangedBodiesDoNotRewriteTheIndexDuringTicksOrBoundaryCalls()
        {
            var scene = new GridOwnershipTestFixture();
            scene.CreateBody(default, 16, 40);
            scene.InitializeIndex();
            string[] before = QueryReadOnlyTests.Capture(scene.Grid);
            for (int tick = 0; tick < 4; tick++)
            {
                scene.Sim.Step(default);
                scene.Sim.CompleteSpatialBoundary();
                GridIndexRegressionAudit.Unchanged(before, scene.Grid);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void BufferedMoversReadOldPositionsInEitherAdvancementOrder(bool reverse)
        {
            var scene = new GridOwnershipTestFixture();
            EntityId left = scene.CreateBody(SimVec3.Metres(-4, 0, 0));
            EntityId right = scene.CreateBody(SimVec3.Metres(4, 0, 0));
            scene.InitializeIndex();
            var output = new EntityId[16];
            scene.Sim.Register("opposing-movers", new GridPhaseProbeSystem(20, context =>
            {
                for (int i = 0; i < 2; i++)
                {
                    EntityId self = (i == 0) != reverse ? left : right;
                    EntityId other = self.Equals(left) ? right : left;
                    SimVec3 start = scene.Store.Read(scene.Positions, self).Value;
                    SimVec3 otherStart = scene.Store.Read(scene.Positions, other).Value;
                    Assert.That(start.X.Raw, Is.EqualTo(self.Equals(left) ? -4 * SimFix.Scale : 4 * SimFix.Scale));
                    Assert.That(otherStart.X.Raw, Is.EqualTo(self.Equals(left) ? 4 * SimFix.Scale : -4 * SimFix.Scale));
                    // This hand-authored contact response uses the old full segment, not endpoint overlap.
                    Assert.That(context.SpatialQuery.Capsule(start, otherStart, SimFix.FromInt(1), output), Is.EqualTo(2));
                    context.SubmitMovement(self, SimVec3.Metres(self.Equals(left) ? -1 : 1, 0, 0));
                    Assert.That(scene.Store.Read(scene.Positions, self).Value, Is.EqualTo(start));
                }
            }), SimSystemPhase.Movement);
            scene.Sim.Step(default);
            Assert.That(scene.Store.Read(scene.Positions, left).Value, Is.EqualTo(SimVec3.Metres(-1, 0, 0)));
            Assert.That(scene.Store.Read(scene.Positions, right).Value, Is.EqualTo(SimVec3.Metres(1, 0, 0)));
            Assert.That(scene.Grid.Sphere(default, default, output), Is.EqualTo(2));
        }

        [Test]
        public void RequestsThenMovementThenHostFirstAttachmentPublishOnce()
        {
            var scene = new GridOwnershipTestFixture();
            EntityId child = scene.CreateBody(SimVec3.Metres(0, 8, 0));
            EntityId passenger = scene.CreateBody(SimVec3.Metres(0, 4, 0));
            EntityId host = scene.CreateBody(default);
            scene.Store.Set(scene.Movements, host, new Movement(MoveMode.Flying, SimVec3.Metres(90, 0, 0)));
            scene.AddMovement();
            scene.Sim.Attach(child, passenger, SimVec3.Metres(0, 4, 0));
            scene.Sim.Attach(passenger, host, SimVec3.Metres(0, 4, 0));
            scene.Sim.RequestPosition(host, SimVec3.Metres(32, 0, 0));
            scene.InitializeIndex();
            scene.Sim.Step(default);
            Assert.That(scene.Store.Read(scene.Positions, host).Value, Is.EqualTo(SimVec3.Metres(35, 0, 0)));
            Assert.That(scene.Store.Read(scene.Positions, passenger).Value, Is.EqualTo(SimVec3.Metres(35, 4, 0)));
            Assert.That(scene.Store.Read(scene.Positions, child).Value, Is.EqualTo(SimVec3.Metres(35, 8, 0)));
            SpatialRequest query = SpatialQueries.Sphere(SimVec3.Metres(35, 4, 0), SimFix.FromInt(8));
            GridOwnershipTestFixture.EqualResults(GridOwnershipTestFixture.Results(scene.Naive(), in query),
                GridOwnershipTestFixture.Results(scene.Grid, in query), "Every attached member must publish together.");
        }

        [Test]
        public void PendingPositionRequestsAndAttachmentsAreSnapshotAuthority()
        {
            var scene = new GridOwnershipTestFixture();
            EntityId host = scene.CreateBody(default);
            EntityId passenger = scene.CreateBody(SimVec3.Metres(0, 4, 0));
            scene.Sim.Attach(passenger, host, SimVec3.Metres(0, 4, 0));
            scene.Sim.RequestPosition(host, SimVec3.Metres(32, 0, 0));
            byte[] saved = scene.Sim.SaveSnapshot();
            ulong hash = scene.Sim.StateHash();
            scene.Sim.Step(default);
            ulong nextHash = scene.Sim.StateHash();
            scene.Sim.RestoreSnapshot(saved);
            Assert.That(scene.Sim.StateHash(), Is.EqualTo(hash));
            scene.Sim.Step(default);
            Assert.That(scene.Sim.StateHash(), Is.EqualTo(nextHash));
            Assert.That(scene.Store.Read(scene.Positions, passenger).Value, Is.EqualTo(SimVec3.Metres(32, 4, 0)));
        }

        [Test]
        public void RequestsSubmittedAfterPublicationWaitUntilTheFollowingTick()
        {
            var scene = new GridOwnershipTestFixture();
            EntityId host = scene.CreateBody(default);
            EntityId passenger = scene.CreateBody(SimVec3.Metres(8, 4, 0));
            scene.InitializeIndex();
            scene.Sim.Register("late-requests", new GridPhaseProbeSystem(40, context =>
            {
                if (context.Clock.Tick != 1)
                    return;
                scene.Sim.RequestPosition(host, SimVec3.Metres(32, 0, 0));
                scene.Sim.Attach(passenger, host, SimVec3.Metres(0, 4, 0));
                Assert.That(scene.Store.Read(scene.Positions, host).Value, Is.EqualTo(default(SimVec3)));
                Assert.That(scene.Store.Read(scene.Positions, passenger).Value, Is.EqualTo(SimVec3.Metres(8, 4, 0)));
            }), SimSystemPhase.Queries);
            scene.Sim.Step(default);
            scene.Sim.Step(default);
            Assert.That(scene.Store.Read(scene.Positions, host).Value, Is.EqualTo(SimVec3.Metres(32, 0, 0)));
            Assert.That(scene.Store.Read(scene.Positions, passenger).Value, Is.EqualTo(SimVec3.Metres(32, 4, 0)));
        }

        [Test]
        public void OwnedQueriesPublishColliderLayerAndShapeTogetherAtSynchronization()
        {
            var scene = new GridOwnershipTestFixture();
            EntityId entity = scene.CreateBody(default);
            scene.InitializeIndex();
            SpatialRequest oldLayer = SpatialQueries.Sphere(default, default, 1);
            SpatialRequest newLayer = SpatialQueries.Sphere(default, default, 2);
            scene.Sim.Register("change-collider", new GridPhaseProbeSystem(10, context =>
            {
                scene.Store.Set(scene.Colliders, entity, new Collider(SimFix.FromInt(2), SimFix.FromInt(4), 2));
                Assert.That(context.SpatialQuery.Any(in oldLayer), Is.True);
                Assert.That(context.SpatialQuery.Any(in newLayer), Is.False);
            }));
            scene.Sim.Register("read-published-collider", new GridPhaseProbeSystem(40, context =>
            {
                Assert.That(context.SpatialQuery.Any(in oldLayer), Is.False);
                Assert.That(context.SpatialQuery.Any(in newLayer), Is.True);
            }), SimSystemPhase.Queries);
            scene.Sim.Step(default);
        }

        [Test]
        public void CyclicAttachmentsFailExplicitlyBeforePublication()
        {
            var scene = new GridOwnershipTestFixture();
            EntityId a = scene.CreateBody(default);
            EntityId b = scene.CreateBody(SimVec3.Metres(8, 0, 0));
            scene.Sim.Attach(a, b, default);
            scene.Sim.Attach(b, a, default);
            scene.InitializeIndex();
            string[] before = QueryReadOnlyTests.Capture(scene.Grid);
            Assert.That(Assert.Throws<SimBoundaryException>(() => scene.Sim.Step(default)).Message, Does.Contain("Cyclic"));
            GridIndexRegressionAudit.Unchanged(before, scene.Grid);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void ColliderWritesAfterPublicationAreRejectedThroughEveryEntry(int entry)
        {
            var scene = new GridOwnershipTestFixture();
            EntityId entity = scene.CreateBody(default);
            scene.InitializeIndex();
            scene.Sim.Register("illegal-shape", new GridPhaseProbeSystem(40, context =>
            {
                if (entry == 0)
                    scene.Store.Set(scene.Colliders, entity, new Collider(SimFix.FromInt(2), SimFix.FromInt(4)));
                else if (entry == 1)
                    scene.Store.Get(scene.Colliders, entity) = new Collider(SimFix.FromInt(2), SimFix.FromInt(4));
                else if (entry == 2)
                    scene.Store.Remove(scene.Colliders, entity);
                else
                    scene.Store.Set(entity, new Collider(SimFix.FromInt(2), SimFix.FromInt(4)));
            }), SimSystemPhase.Queries);
            Assert.Throws<SimBoundaryException>(() => scene.Sim.Step(default));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PreviouslyIssuedPositionReferenceCannotBypassTheFence(bool queryBeforeReturn)
        {
            var scene = new GridOwnershipTestFixture();
            EntityId entity = scene.CreateBody(default);
            scene.InitializeIndex();
            ref Position borrowed = ref scene.Store.Get(scene.Positions, entity);
            borrowed.Value = default;
            // The array is the same storage reached by the ref issued above. It survives the phase transition.
            Position[] escapedStorage = scene.Positions.Storage.Values;
            var output = new EntityId[16];
            scene.Sim.Register("escaped-position", new GridPhaseProbeSystem(40, context =>
            {
                escapedStorage[entity.Id].Value = SimVec3.Metres(32, 0, 0);
                if (queryBeforeReturn)
                    context.SpatialQuery.Sphere(default, SimFix.FromInt(1), output);
            }), SimSystemPhase.Queries);
            GridOwnershipTestFixture.RequirePositionGuard(() => scene.Sim.Step(default));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PostureChangesAfterPublicationAreRejected(bool useRef)
        {
            var scene = new PostureFixture();
            scene.Sim.Register("illegal-posture", new GridPhaseProbeSystem(40, context =>
            {
                if (useRef)
                    scene.Sim.Entities.Get(scene.Postures, scene.Unit) = new Posture(2);
                else
                    scene.Sim.Entities.Set(scene.Postures, scene.Unit, new Posture(2));
            }), SimSystemPhase.Queries);
            Assert.Throws<SimBoundaryException>(() => scene.Sim.Step(default));
        }

        [Test]
        public void WarmOwnedMovementSynchronizationQueriesAndHashAllocateZero()
        {
            var windows = new AllocationWindow();
            do
            {
                var scene = new GridOwnershipTestFixture();
                EntityId entity = scene.CreateBody(default);
                scene.Store.Set(scene.Movements, entity, new Movement(MoveMode.Flying, SimVec3.Metres(30, 0, 0)));
                scene.AddMovement();
                var output = new SpatialHit[16];
                SpatialRequest request = SpatialQueries.Sphere(default, SimFix.FromInt(1024));
                for (int tick = 0; tick < 16; tick++)
                {
                    scene.Sim.Step(default);
                    scene.Grid.AllCapped(in request, 16, output);
                    scene.Sim.StateHash();
                }
                var meter = new AllocationMeter();
                windows.Begin(meter);
                int hits = 0;
                for (int tick = 0; tick < 512; tick++)
                {
                    scene.Sim.Step(default);
                    hits += scene.Grid.AllCapped(in request, 16, output).Written;
                    scene.Sim.StateHash();
                }
                AllocationSample allocation = windows.End();
                Assert.That(hits, Is.EqualTo(512));
                if (!windows.NeedsRetry)
                {
                    AllocationAssertions.Zero(allocation);
                    TestContext.WriteLine("2bA owned pipeline: ticks=512; hits=512; " + allocation.Describe());
                }
            }
            while (windows.NeedsRetry);
        }
    }
}
