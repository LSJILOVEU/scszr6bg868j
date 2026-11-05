using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    [Category("Step2bA")]
    public sealed class GridPhaseRegressionTests
    {
        [Test]
        public void _2bA_DestroyThenCreateInSameTickDoesNotReusePendingId()
        {
            var scene = new GridOwnershipTestFixture();
            EntityId victim = scene.CreateBody(default);
            scene.InitializeIndex();
            EntityId replacement = default;
            scene.Sim.Register("death-and-spawn", new GridPhaseProbeSystem(10, context =>
            {
                context.Entities.Destroy(victim);
                Assert.That(context.Entities.IsAlive(victim), Is.False);
                replacement = context.Entities.Create();
            }));
            scene.Sim.Step(default);
            Assert.That(replacement.Id, Is.Not.EqualTo(victim.Id), "Destroyed IDs must stay pending until all indexes are cleaned at tick end.");
        }

        [TestCase(10)]
        [TestCase(40)]
        public void _2bA_TickEndRemovesDeathsBeforeNextTickCanReuseId(int deathOrder)
        {
            var scene = new GridOwnershipTestFixture();
            EntityId victim = scene.CreateBody(default, 16, 40);
            scene.InitializeIndex();
            int membershipsAtNextCreate = -1;
            EntityId replacement = default;
            scene.Sim.Register("synchronization-marker", new GridPhaseProbeSystem(30, context => { }), SimSystemPhase.Attachment);
            scene.Sim.Register("death", new GridPhaseProbeSystem(deathOrder, context =>
            {
                if (context.Clock.Tick == 1)
                    context.Entities.Destroy(victim);
            }), deathOrder == 40 ? SimSystemPhase.Queries : SimSystemPhase.Requests);
            scene.Sim.Register("next-tick-create", new GridPhaseProbeSystem(5, context =>
            {
                if (context.Clock.Tick != 2)
                    return;
                membershipsAtNextCreate = scene.Grid.MembershipCount;
                replacement = context.Entities.Create();
            }));
            scene.Sim.Step(default);
            int membershipsAtTickEnd = scene.Grid.MembershipCount;
            scene.Sim.Step(default);
            Assert.That(membershipsAtTickEnd, Is.Zero, "The fixed tick-end cleanup must also handle deaths after movement synchronization.");
            Assert.That(membershipsAtNextCreate, Is.Zero, "No recycled ID may be visible while any old membership remains.");
            Assert.That(replacement.Id, Is.EqualTo(victim.Id));
            Assert.That(replacement.Generation, Is.Not.EqualTo(victim.Generation));
        }

        [Test]
        public void _2bA_TickPipelineCleansDeathsWithoutCallerSynchronizeOrQueries()
        {
            var scene = new GridOwnershipTestFixture();
            EntityId victim = scene.CreateBody(default, 16, 40);
            scene.InitializeIndex();
            scene.Sim.Register("death", new GridPhaseProbeSystem(10, context => context.Entities.Destroy(victim)));
            scene.Sim.Step(default);
            Assert.That(scene.Grid.MembershipCount, Is.Zero, "Sim must invoke fixed cleanup even when no query or explicit Synchronize runs.");
        }

        [Test]
        public void _2bA_NewEntityEntersIndexAtSameTickSynchronization()
        {
            var scene = new GridOwnershipTestFixture();
            scene.AddMovement();
            scene.InitializeIndex();
            EntityId created = default;
            int beforeSync = -1;
            int afterSync = -1;
            var output = new EntityId[16];
            scene.Sim.Register("spawn", new GridPhaseProbeSystem(10, context =>
            {
                created = scene.CreateBody(SimVec3.Metres(32, 0, 0));
                scene.Store.Set(scene.Movements, created, new Movement(MoveMode.Flying, SimVec3.Metres(90, 0, 0)));
                beforeSync = scene.Grid.Sphere(SimVec3.Metres(32, 0, 0), SimFix.FromInt(1), output);
            }));
            scene.Sim.Register("after-movement-query", new GridPhaseProbeSystem(40, context =>
            {
                Assert.That(scene.Store.Read(scene.Positions, created).Value, Is.EqualTo(SimVec3.Metres(35, 0, 0)));
                afterSync = scene.Grid.Sphere(SimVec3.Metres(35, 0, 0), default, output);
            }), SimSystemPhase.Queries);
            scene.Sim.Step(default);
            Assert.That(beforeSync, Is.Zero, "Creation alone must not publish an index entry before synchronization.");
            Assert.That(afterSync, Is.EqualTo(1), "The same tick must publish the new entity after all movement substeps.");
            Assert.That(output[0], Is.EqualTo(created));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void _2bA_AfterSynchronizationPositionWriteIsRejected(bool useSet)
        {
            var scene = new GridOwnershipTestFixture();
            EntityId entity = scene.CreateBody(default);
            scene.InitializeIndex();
            bool reachedWrite = false;
            scene.Sim.Register("sync-then-illegal-write", new GridPhaseProbeSystem(40, context =>
            {
                // Exercise the actual product synchronization boundary, not a test-only guard.
                reachedWrite = true;
                if (useSet)
                    scene.Store.Set(scene.Positions, entity, new Position(SimVec3.Metres(32, 0, 0)));
                else
                    scene.Store.Get(scene.Positions, entity).Value = SimVec3.Metres(32, 0, 0);
            }), SimSystemPhase.Queries);
            try
            {
                GridOwnershipTestFixture.RequirePositionGuard(() => scene.Sim.Step(default));
            }
            finally
            {
                Assert.That(reachedWrite, Is.True, "An unrelated setup failure must not count as a phase guard.");
            }
        }

        [TestCase(1)]
        [TestCase(2)]
        public void _2bA_MovementQueriesReadPreviousSynchronizationPoint(int ticks)
        {
            var scene = new GridOwnershipTestFixture();
            EntityId entity = scene.CreateBody(SimVec3.Metres(8, 1, 8));
            scene.Store.Set(scene.Movements, entity, new Movement(MoveMode.Ground, SimVec3.Metres(240, 0, 0)));
            var navigation = new QueryingNavigation(scene.Grid);
            scene.AddMovement(navigation);
            scene.InitializeIndex();
            for (int tick = 1; tick <= ticks; tick++)
                scene.Sim.Step(default);
            Assert.That(navigation.BeforeCounts, Is.EqualTo(ticks == 1 ? new[]{1} : new[]{1, 1}), "Movement must read the previous sync point on every tick.");
            Assert.That(navigation.FutureCounts, Is.EqualTo(ticks == 1 ? new[]{0} : new[]{0, 0}), "Pending movement must remain invisible to queries.");
        }

        [Test]
        public void _2bA_RequestsAdvanceAndAttachmentFinishBeforeIndexPublication()
        {
            var scene = new GridOwnershipTestFixture();
            EntityId host = scene.CreateBody(SimVec3.Metres(8, 1, 8));
            EntityId passenger = scene.CreateBody(SimVec3.Metres(8, 5, 8));
            scene.Store.Set(scene.Movements, host, new Movement(MoveMode.Ground, SimVec3.Metres(240, 0, 0)));
            var navigation = new QueryingNavigation(scene.Grid);
            scene.AddMovement(navigation);
            scene.InitializeIndex();
            var trace = new List<string>();
            var output = new EntityId[16];
            int duringFollow = -1;
            int published = -1;
            scene.Sim.Register("apply-position-request", new GridPhaseProbeSystem(10, context =>
            {
                trace.Add("request");
                scene.Store.Get(scene.Positions, host).Value = SimVec3.Metres(40, 1, 8);
            }));
            scene.Sim.Register("attachment-follow", new GridPhaseProbeSystem(30, context =>
            {
                trace.Add("advance=" + scene.Store.Get(scene.Positions, host).Value.X.Raw);
                scene.Store.Get(scene.Positions, passenger).Value = scene.Store.Get(scene.Positions, host).Value + SimVec3.Metres(0, 4, 0);
                duringFollow = scene.Grid.Sphere(SimVec3.Metres(48, 3, 8), SimFix.FromInt(4), output);
                trace.Add("attachment");
            }), SimSystemPhase.Attachment);
            scene.Sim.Register("query-final-positions", new GridPhaseProbeSystem(40, context =>
            {
                published = scene.Grid.Sphere(SimVec3.Metres(48, 3, 8), SimFix.FromInt(4), output);
                trace.Add("query");
            }), SimSystemPhase.Queries);
            scene.Sim.Step(default);
            Assert.That(trace, Is.EqualTo(new[]{"request", "advance=" + 48 * SimFix.Scale, "attachment", "query"}));
            Assert.That(duringFollow, Is.Zero, "Substep three still reads the previous index.");
            Assert.That(published, Is.EqualTo(2), "One publication must include both the final host and attached passenger.");
            Assert.That(output[0], Is.EqualTo(host));
            Assert.That(output[1], Is.EqualTo(passenger));
        }

        private sealed class QueryingNavigation : INavProvider
        {
            private readonly ISpatialQuery query;
            private readonly EntityId[] output = new EntityId[16];
            internal readonly List<int> BeforeCounts = new List<int>();
            internal readonly List<int> FutureCounts = new List<int>();
            public string StateTypeId => "test.2ba.querying-navigation";
            public int StateVersion => 1;

            internal QueryingNavigation(ISpatialQuery query) => this.query = query;

            public SimVec3 Advance(SimVec3 position, SimVec3 displacement, SimFix halfHeight)
            {
                BeforeCounts.Add(query.Sphere(position, default, output));
                FutureCounts.Add(query.Sphere(position + displacement, default, output));
                return position + displacement;
            }

            public void WriteState(StateHash hash)
            {
            }

            public void SaveState(SimStateWriter writer)
            {
            }

            public void LoadState(SimStateReader reader)
            {
            }
        }
    }
}
