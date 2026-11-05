using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class A4SchedulerTests
    {
        private static SimEvent Payload(int tag, int type = 741) => new SimEvent(new EventTypeId(type),
            source: new EntityId(2, 3), target: new EntityId(4, 5), position: SimVec3.Metres(1, 2, 3),
            aux: SimVec3.Metres(4, 5, 6), magnitude: SimFix.Ratio(3, 2), radius: SimFix.FromInt(7), tag: tag);

        private static void Take(SimScheduler scheduler, long id, SimEvent item)
        {
            Assert.That(scheduler.TryDequeue(out ScheduledTask task), Is.True);
            Assert.That(task.Id, Is.EqualTo(id));
            Assert.That(task.Event, Is.EqualTo(item));
        }

        private static ulong ReferenceHash(long nextId, long tick,
            List<(long Id, SimEvent Event)> pending, List<(long Id, SimEvent Event)> due)
        {
            // Recorded inputs and a library sort keep the reference independent of ring compaction.
            var ordered = new List<(long Id, SimEvent Event)>(pending);
            ordered.Sort(Compare);
            var hash = new StateHash();
            hash.Add(nextId);
            hash.Add(tick);
            hash.Add(ordered.Count);
            foreach (var task in ordered)
            {
                hash.Add(task.Id);
                task.Event.WriteState(hash);
            }

            hash.Add(due.Count);
            foreach (var task in due)
            {
                hash.Add(task.Id);
                task.Event.WriteState(hash);
            }

            return hash.Value;
        }

        private static int Compare((long Id, SimEvent Event) a, (long Id, SimEvent Event) b)
        {
            int tick = a.Event.Tick.CompareTo(b.Event.Tick);
            return tick != 0 ? tick : a.Id.CompareTo(b.Id);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        public void A4_CancelWrappedDueTaskPreservesPayloadsOrderHashAndPendingTasks(int cancelIndex)
        {
            var scheduler = new SimScheduler(8);
            for (int id = 1; id <= 6; id++)
                scheduler.Schedule(1, Payload(id));
            scheduler.Advance(1);
            for (int id = 1; id <= 5; id++)
                Take(scheduler, id, Payload(id).At(1));
            for (int id = 7; id <= 11; id++)
                scheduler.Schedule(2, Payload(id));
            scheduler.Schedule(4, Payload(12));
            scheduler.Schedule(3, Payload(13));
            scheduler.CompleteWarmup();
            scheduler.Advance(2);
            Assert.That(scheduler.DueCount, Is.EqualTo(6));
            Assert.That(scheduler.PendingCount, Is.EqualTo(2));

            long canceled = 6 + cancelIndex;
            Assert.That(scheduler.Cancel(canceled), Is.True);
            Assert.That(scheduler.DueCount, Is.EqualTo(5));
            Assert.That(scheduler.PendingCount, Is.EqualTo(2));
            ulong afterCancel = Fixtures.Hash(scheduler);
            Assert.That(scheduler.Cancel(canceled), Is.False);
            Assert.That(scheduler.Cancel(1), Is.False);
            Assert.That(scheduler.Cancel(0), Is.False);
            Assert.That(scheduler.Cancel(long.MaxValue), Is.False);
            Assert.That(Fixtures.Hash(scheduler), Is.EqualTo(afterCancel));
            Assert.That(scheduler.Schedule(3, Payload(14)), Is.EqualTo(14));

            var pending = new List<(long Id, SimEvent Event)>
            {
                (12, Payload(12).At(4)), (13, Payload(13).At(3)), (14, Payload(14).At(3))
            };
            var due = new List<(long Id, SimEvent Event)>();
            for (int id = 6; id <= 11; id++)
                if (id != canceled)
                    due.Add((id, Payload(id).At(id == 6 ? 1 : 2)));
            Assert.That(Fixtures.Hash(scheduler), Is.EqualTo(ReferenceHash(15, 2, pending, due)));
            foreach (var task in due)
                Take(scheduler, task.Id, task.Event);
            Assert.That(scheduler.TryDequeue(out _), Is.False);
            scheduler.Advance(3);
            Take(scheduler, 13, Payload(13).At(3));
            Take(scheduler, 14, Payload(14).At(3));
            scheduler.Advance(4);
            Take(scheduler, 12, Payload(12).At(4));
            Assert.That(scheduler.DueCount, Is.Zero);
            Assert.That(scheduler.PendingCount, Is.Zero);
        }

        [Test]
        public void A4_CancelOnlyDueTaskReleasesCapacityWithoutReusingIds()
        {
            var scheduler = new SimScheduler(1);
            scheduler.CompleteWarmup();
            for (int tick = 1; tick <= 16; tick++)
            {
                long id = scheduler.Schedule(tick, Payload(tick));
                Assert.That(id, Is.EqualTo(tick));
                scheduler.Advance(tick);
                Assert.That(scheduler.Cancel(id), Is.True);
                Assert.That(scheduler.DueCount, Is.Zero);
                ulong afterCancel = Fixtures.Hash(scheduler);
                Assert.That(scheduler.Cancel(id), Is.False);
                Assert.That(scheduler.TryDequeue(out _), Is.False);
                Assert.That(Fixtures.Hash(scheduler), Is.EqualTo(afterCancel));
            }
        }

        [TestCase(0, "[741:2, 812:1, 913:1]")]
        [TestCase(1, "[741:2, 812:1]")]
        [TestCase(4, "[]")]
        public void A4_ExhaustionReportsLiveBacklogTypesCountsAndRejectedType(int pendingCount, string types)
        {
            var scheduler = new SimScheduler(4);
            for (int id = 1; id <= 3; id++)
                scheduler.Schedule(1, Payload(id, 500));
            scheduler.Advance(1);
            for (int id = 1; id <= 3; id++)
                Take(scheduler, id, Payload(id, 500).At(1));

            int dueCount = 4 - pendingCount;
            int[] dueTypes = {741, 812, 741, 913};
            for (int i = 0; i < 4; i++)
                scheduler.Schedule(i < dueCount ? 2 : 4, Payload(i + 4, i < dueCount ? dueTypes[i] : 620));
            scheduler.CompleteWarmup();
            scheduler.Advance(2);
            ulong before = Fixtures.Hash(scheduler);
            InvalidOperationException error = Assert.Throws<InvalidOperationException>(
                () => scheduler.Schedule(3, Payload(8, 999)));
            Assert.That(error.Message, Does.Contain("capacity=4"));
            Assert.That(error.Message, Does.Contain("pending=" + pendingCount));
            Assert.That(error.Message, Does.Contain("due=" + dueCount));
            Assert.That(error.Message, Does.Contain("requested task type=999"));
            Assert.That(error.Message, Does.Contain("due task types=" + types));
            Assert.That(error.Message, Does.Not.Contain("500"));
            Assert.That(Fixtures.Hash(scheduler), Is.EqualTo(before));
            TestContext.WriteLine("A4 capacity diagnostic: " + error.Message);

            for (int i = 0; i < dueCount; i++)
                Take(scheduler, i + 4, Payload(i + 4, dueTypes[i]).At(2));
            for (int i = dueCount; i < 4; i++)
                Assert.That(scheduler.Cancel(i + 4), Is.True);
            Assert.That(scheduler.Schedule(3, Payload(8, 999)), Is.EqualTo(8));
            scheduler.Advance(3);
            Take(scheduler, 8, Payload(8, 999).At(3));
            Assert.That(scheduler.TryDequeue(out _), Is.False);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void A4_ScheduleUsesAllReservedCapacityWithUnconsumedDueTasks(bool explicitReserve)
        {
            var scheduler = new SimScheduler(2);
            scheduler.Schedule(1, Payload(1));
            scheduler.Schedule(1, Payload(2));
            scheduler.Advance(1);
            Take(scheduler, 1, Payload(1).At(1));
            scheduler.Schedule(2, Payload(3));
            if (explicitReserve)
                scheduler.Reserve(4);
            Assert.That(scheduler.Schedule(2, Payload(4)), Is.EqualTo(4));
            scheduler.CompleteWarmup();
            scheduler.Advance(2);
            scheduler.Advance(3);
            Assert.That(scheduler.DueCount, Is.EqualTo(3));
            Assert.That(scheduler.Schedule(4, Payload(5)), Is.EqualTo(5));
            scheduler.Advance(4);
            Assert.That(scheduler.DueCount, Is.EqualTo(4));
            for (int id = 2; id <= 5; id++)
                Take(scheduler, id, Payload(id).At(id == 2 ? 1 : id <= 4 ? 2 : 4));
            Assert.That(scheduler.TryDequeue(out _), Is.False);
        }

        [Test]
        public void A4_EarlierSystemCancelsAfterAdvanceBeforeLaterConsumer()
        {
            var sim = Fixtures.Empty(1);
            EntityId victim = sim.Entities.Create();
            long canceled = sim.Scheduler.Schedule(1, new SimEvent(new EventTypeId(741), target: victim));
            long survivor = sim.Scheduler.Schedule(1, Payload(2));
            var killer = new KillAndCancelSystem(victim, canceled);
            var consumer = new ConsumerSystem();
            sim.Register("consumer", consumer);
            sim.Register("killer", killer);
            sim.Step(default);
            Assert.That(sim.Entities.IsAlive(victim), Is.False);
            Assert.That(killer.Canceled, Is.True);
            Assert.That(consumer.Count, Is.EqualTo(1));
            Assert.That(consumer.LastId, Is.EqualTo(survivor));
            Assert.That(sim.Scheduler.DueCount, Is.Zero);
            Assert.That(sim.Scheduler.Cancel(canceled), Is.False);
            // Advance already emitted notifications; authoritative cancellation affects the consumer queue.
            Assert.That(sim.Events.Read(1).Length, Is.EqualTo(2));
        }

        private sealed class KillAndCancelSystem : ISimSystem
        {
            private readonly EntityId victim;
            private readonly long taskId;
            public bool Canceled
            {
                get;
                private set;
            }
            public int Order => 10;

            public KillAndCancelSystem(EntityId victim, long taskId)
            {
                this.victim = victim;
                this.taskId = taskId;
            }

            public void Step(SimContext context)
            {
                context.Entities.Destroy(victim);
                Canceled = context.Scheduler.Cancel(taskId);
            }

            public void WriteState(StateHash hash)
            {
                victim.WriteState(hash);
                hash.Add(taskId);
                hash.Add(Canceled);
            }
        }

        private sealed class ConsumerSystem : ISimSystem
        {
            public int Count
            {
                get;
                private set;
            }

            public long LastId
            {
                get;
                private set;
            }
            public int Order => 20;

            public void Step(SimContext context)
            {
                while (context.Scheduler.TryDequeue(out ScheduledTask task))
                {
                    Count++;
                    LastId = task.Id;
                }
            }

            public void WriteState(StateHash hash)
            {
                hash.Add(Count);
                hash.Add(LastId);
            }
        }

        [Test]
        public void A4_RandomDueCancellationsMatchAnIndependentOrderedReference()
        {
            const int rounds = 256;
            var random = new Random(0xA46017);
            int scheduled = 0;
            int canceled = 0;
            int dequeued = 0;
            int hashChecks = 0;
            for (int round = 0; round < rounds; round++)
            {
                int capacity = random.Next(1, 33);
                var scheduler = new SimScheduler(capacity);
                int head = random.Next(capacity);
                long nextId = 1;
                for (int i = 0; i < head; i++)
                    Assert.That(scheduler.Schedule(1, Payload(i)), Is.EqualTo(nextId++));
                scheduler.Advance(1);
                for (int i = 0; i < head; i++)
                    Take(scheduler, i + 1, Payload(i).At(1));
                scheduled += head;
                dequeued += head;

                var pending = new List<(long Id, SimEvent Event)>();
                var due = new List<(long Id, SimEvent Event)>();
                int dueCount = random.Next(1, capacity + 1);
                for (int i = 0; i < capacity; i++)
                {
                    int tick = i < dueCount ? 2 : 3;
                    SimEvent item = Payload((int)nextId, 741 + i % 3).At(tick);
                    Assert.That(scheduler.Schedule(tick, item), Is.EqualTo(nextId));
                    (i < dueCount ? due : pending).Add((nextId, item));
                    nextId++;
                    scheduled++;
                }

                scheduler.CompleteWarmup();
                scheduler.Advance(2);
                while (due.Count != 0)
                {
                    if (random.Next(3) != 0)
                    {
                        int index = random.Next(due.Count);
                        long id = due[index].Id;
                        Assert.That(scheduler.Cancel(id), Is.True);
                        Assert.That(scheduler.Cancel(id), Is.False);
                        due.RemoveAt(index);
                        canceled++;
                    }
                    else
                    {
                        Take(scheduler, due[0].Id, due[0].Event);
                        due.RemoveAt(0);
                        dequeued++;
                    }

                    if (random.Next(2) == 0)
                    {
                        SimEvent item = Payload((int)nextId).At(3);
                        Assert.That(scheduler.Schedule(3, item), Is.EqualTo(nextId));
                        pending.Add((nextId++, item));
                        scheduled++;
                    }

                    Assert.That(scheduler.DueCount, Is.EqualTo(due.Count));
                    Assert.That(scheduler.PendingCount, Is.EqualTo(pending.Count));
                    Assert.That(Fixtures.Hash(scheduler), Is.EqualTo(ReferenceHash(nextId, 2, pending, due)));
                    hashChecks++;
                }

                scheduler.Advance(3);
                Assert.That(Fixtures.Hash(scheduler), Is.EqualTo(ReferenceHash(nextId, 3, due, pending)));
                hashChecks++;
                foreach (var task in pending)
                {
                    Take(scheduler, task.Id, task.Event);
                    dequeued++;
                }

                Assert.That(scheduler.TryDequeue(out _), Is.False);
                Assert.That(scheduler.PendingCount, Is.Zero);
            }

            TestContext.WriteLine("A4 ordered reference: seed=0xA46017; rounds={0}; scheduled={1}; due cancellations={2}; dequeued={3}; hash comparisons={4}; mismatches=0; capacities 1..32, wrapped queues, partial consumption and refills covered.",
                rounds, scheduled, canceled, dequeued, hashChecks);
        }

        [Test]
        public void A4_WarmCancellationSchedulingAndDequeueAllocateNothing()
        {
            var windows = new AllocationWindow();
            do
            {
                var scheduler = new SimScheduler(8);
                scheduler.CompleteWarmup();
                for (int tick = 1; tick <= 32; tick++)
                    Assert.That(CancellationCycle(scheduler, tick), Is.True);
                var meter = new AllocationMeter();
                windows.Begin(meter);
                bool valid = true;
                for (int tick = 33; tick <= 1056; tick++)
                    valid &= CancellationCycle(scheduler, tick);
                AllocationSample allocated = windows.End();
                Assert.That(valid, Is.True);
                if (!windows.NeedsRetry)
                    AllocationAssertions.Zero(allocated);
                Assert.That(scheduler.PendingCount, Is.Zero);
                Assert.That(scheduler.DueCount, Is.Zero);
                TestContext.WriteLine("A4 warm cancellation: ticks=1024; schedules=5120; successful cancels=3072; rejected cancels=3072; dequeues=2048; empty dequeues=1024; {0}.", allocated.Describe());
            }
            while (windows.NeedsRetry);
        }

        private static bool CancellationCycle(SimScheduler scheduler, int tick)
        {
            long first = scheduler.Schedule(tick, Payload(1));
            long second = scheduler.Schedule(tick, Payload(2));
            long third = scheduler.Schedule(tick, Payload(3));
            long fourth = scheduler.Schedule(tick, Payload(4));
            long pending = scheduler.Schedule(tick + 1, Payload(5));
            bool valid = scheduler.Cancel(pending);
            scheduler.Advance(tick);
            valid &= scheduler.Cancel(second);
            valid &= scheduler.TryDequeue(out ScheduledTask task);
            valid &= task.Id == first && task.Event.Tag == 1 && task.Event.Tick == tick;
            valid &= scheduler.Cancel(fourth);
            valid &= !scheduler.Cancel(first);
            valid &= !scheduler.Cancel(0);
            valid &= !scheduler.Cancel(second);
            valid &= scheduler.TryDequeue(out task);
            valid &= task.Id == third && task.Event.Tag == 3 && task.Event.Tick == tick;
            valid &= !scheduler.TryDequeue(out _);
            return valid;
        }
    }
}
