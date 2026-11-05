using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    [Category("S39")]
    public sealed class IncrementalSchedulerHashRegressionTests
    {
        private static Action<StateHash> Writer(SimScheduler scheduler)
        {
            MethodInfo method = typeof(SimScheduler).GetMethod("WriteStateV2");
            Assert.That(method, Is.Not.Null, "The incremental protocol must be explicit; v1 remains unchanged.");
            return (Action<StateHash>)Delegate.CreateDelegate(typeof(Action<StateHash>), scheduler, method);
        }

        private static Func<ulong> SimHash(Simulation sim)
        {
            MethodInfo method = typeof(Simulation).GetMethod("StateHashV2", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, "A caller must explicitly select v2.");
            return (Func<ulong>)Delegate.CreateDelegate(typeof(Func<ulong>), sim, method);
        }

        private static ulong Hash(Action<StateHash> write)
        {
            var hash = new StateHash();
            write(hash);
            return hash.Value;
        }

        // The oracle enumerates current tasks; it has no incremental cache to share a missed update.
        private static ulong Sum(IEnumerable<ScheduledTask> tasks)
        {
            ulong sum = 0;
            foreach (ScheduledTask task in tasks)
            {
                var hash = new StateHash();
                hash.Add("warcore.scheduler-task-v2");
                hash.Add(task.Id);
                SimEvent item = task.Event;
                hash.Add(item.Type.Value);
                hash.Add(item.Tick);
                hash.Add(item.Sequence);
                item.Source.WriteState(hash);
                item.Target.WriteState(hash);
                item.Position.WriteState(hash);
                item.Aux.WriteState(hash);
                hash.Add(item.Magnitude.Raw);
                hash.Add(item.Radius.Raw);
                hash.Add(item.Tag);
                sum = unchecked(sum + hash.Value);
            }
            return sum;
        }

        private static ulong Reference(long nextId, long tick, List<ScheduledTask> pending, List<ScheduledTask> due)
        {
            var hash = new StateHash();
            hash.Add("warcore.scheduler-v2");
            hash.Add(nextId);
            hash.Add(tick);
            hash.Add(pending.Count);
            hash.Add(Sum(pending));
            hash.Add(due.Count);
            hash.Add(Sum(due));
            return hash.Value;
        }

        [Test]
        public void IncrementalProtocolIsOptInAndLeavesTheKnownV1VectorUntouched()
        {
            Simulation a = SnapshotFixtures.A3(false);
            Simulation b = SnapshotFixtures.A3(true);
            Func<ulong> v2 = SimHash(a);
            Assert.That(Fixtures.Hash(a.Scheduler), Is.EqualTo(0x22CB551F19EC00FEUL));
            byte[] before = a.SaveSnapshot();
            Assert.That(v2(), Is.Not.EqualTo(a.StateHash()));
            Assert.That(v2(), Is.EqualTo(SimHash(b)()));
            Assert.That(Hash(Writer(a.Scheduler)), Is.EqualTo(0xEBB41DEBE5CC13ADUL));
            Assert.That(Hash(Writer(b.Scheduler)), Is.EqualTo(0xEBB41DEBE5CC13ADUL));
            Assert.That(Fixtures.Hash(a.Scheduler), Is.EqualTo(0x22CB551F19EC00FEUL));
            Assert.That(a.SaveSnapshot(), Is.EqualTo(before));
            TestContext.WriteLine("S39 A3 scheduler v2=0x{0:X16}; scheduler v1=0x{1:X16}; whole Sim v2=0x{2:X16}",
                Hash(Writer(a.Scheduler)), Fixtures.Hash(a.Scheduler), v2());
        }

        [TestCase(false)]
        [TestCase(true)]
        public void WholeMatchV2RestoresAll490ContinuationHashes(bool fresh)
        {
            Simulation online = Fixtures.Create(123);
            for (int tick = 1; tick <= 510; tick++)
                online.Step(Fixtures.Input(tick));
            byte[] saved = online.SaveSnapshot();
            var expected = new ulong[490];
            for (int tick = 511; tick <= 1000; tick++)
            {
                online.Step(Fixtures.Input(tick));
                expected[tick - 511] = online.StateHashV2();
            }
            Assert.That(online.StateHash(), Is.EqualTo(0x8166D6848CDEB66CUL));
            Assert.That(expected[489], Is.EqualTo(0x498903006F99EE22UL));
            Simulation restored = fresh ? Fixtures.Create(999) : online;
            restored.RestoreSnapshot(saved);
            for (int tick = 511; tick <= 1000; tick++)
            {
                restored.Step(Fixtures.Input(tick));
                Assert.That(restored.StateHashV2(), Is.EqualTo(expected[tick - 511]));
            }
            Assert.That(restored.StateHash(), Is.EqualTo(0x8166D6848CDEB66CUL));
            TestContext.WriteLine("S39 whole match: fresh={0}; ticks=1000; v1=0x{1:X16}; v2=0x{2:X16}; 490 continuation hashes equal.",
                fresh, restored.StateHash(), restored.StateHashV2());
        }

        [Test]
        public void CacheMatchesFullRecomputationAcrossAllTaskLifecycleOperations()
        {
            var scheduler = new SimScheduler(2);
            Action<StateHash> write = Writer(scheduler);
            var pending = new List<ScheduledTask>();
            var due = new List<ScheduledTask>();
            var random = new Random(0x539);
            long nextId = 1;
            for (int i = 0; i < 256; i++)
            {
                int deadline = random.Next(1, 17);
                long id = scheduler.Schedule(deadline, new SimEvent(new EventTypeId(i % 7 + 1),
                    source: new EntityId(i, 1), target: new EntityId(i + 1, 2),
                    position: SimVec3.Metres(i, -i, 3), aux: SimVec3.Metres(-2, i, 5),
                    magnitude: SimFix.FromRaw(i - 128), radius: SimFix.FromRaw(i), tag: i));
                Assert.That(id, Is.EqualTo(nextId++));
                Assert.That(scheduler.TryGetPending(id, out ScheduledTask task), Is.True);
                pending.Add(task);
                Assert.That(Hash(write), Is.EqualTo(Reference(nextId, 0, pending, due)), "schedule=" + i);
            }
            for (int i = pending.Count - 1; i >= 0; i -= 3)
            {
                Assert.That(scheduler.Cancel(pending[i].Id), Is.True);
                Assert.That(scheduler.Cancel(pending[i].Id), Is.False);
                pending.RemoveAt(i);
                Assert.That(Hash(write), Is.EqualTo(Reference(nextId, 0, pending, due)));
            }
            for (int tick = 1; tick <= 16; tick++)
            {
                due.AddRange(pending.Where(task => task.Event.Tick == tick).OrderBy(task => task.Id));
                pending.RemoveAll(task => task.Event.Tick == tick);
                scheduler.Advance(tick);
                Assert.That(Hash(write), Is.EqualTo(Reference(nextId, tick, pending, due)), "advance=" + tick);
                if (tick == 1)
                {
                    scheduler.Reserve(1024);
                    scheduler.CompleteWarmup();
                    Assert.That(Hash(write), Is.EqualTo(Reference(nextId, tick, pending, due)), "reserve with due work");
                }
                if (due.Count != 0)
                {
                    int middle = due.Count / 2;
                    Assert.That(scheduler.Cancel(due[middle].Id), Is.True);
                    due.RemoveAt(middle);
                    Assert.That(Hash(write), Is.EqualTo(Reference(nextId, tick, pending, due)), "cancel due");
                }
                var type = new EventTypeId(tick % 7 + 1);
                int typed = due.FindIndex(task => task.Event.Type.Value == type.Value);
                bool removed = scheduler.TryDequeue(type, out ScheduledTask actual);
                Assert.That(removed, Is.EqualTo(typed >= 0));
                if (typed >= 0)
                {
                    Assert.That(actual.Id, Is.EqualTo(due[typed].Id));
                    due.RemoveAt(typed);
                }
                Assert.That(Hash(write), Is.EqualTo(Reference(nextId, tick, pending, due)), "typed dequeue");
                while (due.Count != 0)
                {
                    Assert.That(scheduler.TryDequeue(out actual), Is.True);
                    Assert.That(actual.Id, Is.EqualTo(due[0].Id));
                    due.RemoveAt(0);
                    Assert.That(Hash(write), Is.EqualTo(Reference(nextId, tick, pending, due)), "global dequeue");
                }
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RestoredCacheMatchesEveryOnlineTickIncludingNewTasks(bool fresh)
        {
            Simulation online = SnapshotFixtures.A3(false);
            online.Step(default);
            byte[] saved = online.SaveSnapshot();
            ulong expectedAtSave = SimHash(online)();
            var expected = new ulong[16];
            for (int i = 0; i < expected.Length; i++)
            {
                Continue(online, i);
                expected[i] = SimHash(online)();
            }
            Simulation restored = fresh ? new Simulation(999, config: online.Config) : online;
            restored.RestoreSnapshot(saved);
            Assert.That(SimHash(restored)(), Is.EqualTo(expectedAtSave));
            for (int i = 0; i < expected.Length; i++)
            {
                Continue(restored, i);
                Assert.That(SimHash(restored)(), Is.EqualTo(expected[i]), "continued tick=" + i);
            }
        }

        private static void Continue(Simulation sim, int tag)
        {
            while (sim.Scheduler.TryDequeue(out _)) { }
            long id = sim.Scheduler.Schedule(sim.Clock.Tick + 1, new SimEvent(new EventTypeId(931), tag: tag));
            if (tag % 3 == 0)
                sim.Scheduler.Cancel(id);
            sim.Step(default);
        }

        [Test]
        public void VersionTwoSchedulerWriterDoesNotTraversePendingOrDueStorage()
        {
            MethodInfo method = typeof(SimScheduler).GetMethod("WriteStateV2");
            Assert.That(method, Is.Not.Null);
            var instructions = HotPathAllocationRules.Read(method).ToArray();
            Assert.That(instructions.Any(instruction => instruction.Targets.Any(target => target < instruction.Offset)), Is.False);
            Assert.That(instructions.Any(instruction => instruction.Member is MethodBase call
                && call.DeclaringType == typeof(SimScheduler)), Is.False, "The v2 writer must read cached values directly.");
        }

        [Test]
        public void WarmedCacheUpdatesAndBothHashEntryPointsAllocateZero()
        {
            var scheduler = new SimScheduler(8);
            Action<StateHash> write = Writer(scheduler);
            var sink = new StateHash();
            Simulation sim = Fixtures.Create(123);
            Func<ulong> simV2 = SimHash(sim);
            for (int i = 0; i < 8; i++)
                sim.Step(default);
            scheduler.CompleteWarmup();
            Cycle(scheduler, 1);
            write(sink);
            simV2();
            var meter = new AllocationMeter();
            if (meter.Basis != AllocationBasis.Direct)
                Assert.Inconclusive("Strict zero requires the direct positive control.");
            Assert.That(meter.DirectControlDelta.Bytes, Is.GreaterThanOrEqualTo(32768));
            AllocationSample before = meter.Read();
            for (int tick = 2; tick <= 1024; tick++)
            {
                Cycle(scheduler, tick);
                sink.Reset();
                write(sink);
                simV2();
                sim.StateHash();
            }
            AllocationSample sample = meter.Difference(before);
            TestContext.WriteLine("S39 control_bytes={0}; {1}", meter.DirectControlDelta.Bytes, sample.Describe());
            AllocationAssertions.Zero(sample);
        }

        private static void Cycle(SimScheduler scheduler, int tick)
        {
            var type = new EventTypeId(17);
            long pending = scheduler.Schedule(tick + 1, new SimEvent(type));
            scheduler.Cancel(pending);
            long due = scheduler.Schedule(tick, new SimEvent(type));
            scheduler.Schedule(tick, new SimEvent(type));
            scheduler.Schedule(tick, new SimEvent(type));
            scheduler.Advance(tick);
            scheduler.Cancel(due);
            scheduler.TryDequeue(type, out _);
            scheduler.TryDequeue(out _);
        }

        [Test]
        public void HundredThousandPendingDiagnosticKeepsOneHundredFiringsPerTick()
        {
            const int capacity = 100000;
            var scheduler = new SimScheduler(capacity);
            Action<StateHash> v2 = Writer(scheduler);
            for (int i = 0; i < capacity; i++)
                scheduler.Schedule(i / 100 + 1, new SimEvent(new EventTypeId(29), tag: i));
            scheduler.CompleteWarmup();
            var sink = new StateHash();
            scheduler.WriteState(sink);
            v2(sink);
            long v1Ticks = 0;
            long v2Ticks = 0;
            long dispatchTicks = 0;
            for (int tick = 1; tick <= 20; tick++)
            {
                long start = Stopwatch.GetTimestamp();
                scheduler.Advance(tick);
                int fired = 0;
                while (scheduler.TryDequeue(out _))
                    fired++;
                dispatchTicks += Stopwatch.GetTimestamp() - start;
                Assert.That(fired, Is.EqualTo(100));
                start = Stopwatch.GetTimestamp();
                sink.Reset();
                scheduler.WriteState(sink);
                v1Ticks += Stopwatch.GetTimestamp() - start;
                start = Stopwatch.GetTimestamp();
                for (int repeat = 0; repeat < 1000; repeat++)
                {
                    sink.Reset();
                    v2(sink);
                }
                v2Ticks += Stopwatch.GetTimestamp() - start;
            }
            Assert.That(scheduler.PendingCount, Is.EqualTo(98000));
            TestContext.WriteLine("S39 mono diagnostic: initial_pending=100000; ticks=20; fired_per_tick=100; final_pending=98000; v1_hash_us={0:F3}; v2_hash_us={1:F3}; advance_and_drain_100_us={2:F3}",
                v1Ticks * 1000000.0 / Stopwatch.Frequency / 20,
                v2Ticks * 1000000.0 / Stopwatch.Frequency / 20000,
                dispatchTicks * 1000000.0 / Stopwatch.Frequency / 20);
        }
    }
}
