using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class A3SchedulerHashTests
    {
        public const ulong NonemptyPendingHash = 0x22CB551F19EC00FEUL;

        private static SimEvent Payload(int tag) => new SimEvent(new EventTypeId(741),
            source: new EntityId(2, 3), target: new EntityId(4, 5), position: SimVec3.Metres(tag, 2, 3),
            aux: SimVec3.Metres(4, 5, 6), magnitude: SimFix.Ratio(3, 2), radius: SimFix.FromInt(7), tag: tag);

        private static SimScheduler KnownFixture(bool reverseCancellation)
        {
            var scheduler = new SimScheduler(8);
            int[] ticks = {2, 2, 2, 3, 2};
            for (int i = 0; i < ticks.Length; i++)
                scheduler.Schedule(ticks[i], Payload(i + 1));
            Assert.That(scheduler.Cancel(reverseCancellation ? 3 : 1), Is.True);
            Assert.That(scheduler.Cancel(reverseCancellation ? 1 : 3), Is.True);
            scheduler.Advance(1);
            scheduler.CompleteWarmup();
            return scheduler;
        }

        [Test]
        public void A3_NonemptyPendingSetHasKnownHashVector()
        {
            var pending = new List<(long Id, SimEvent Event)>
            {
                (2, Payload(2).At(2)),
                (5, Payload(5).At(2)),
                (4, Payload(4).At(3))
            };
            Assert.That(ReferenceHash(6, 1, pending, new List<(long, SimEvent)>()), Is.EqualTo(NonemptyPendingHash));
            foreach (bool reverseCancellation in new[]{false, true})
            {
                SimScheduler scheduler = KnownFixture(reverseCancellation);
                Assert.That(scheduler.PendingCount, Is.EqualTo(3));
                Assert.That(scheduler.DueCount, Is.Zero);
                Assert.That(Fixtures.Hash(scheduler), Is.EqualTo(NonemptyPendingHash));
            }

            TestContext.WriteLine("A3 known nonempty scheduler hash: ids=2,5,4; ticks=2,2,3; nextId=6; advancedThrough=1; pending=3; due=0; hash=0x{0:X16}; both cancellation histories verified.", NonemptyPendingHash);
        }

        private static int Compare((long Id, SimEvent Event) a, (long Id, SimEvent Event) b)
        {
            int tick = a.Event.Tick.CompareTo(b.Event.Tick);
            return tick != 0 ? tick : a.Id.CompareTo(b.Id);
        }

        private static ulong ReferenceHash(long nextId, long advancedThrough,
            List<(long Id, SimEvent Event)> pending, List<(long Id, SimEvent Event)> due)
        {
            // A library sort over recorded inputs avoids sharing the scheduler's heap traversal.
            var ordered = new List<(long Id, SimEvent Event)>(pending);
            ordered.Sort(Compare);
            var hash = new StateHash();
            hash.Add(nextId);
            hash.Add(advancedThrough);
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

        [Test]
        public void A3_RandomCancellationHistoriesMatchAnIndependentSortedReference()
        {
            const int rounds = 256;
            var random = new Random(0xA35017);
            int hashChecks = 0;
            int scheduled = 0;
            int cancellations = 0;
            for (int round = 0; round < rounds; round++)
            {
                var first = new SimScheduler(1);
                var second = new SimScheduler(1);
                second.Reserve(512);
                var due = new List<(long Id, SimEvent Event)>();
                var pending = new List<(long Id, SimEvent Event)>();
                var canceled = new List<long>();
                long nextId = 1;
                int dueCount = random.Next(6);
                for (int i = 0; i < dueCount; i++)
                {
                    SimEvent item = Payload(500 + i);
                    Assert.That(first.Schedule(1, item), Is.EqualTo(nextId));
                    Assert.That(second.Schedule(1, item), Is.EqualTo(nextId));
                    due.Add((nextId++, item.At(1)));
                }

                int count = round < 4 ? round : random.Next(4, 257);
                for (int i = 0; i < count; i++)
                {
                    long tick = i % 17 == 0 ? long.MaxValue : random.Next(2, 34);
                    SimEvent item = Payload(i + 1);
                    Assert.That(first.Schedule(tick, item), Is.EqualTo(nextId));
                    Assert.That(second.Schedule(tick, item), Is.EqualTo(nextId));
                    if (random.Next(3) == 0)
                        canceled.Add(nextId);
                    else
                        pending.Add((nextId, item.At(tick)));
                    nextId++;
                }

                foreach (long id in canceled)
                    Assert.That(first.Cancel(id), Is.True);
                for (int i = canceled.Count - 1; i >= 0; i--)
                    Assert.That(second.Cancel(canceled[i]), Is.True);
                first.Advance(1);
                second.Advance(1);
                first.CompleteWarmup();
                second.CompleteWarmup();
                ulong expected = ReferenceHash(nextId, 1, pending, due);
                Assert.That(Fixtures.Hash(first), Is.EqualTo(expected), "First history round " + round);
                Assert.That(Fixtures.Hash(second), Is.EqualTo(expected), "Second history round " + round);
                Assert.That(Fixtures.Hash(first), Is.EqualTo(expected), "Repeated hash round " + round);
                hashChecks += 3;
                if (due.Count != 0)
                {
                    Take(first, due[0].Id, due[0].Event);
                    Take(second, due[0].Id, due[0].Event);
                    due.RemoveAt(0);
                    expected = ReferenceHash(nextId, 1, pending, due);
                    Assert.That(Fixtures.Hash(first), Is.EqualTo(expected));
                    Assert.That(Fixtures.Hash(second), Is.EqualTo(expected));
                    hashChecks += 2;
                }

                // Cancellation after hashing must still address the live heap correctly.
                foreach (var task in pending)
                {
                    Assert.That(first.Cancel(task.Id), Is.True);
                    Assert.That(second.Cancel(task.Id), Is.True);
                }

                pending.Clear();
                expected = ReferenceHash(nextId, 1, pending, due);
                Assert.That(Fixtures.Hash(first), Is.EqualTo(expected));
                Assert.That(Fixtures.Hash(second), Is.EqualTo(expected));
                hashChecks += 2;
                scheduled += dueCount + count;
                cancellations += canceled.Count;
            }

            TestContext.WriteLine("A3 sorted reference: seed=0xA35017; rounds={0}; scheduled inputs per history={1}; initial cancellations per history={2}; hash comparisons={3}; mismatches=0; empty/single/tied/long.MaxValue deadlines, due consumption, and automatic/explicit Reserve covered.", rounds, scheduled, cancellations, hashChecks);
        }

        private static T Field<T>(SimScheduler scheduler, string name)
        {
            return (T)typeof(SimScheduler).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(scheduler);
        }

        private static void Take(SimScheduler scheduler, long id, SimEvent item)
        {
            Assert.That(scheduler.TryDequeue(out ScheduledTask task), Is.True);
            Assert.That(task.Id, Is.EqualTo(id));
            Assert.That(task.Event, Is.EqualTo(item));
        }

        [Test]
        public void A3_HashingPreservesLiveHeapIndicesAndWrappedDueQueue()
        {
            var scheduler = new SimScheduler(8);
            for (int id = 1; id <= 6; id++)
                scheduler.Schedule(1, Payload(id));
            scheduler.Advance(1);
            for (int id = 1; id <= 5; id++)
                Take(scheduler, id, Payload(id).At(1));
            for (int id = 7; id <= 9; id++)
                scheduler.Schedule(2, Payload(id));
            scheduler.Schedule(5, Payload(10));
            scheduler.Schedule(4, Payload(11));
            scheduler.Schedule(4, Payload(12));
            scheduler.Schedule(3, Payload(13));
            scheduler.Advance(2);
            scheduler.CompleteWarmup();
            var pending = new List<(long Id, SimEvent Event)>
            {
                (10, Payload(10).At(5)), (11, Payload(11).At(4)),
                (12, Payload(12).At(4)), (13, Payload(13).At(3))
            };
            var dueTasks = new List<(long Id, SimEvent Event)>
            {
                (6, Payload(6).At(1)), (7, Payload(7).At(2)),
                (8, Payload(8).At(2)), (9, Payload(9).At(2))
            };
            var before = new SimStateWriter();
            scheduler.SaveScheduler(before);
            byte[] stateBefore = before.ToArray();
            var handles = new ScheduledTaskHandle[pending.Count];
            for (int i = 0; i < pending.Count; i++)
                Assert.That(scheduler.TryGetHandle(pending[i].Id, out handles[i]), Is.True);
            ulong expected = ReferenceHash(14, 2, pending, dueTasks);
            for (int i = 0; i < 16; i++)
                Assert.That(Fixtures.Hash(scheduler), Is.EqualTo(expected));
            var after = new SimStateWriter();
            scheduler.SaveScheduler(after);
            Assert.That(after.ToArray(), Is.EqualTo(stateBefore));
            for (int i = 0; i < pending.Count; i++)
            {
                Assert.That(scheduler.TryGetPending(pending[i].Id, out ScheduledTask live), Is.True);
                Assert.That(live.Event, Is.EqualTo(pending[i].Event));
                Assert.That(scheduler.TryGetHandle(pending[i].Id, out ScheduledTaskHandle handle), Is.True);
                Assert.That(handle, Is.EqualTo(handles[i]));
            }
            Assert.That(scheduler.PendingCount, Is.EqualTo(4));
            Assert.That(scheduler.DueCount, Is.EqualTo(4));
            Assert.That(scheduler.Cancel(12), Is.True);
            foreach (var task in dueTasks)
                Take(scheduler, task.Id, task.Event);
            scheduler.Advance(3);
            Take(scheduler, 13, Payload(13).At(3));
            scheduler.Advance(4);
            Take(scheduler, 11, Payload(11).At(4));
            scheduler.Advance(5);
            Take(scheduler, 10, Payload(10).At(5));
            Assert.That(scheduler.PendingCount, Is.Zero);
            Assert.That(scheduler.DueCount, Is.Zero);
            Assert.That(scheduler.Schedule(6, Payload(14)), Is.EqualTo(14));
            TestContext.WriteLine("A3 read-only hash: 16 repeated hashes; serialized heap/due order and live id/handle lookups unchanged; subsequent cancellation, dequeue and scheduling verified.");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void A3_HashingUsesReservedStorageWithoutWarmAllocations(bool explicitReserve)
        {
            var windows = new AllocationWindow();
            do
            {
                var scheduler = new SimScheduler(1);
                if (explicitReserve)
                    scheduler.Reserve(256);
                for (int i = 0; i < 256; i++)
                    scheduler.Schedule(i < 8 ? 1 : 2 + (i % 17), Payload(i + 1));
                scheduler.Advance(1);
                scheduler.CompleteWarmup();
                var hash = new StateHash();
                for (int i = 0; i < 32; i++)
                    scheduler.WriteState(hash);
                var meter = new AllocationMeter();
                windows.Begin(meter);
                for (int i = 0; i < 256; i++)
                    scheduler.WriteState(hash);
                AllocationSample allocated = windows.End();
                if (!windows.NeedsRetry)
                    AllocationAssertions.Zero(allocated);
                Assert.That(scheduler.PendingCount, Is.EqualTo(248));
                Assert.That(scheduler.DueCount, Is.EqualTo(8));
                TestContext.WriteLine("A3 warm hash: explicit Reserve={0}; calls=256; pending=248; due=8; {1}; checksum=0x{2:X16}.", explicitReserve, allocated.Describe(), hash.Value);
            }
            while (windows.NeedsRetry);
        }
    }
}
