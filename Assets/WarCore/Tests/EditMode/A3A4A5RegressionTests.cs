using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class A3A4A5RegressionTests
    {
        private static SimEvent Payload(int tag) => new SimEvent(new EventTypeId(741),
            source: new EntityId(2, 3), target: new EntityId(4, 5), position: SimVec3.Metres(tag, 2, 3),
            aux: SimVec3.Metres(4, 5, 6), magnitude: SimFix.Ratio(3, 2), radius: SimFix.FromInt(7), tag: tag);

        private static List<ScheduledTask> Drain(SimScheduler scheduler)
        {
            var tasks = new List<ScheduledTask>();
            while (scheduler.TryDequeue(out ScheduledTask task))
                tasks.Add(task);
            return tasks;
        }

        private static long[] Ids(List<ScheduledTask> tasks)
        {
            var ids = new long[tasks.Count];
            for (int i = 0; i < ids.Length; i++)
                ids[i] = tasks[i].Id;
            return ids;
        }

        [Test]
        public void A3_SamePendingTasksHashEquallyAfterDifferentCancellationHistories()
        {
            var first = new SimScheduler(8);
            var second = new SimScheduler(8);
            int[] ticks = {2, 2, 2, 3, 2};
            for (int i = 0; i < ticks.Length; i++)
            {
                Assert.That(first.Schedule(ticks[i], Payload(i + 1)), Is.EqualTo(i + 1));
                Assert.That(second.Schedule(ticks[i], Payload(i + 1)), Is.EqualTo(i + 1));
            }

            Assert.That(first.Cancel(1), Is.True);
            Assert.That(first.Cancel(3), Is.True);
            Assert.That(second.Cancel(3), Is.True);
            Assert.That(second.Cancel(1), Is.True);
            first.Advance(1);
            second.Advance(1);
            first.CompleteWarmup();
            second.CompleteWarmup();
            Assert.That(first.PendingCount, Is.EqualTo(3));
            Assert.That(second.PendingCount, Is.EqualTo(3));
            Assert.That(first.DueCount, Is.Zero);
            Assert.That(second.DueCount, Is.Zero);
            ulong firstHash = Fixtures.Hash(first);
            ulong secondHash = Fixtures.Hash(second);

            // Observing the next id and complete drain rules out differing authority behind the hash.
            Assert.That(first.Schedule(4, Payload(6)), Is.EqualTo(6));
            Assert.That(second.Schedule(4, Payload(6)), Is.EqualTo(6));
            Assert.That(first.Cancel(6), Is.True);
            Assert.That(second.Cancel(6), Is.True);
            var firstTasks = new List<ScheduledTask>();
            var secondTasks = new List<ScheduledTask>();
            for (int tick = 2; tick <= 3; tick++)
            {
                first.Advance(tick);
                second.Advance(tick);
                firstTasks.AddRange(Drain(first));
                secondTasks.AddRange(Drain(second));
            }

            long[] expectedIds = {2, 5, 4};
            Assert.That(Ids(firstTasks), Is.EqualTo(expectedIds));
            Assert.That(Ids(secondTasks), Is.EqualTo(expectedIds));
            for (int i = 0; i < expectedIds.Length; i++)
            {
                int id = (int)expectedIds[i];
                SimEvent expected = Payload(id).At(ticks[id - 1]);
                Assert.That(firstTasks[i].Event, Is.EqualTo(expected), "A3 first history payload");
                Assert.That(secondTasks[i].Event, Is.EqualTo(expected), "A3 second history payload");
            }

            Assert.That(Fixtures.Hash(first), Is.EqualTo(Fixtures.Hash(second)), "A3 drained authority must match");
            TestContext.WriteLine("A3 nonempty pending set: ids=2,5,4; nextId=6; advancedThrough=1; due=0; hashes=0x{0:X16},0x{1:X16}", firstHash, secondHash);
            // A3 known-hash vector placeholder: freeze this nonempty fixture after the canonical hash fix.
            // A pre-fix constant would pin the defect; this phase asserts the ordering invariant instead.
            Assert.That(firstHash, Is.EqualTo(secondHash),
                "A3 heap layout leaked into state hash: identical ids, deadlines, payloads, nextId, advancedThrough and due FIFO must hash equally.");
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void A4_CancelCurrentTickDueTaskRemovesItAndPreservesSurvivorOrder(int cancelIndex)
        {
            var scheduler = new SimScheduler(4);
            var ids = new long[3];
            for (int i = 0; i < ids.Length; i++)
                ids[i] = scheduler.Schedule(1, Payload(i + 1));
            scheduler.CompleteWarmup();
            scheduler.Advance(1);
            Assert.That(scheduler.PendingCount, Is.Zero);
            Assert.That(scheduler.DueCount, Is.EqualTo(3));
            bool canceled = scheduler.Cancel(ids[cancelIndex]);
            int dueAfterCancel = scheduler.DueCount;
            bool canceledAgain = scheduler.Cancel(ids[cancelIndex]);
            List<ScheduledTask> remaining = Drain(scheduler);
            var expected = new List<long>(ids);
            expected.RemoveAt(cancelIndex);
            foreach (ScheduledTask task in remaining)
                Assert.That(task.Event, Is.EqualTo(Payload((int)task.Id).At(1)), "A4 survivor payload");
            Assert.That(scheduler.DueCount, Is.Zero);
            Assert.That((canceled, dueAfterCancel, canceledAgain, string.Join(",", Ids(remaining))),
                Is.EqualTo((true, 2, false, string.Join(",", expected))),
                "A4 Cancel must remove an already-due task and preserve survivor order: (cancel result, due count, second cancel result, remaining ids).");
        }

        [TestCase("task type", 741)]
        [TestCase("capacity", 4)]
        public void A4_FullUnconsumedDueQueueErrorIdentifiesTaskTypeAndCapacity(string field, int value)
        {
            var scheduler = new SimScheduler(4);
            for (int i = 0; i < 4; i++)
                scheduler.Schedule(1, Payload(i + 1));
            scheduler.CompleteWarmup();
            scheduler.Advance(1);
            Assert.That(scheduler.PendingCount, Is.Zero);
            Assert.That(scheduler.DueCount, Is.EqualTo(4));
            ulong before = Fixtures.Hash(scheduler);
            InvalidOperationException error = Assert.Throws<InvalidOperationException>(
                () => scheduler.Schedule(2, Payload(5)), "A4 a full authoritative due queue must report capacity exhaustion.");
            TestContext.WriteLine("A4 backlog exception: " + error.GetType().Name + ": " + error.Message);
            Assert.That(Fixtures.Hash(scheduler), Is.EqualTo(before), "A4 rejected scheduling must preserve queued authority.");
            Assert.That(Ids(Drain(scheduler)), Is.EqualTo(new long[]{1, 2, 3, 4}));
            Assert.That(error.Message, Does.Match(@"\b" + value + @"\b"), "A4 backlog error must identify " + field + " " + value + ".");
        }

        [TestCase(0)]
        [TestCase(3)]
        public void A4_ScheduleCurrentTickThrowsWithoutChangingAuthority(int currentTick)
        {
            var scheduler = new SimScheduler(4);
            for (int tick = 1; tick <= currentTick; tick++)
                scheduler.Advance(tick);
            ulong before = Fixtures.Hash(scheduler);
            Assert.Throws<ArgumentOutOfRangeException>(() => scheduler.Schedule(currentTick, Payload(1)),
                "A4 Schedule(current tick) must reject a deadline that cannot be advanced again.");
            Assert.That(Fixtures.Hash(scheduler), Is.EqualTo(before));
            Assert.That(scheduler.Schedule(currentTick + 1, Payload(1)), Is.EqualTo(1));
        }

        [TestCase(0)]
        [TestCase(3)]
        public void A4_AdvanceSkippingATickThrowsWithoutLosingTheNextDeadline(int currentTick)
        {
            var scheduler = new SimScheduler(4);
            for (int tick = 1; tick <= currentTick; tick++)
                scheduler.Advance(tick);
            long id = scheduler.Schedule(currentTick + 1, Payload(1));
            ulong before = Fixtures.Hash(scheduler);
            Assert.Throws<InvalidOperationException>(() => scheduler.Advance(currentTick + 2),
                "A4 Advance must reject a skipped tick instead of stranding due tasks.");
            Assert.That(Fixtures.Hash(scheduler), Is.EqualTo(before));
            scheduler.Advance(currentTick + 1);
            Assert.That(Ids(Drain(scheduler)), Is.EqualTo(new[]{id}));
        }

        [Test]
        public void A4_ReservePreservesWrappedDueQueueAndSupportsWrappingAgain()
        {
            var scheduler = new SimScheduler(4);
            for (int id = 1; id <= 3; id++)
                Assert.That(scheduler.Schedule(1, Payload(id)), Is.EqualTo(id));
            scheduler.Advance(1);
            Take(scheduler, 1, 1);
            Take(scheduler, 2, 1);
            Assert.That(scheduler.Schedule(2, Payload(4)), Is.EqualTo(4));
            Assert.That(scheduler.Schedule(2, Payload(5)), Is.EqualTo(5));
            scheduler.Advance(2);
            Assert.That(scheduler.DueCount, Is.EqualTo(3));
            ulong before = Fixtures.Hash(scheduler);
            scheduler.Reserve(6);
            Assert.That(Fixtures.Hash(scheduler), Is.EqualTo(before), "A4 Reserve must preserve the wrapped FIFO and authority.");
            scheduler.CompleteWarmup();
            for (int id = 6; id <= 8; id++)
                Assert.That(scheduler.Schedule(3, Payload(id)), Is.EqualTo(id));
            scheduler.Advance(3);
            Assert.That(scheduler.DueCount, Is.EqualTo(6));
            for (int id = 3; id <= 6; id++)
                Take(scheduler, id, id <= 3 ? 1 : id <= 5 ? 2 : 3);
            for (int id = 9; id <= 12; id++)
                Assert.That(scheduler.Schedule(4, Payload(id)), Is.EqualTo(id));
            scheduler.Advance(4);
            Assert.That(scheduler.DueCount, Is.EqualTo(6));
            for (int id = 7; id <= 12; id++)
                Take(scheduler, id, id <= 8 ? 3 : 4);
            Assert.That(scheduler.TryDequeue(out _), Is.False);
            Assert.That(scheduler.DueCount, Is.Zero);
            Assert.That(scheduler.PendingCount, Is.Zero);
        }

        private static void Take(SimScheduler scheduler, int id, int tick)
        {
            Assert.That(scheduler.TryDequeue(out ScheduledTask task), Is.True, "A4 wrapped queue lost a task.");
            Assert.That(task.Id, Is.EqualTo(id), "A4 wrapped queue changed FIFO order.");
            Assert.That(task.Event, Is.EqualTo(Payload(id).At(tick)), "A4 wrapped queue changed payload.");
        }

        [TestCase(SpatialQueryKind.Sphere)]
        [TestCase(SpatialQueryKind.Sector)]
        [TestCase(SpatialQueryKind.Capsule)]
        [TestCase(SpatialQueryKind.Ray)]
        public void A5_DestroyedEntityIsFilteredAndAllItsMembershipsAreReclaimed(SpatialQueryKind kind)
        {
            var scene = new A5GridScenario();
            EntityId survivor = scene.CreateBody(SimVec3.Metres(4, 0, 0), 1, 2);
            int survivorMemberships = scene.Grid.MembershipCount;
            EntityId dead = scene.CreateBody(default, 9, 20);
            Assert.That(scene.Grid.MembershipCount - survivorMemberships, Is.GreaterThan(1));
            scene.Store.Destroy(dead);
            int membershipsBeforeQuery = scene.Grid.MembershipCount;
            A5GridScenario.QueryResult result = A5GridScenario.Query(scene.Grid, kind, default);
            Assert.That(scene.Grid.MembershipCount, Is.EqualTo(membershipsBeforeQuery), "Query must leave all index membership unchanged.");
            scene.Grid.Synchronize();
            int membershipsAfterSynchronization = scene.Grid.MembershipCount;
            bool staleMembershipsRemain = scene.Grid.Remove(dead);
            long distance = kind == SpatialQueryKind.Ray ? 35 * SimFix.Scale : 0;
            string expectedResult = new A5GridScenario.QueryResult(new[]{survivor}, new[]{distance}).ToString();
            Assert.That((result.ToString(), membershipsAfterSynchronization, staleMembershipsRemain),
                Is.EqualTo((expectedResult, survivorMemberships, false)),
                "A5 query must filter the destroyed handle and preserve its live neighbor; synchronization must reclaim all dead cells: (ordered id:generation@distanceRaw, memberships, stale Remove result).");
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        public void A5_UpsertReusedSlotWithMissingComponentsReclaimsTheDeadOccupant(bool hasPosition, bool hasCollider)
        {
            var scene = new A5GridScenario();
            EntityId old = scene.CreateBody(default, 9, 20);
            scene.Store.Destroy(old);
            // Deliberately retain an independent stale cache to exercise generation defenses.
            scene.Store.RecycleDestroyed();
            EntityId replacement = scene.Store.Create();
            Assert.That(replacement.Id, Is.EqualTo(old.Id));
            Assert.That(replacement.Generation, Is.Not.EqualTo(old.Generation));
            if (hasPosition)
                scene.Store.Set(scene.Positions, replacement, new Position(default));
            if (hasCollider)
                scene.Store.Set(scene.Colliders, replacement, new Collider(SimFix.FromInt(1), SimFix.FromInt(2)));
            scene.Grid.Upsert(replacement);
            int membershipsImmediatelyAfterUpsert = scene.Grid.MembershipCount;
            var counts = new List<int>();
            foreach (SpatialQueryKind kind in A5GridScenario.QueryKinds())
                counts.Add(A5GridScenario.Query(scene.Grid, kind, default).Entities.Length);
            // Preserve the count before querying so query-time cleanup cannot hide a broken Upsert branch.
            Assert.That((membershipsImmediatelyAfterUpsert, string.Join(",", counts)), Is.EqualTo((0, "0,0,0,0")),
                "A5 missing-component Upsert must clean the dead occupant by slot despite its old generation: (memberships immediately after Upsert, Sphere/Sector/Capsule/Ray counts).");
        }

        [Test]
        public void A5_Random200MutationStepsMatchRebuiltGridAndIndependentNaiveQueries()
        {
            new A5GridDifferential().Run();
        }
    }
}
