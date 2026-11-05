using NUnit.Framework;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    public sealed class CoreSnapshotTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void G12_WholeMatchKnownVectorContinuesFrom510WithPendingTimerAtEveryTick(bool freshInstance)
        {
            Simulation uninterrupted = Fixtures.Create(123);
            for (int tick = 1; tick <= 510; tick++)
                uninterrupted.Step(Fixtures.Input(tick));
            Assert.That(uninterrupted.Scheduler.PendingCount, Is.GreaterThan(0));
            byte[] snapshot = uninterrupted.SaveSnapshot();
            ulong savedHash = uninterrupted.StateHash();
            var expected = new ulong[490];
            for (int tick = 511; tick <= 1000; tick++)
            {
                uninterrupted.Step(Fixtures.Input(tick));
                expected[tick - 511] = uninterrupted.StateHash();
            }

            Assert.That(expected[489], Is.EqualTo(0x8166D6848CDEB66CUL));
            Simulation restored = freshInstance ? Fixtures.Create(999) : uninterrupted;
            RandomStreamId combat = restored.Rng.Stream("combat");
            restored.RestoreSnapshot(snapshot);
            Assert.That(restored.Rng.Stream("combat"), Is.EqualTo(combat));
            Assert.That(restored.Clock.Tick, Is.EqualTo(510));
            Assert.That(restored.StateHash(), Is.EqualTo(savedHash));
            Assert.That(restored.SaveSnapshot(), Is.EqualTo(snapshot));
            for (int tick = 511; tick <= 1000; tick++)
            {
                restored.Step(Fixtures.Input(tick));
                Assert.That(restored.StateHash(), Is.EqualTo(expected[tick - 511]), "Restored tick " + tick);
            }

            Assert.That(restored.StateHash(), Is.EqualTo(0x8166D6848CDEB66CUL));
            TestContext.WriteLine("G12 whole-match: save tick=510, pending>0, fresh={0}, 490 per-tick hashes equal; final=0x{1:X16}.", freshInstance, restored.StateHash());
        }

        [TestCase(false)]
        [TestCase(true)]
        public void G12_A3NonemptyVectorRestoresOriginalIdsAndContinuesThroughDueConsumption(bool reverse)
        {
            Simulation baseline = SnapshotFixtures.A3(reverse);
            Simulation restored = SnapshotFixtures.A3(reverse);
            Assert.That(Fixtures.Hash(restored.Scheduler), Is.EqualTo(0x22CB551F19EC00FEUL));
            byte[] snapshot = restored.SaveSnapshot();
            long extra = restored.Scheduler.Schedule(9, SnapshotFixtures.Payload(99));
            Assert.That(extra, Is.EqualTo(6));
            restored.Step(default);
            Assert.That(restored.Events.Read(2).Length, Is.EqualTo(2));
            restored.RestoreSnapshot(snapshot);
            Assert.That(restored.Events.Read(2).Length, Is.Zero);
            Assert.That(restored.Scheduler.Cancel(extra), Is.False);
            Assert.That(Fixtures.Hash(restored.Scheduler), Is.EqualTo(0x22CB551F19EC00FEUL));
            Assert.That(restored.SaveSnapshot(), Is.EqualTo(snapshot));
            Assert.That(baseline.Scheduler.Schedule(5, SnapshotFixtures.Payload(6)), Is.EqualTo(6));
            Assert.That(restored.Scheduler.Schedule(5, SnapshotFixtures.Payload(6)), Is.EqualTo(6));
            long[] expectedIds = { 2, 5, 4, 6 };
            int consumed = 0;
            for (int tick = 2; tick <= 8; tick++)
            {
                baseline.Step(default);
                restored.Step(default);
                bool expected = baseline.Scheduler.TryDequeue(out ScheduledTask a);
                Assert.That(restored.Scheduler.TryDequeue(out ScheduledTask b), Is.EqualTo(expected));
                if (expected)
                {
                    Assert.That(b.Id, Is.EqualTo(expectedIds[consumed++]));
                    Assert.That(b.Id, Is.EqualTo(a.Id));
                    Assert.That(b.Event, Is.EqualTo(a.Event));
                }

                Assert.That(restored.StateHash(), Is.EqualTo(baseline.StateHash()), "A3 tick " + tick);
                Assert.That(Fixtures.Hash(restored.Scheduler), Is.EqualTo(Fixtures.Hash(baseline.Scheduler)));
            }

            Assert.That(consumed, Is.EqualTo(4));
            TestContext.WriteLine("G12 A3: save tick=1, pending=3, hash=0x22CB551F19EC00FE; reverse={0}; ticks 2..8 match, ids 2/5/4/6 preserved, post-save timer/event removed.", reverse);
        }

        [Test]
        public void G12_RestoresDeadSlotsGenerationsComponentsRngQueuesWindowInputModeAndSystem()
        {
            var mode = new SnapshotMode();
            var sim = new Simulation(71, mode, "snapshot-mode", config: new SimConfig(3, 2, 2, 8, 8));
            // This persistence test intentionally retains overdue work across several ticks.
            sim.Scheduler.DiagnoseUnconsumedTasks = false;
            var output = new SnapshotOutput();
            sim.Register("output", output);
            var health = sim.Entities.Register<Health>("health", new HealthSerializer());
            var position = sim.Entities.Register<Position>("position", new PositionSerializer());
            var collider = sim.Entities.Register<Collider>("collider", new ColliderSerializer());
            var squad = sim.Entities.Register<SquadId>("squad", new SquadIdSerializer());
            EntityId a = sim.Entities.Create();
            EntityId dead = sim.Entities.Create();
            EntityId recycled = sim.Entities.Create();
            sim.Entities.Destroy(recycled);
            sim.CompleteSpatialBoundary();
            recycled = sim.Entities.Create();
            EntityId otherDead = sim.Entities.Create();
            sim.Entities.Destroy(dead);
            sim.Entities.Destroy(otherDead);
            sim.Entities.Set(health, a, new Health { Value = 123 });
            sim.Entities.Set(position, recycled, new Position(SimVec3.Metres(7, 8, 9)));
            sim.Entities.Set(collider, recycled, new Collider(SimFix.FromInt(1), SimFix.FromInt(3)));
            sim.Entities.Set(squad, recycled, new SquadId(31));
            RandomStreamId alpha = sim.Rng.Stream("alpha");
            RandomStreamId beta = sim.Rng.Stream("beta");
            sim.Rng.NextUInt64(alpha);
            sim.Rng.NextUInt64(beta);
            sim.Rng.NextUInt64(beta);
            sim.Scheduler.Schedule(1, SnapshotFixtures.Payload(1));
            sim.Scheduler.Schedule(1, SnapshotFixtures.Payload(2));
            sim.Scheduler.Schedule(2, SnapshotFixtures.Payload(3));
            long pending = sim.Scheduler.Schedule(6, SnapshotFixtures.Payload(4));
            sim.Step(default);
            Assert.That(sim.Scheduler.TryDequeue(out ScheduledTask first), Is.True);
            Assert.That(first.Id, Is.EqualTo(1));
            Assert.That(sim.Scheduler.Cancel(2), Is.True);
            var input = new InputFrame(SimVec3.Metres(1, 2, 3), SimVec3.Metres(-1, 0, 1), true, true, true, 7,
                new SquadCommand(SquadCommandKind.MoveTo, 3, SimVec3.Metres(4, 5, 6), recycled));
            for (int tick = 2; tick <= 5; tick++)
                sim.Step(input);
            Assert.That(sim.Scheduler.DueCount, Is.EqualTo(1));
            Assert.That(sim.Events.EventsDroppedTotal, Is.GreaterThan(0));
            byte[] snapshot = sim.SaveSnapshot();
            ulong hash = sim.StateHash();
            long sequence = sim.Events.NextSequence;
            long dropped = sim.Events.EventsDroppedTotal;
            SimEvent[] events = sim.Events.Read(5).ToArray();
            ulong nextAlpha = sim.Rng.NextUInt64(alpha);
            ulong nextBeta = sim.Rng.NextUInt64(beta);
            sim.Rng.NextUInt64(sim.Rng.Stream("after-save"));
            sim.Entities.Create();
            sim.Entities.Create();
            sim.Entities.Destroy(a);
            sim.Entities.Remove(collider, recycled);
            sim.Scheduler.Cancel(pending);
            long added = sim.Scheduler.Schedule(99, SnapshotFixtures.Payload(99));
            sim.Step(default);
            mode.FinishAt = 6;
            sim.RestoreSnapshot(snapshot);
            Assert.That(sim.StateHash(), Is.EqualTo(hash));
            Assert.That(sim.SaveSnapshot(), Is.EqualTo(snapshot));
            Assert.That(sim.Context.Input, Is.EqualTo(input));
            Assert.That(sim.Events.NextSequence, Is.EqualTo(sequence));
            Assert.That(sim.Events.EventsDroppedTotal, Is.EqualTo(dropped));
            Assert.That(sim.Events.Read(5).ToArray(), Is.EqualTo(events));
            Assert.That(sim.Events.Read(2).Length, Is.Zero);
            Assert.That(sim.Events.Read(6).Length, Is.Zero);
            Assert.That(sim.Scheduler.Cancel(added), Is.False);
            Assert.That(sim.Scheduler.TryDequeue(out ScheduledTask due), Is.True);
            Assert.That(due.Id, Is.EqualTo(3));
            Assert.That(due.Event, Is.EqualTo(SnapshotFixtures.Payload(3).At(2)));
            Assert.That(sim.Entities.IsAlive(a), Is.True);
            Assert.That(sim.Entities.IsAlive(dead), Is.False);
            Assert.That(sim.Entities.IsAlive(new EntityId(recycled.Id, 1)), Is.False);
            Assert.That(sim.Entities.Get(health, a).Value, Is.EqualTo(123));
            Assert.That(sim.Entities.Get(position, recycled).Value, Is.EqualTo(SimVec3.Metres(7, 8, 9)));
            Assert.That(sim.Entities.Get(collider, recycled).Height, Is.EqualTo(SimFix.FromInt(3)));
            Assert.That(sim.Entities.Get(squad, recycled).Value, Is.EqualTo(31));
            Assert.That(sim.Entities.TryGet(health, recycled, out _), Is.False);
            Assert.That(sim.Entities.Create(), Is.EqualTo(new EntityId(1, 2)));
            Assert.That(sim.Entities.Create(), Is.EqualTo(new EntityId(3, 2)));
            Assert.That(sim.Rng.Stream("alpha"), Is.EqualTo(alpha));
            Assert.That(sim.Rng.NextUInt64(alpha), Is.EqualTo(nextAlpha));
            Assert.That(sim.Rng.NextUInt64(beta), Is.EqualTo(nextBeta));
            Assert.That(sim.Rng.NextUInt64(sim.Rng.Stream("after-save")), Is.EqualTo(new SimRandom(71).NextUInt64(new RandomStreamId("after-save"))));
            Assert.That(mode.Starts, Is.EqualTo(1));
            Assert.That(mode.Steps, Is.EqualTo(5));
            Assert.That(mode.IsFinished, Is.False);
            Assert.That(output.Count, Is.EqualTo(5));
            sim.Step(default);
            Assert.That(mode.Starts, Is.EqualTo(1));
            Assert.That(mode.Steps, Is.EqualTo(6));
            Assert.That(sim.Scheduler.TryDequeue(out due), Is.True);
            Assert.That(due.Id, Is.EqualTo(pending));
        }

        [Test]
        public void G12_InitialAndFinishedBoundariesPreserveLifecycleWithoutStartingOnRestore()
        {
            var mode = new SnapshotMode { FinishAt = 2 };
            var sim = new Simulation(1, mode, config: new SimConfig(1, 1, 1));
            byte[] initial = sim.SaveSnapshot();
            sim.Step(default);
            sim.RestoreSnapshot(initial);
            Assert.That(mode.Starts, Is.Zero);
            Assert.That(sim.Clock.Tick, Is.Zero);
            sim.Entities.Reserve(8);
            sim.Step(default);
            sim.Step(default);
            byte[] finished = sim.SaveSnapshot();
            var targetMode = new SnapshotMode();
            var target = new Simulation(999, targetMode, config: new SimConfig(1, 1, 1));
            target.RestoreSnapshot(finished);
            Assert.That(target.IsFinished, Is.True);
            Assert.That(targetMode.Starts, Is.EqualTo(1));
            Assert.That(target.StateHash(), Is.EqualTo(sim.StateHash()));
            Assert.That(target.Entities.Capacity, Is.EqualTo(8));
            Assert.Throws<System.InvalidOperationException>(() => target.Step(default));
        }
    }
}
