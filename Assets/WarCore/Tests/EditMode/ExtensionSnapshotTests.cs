using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class ExtensionSnapshotTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void CombinedContinuationRestoresAllExtensionStateAndFutureBehavior(bool fresh)
        {
            var baseline = new ExtensionFixture();
            var subject = new ExtensionFixture();
            // These snapshots deliberately include work consumed more than one tick late.
            baseline.P.Sim.Scheduler.DiagnoseUnconsumedTasks = false;
            subject.P.Sim.Scheduler.DiagnoseUnconsumedTasks = false;
            Prepare(baseline);
            Prepare(subject);
            byte[] snapshot = subject.P.Sim.SaveSnapshot();
            ulong hash = subject.P.Sim.StateHash();
            Assert.That(subject.P.Sim.Entities.Get(subject.P.Postures, subject.P.Unit).IsTransitioning, Is.True);
            Assert.That(subject.P.Sim.Scheduler.DueCount, Is.EqualTo(2));
            Assert.That(subject.P.Sim.Entities.Get(subject.Healths, subject.Second).Collapsed, Is.True);
            subject.P.Sim.Scheduler.Schedule(100, new SimEvent(new EventTypeId(999)));
            subject.P.Sim.Entities.Remove(subject.Seats, subject.Second);
            subject.P.Sim.Entities.Set(subject.Masses, subject.Second, new MassClass(99));
            subject.P.Sim.Entities.Set(subject.Containers, subject.P.Unit, new Container(1));
            subject.P.Sim.Entities.Set(subject.Occupants, subject.P.Unit, default);
            subject.P.Sim.Step(default);
            if (fresh)
            {
                subject = new ExtensionFixture();
                subject.P.Sim.Scheduler.DiagnoseUnconsumedTasks = false;
            }
            subject.P.Sim.RestoreSnapshot(snapshot);
            Assert.That(subject.P.Sim.StateHash(), Is.EqualTo(hash));
            Assert.That(subject.P.Sim.SaveSnapshot(), Is.EqualTo(snapshot));
            Assert.That(subject.Control.Current, Is.EqualTo(subject.Second));
            Assert.That(subject.P.Sim.Entities.Get(subject.Seats, subject.Second), Is.EqualTo(new Seat(subject.Second, 2, subject.P.Unit)));
            Assert.That(subject.P.Sim.Entities.Get(subject.Occupants, subject.P.Unit).Seat, Is.EqualTo(subject.Second));
            Assert.That(subject.P.Sim.Entities.Get(subject.Containers, subject.P.Unit).Count, Is.EqualTo(3));
            Assert.That(subject.P.Sim.Entities.Get(subject.Masses, subject.Second).Value, Is.EqualTo(2));
            Assert.That(subject.P.Sim.Entities.Get(subject.Healths, subject.Second).Health, Is.EqualTo(SimFix.FromInt(4)));
            Assert.That(subject.Triggers.Get(0).Fired, Is.True);
            Assert.That(subject.Triggers.Get(1).Fired, Is.False);
            Assert.That(subject.Action.Calls, Is.EqualTo(1));
            Assert.That(subject.P.Sim.Entities.IsAlive(new EntityId(subject.Recycled.Id, 1)), Is.False);
            EntityId reuseA = baseline.P.Add(9, 0, 1);
            EntityId reuseB = subject.P.Add(9, 0, 1);
            Assert.That(reuseB, Is.EqualTo(new EntityId(subject.Free.Id, 2)));
            Assert.That(reuseB, Is.EqualTo(reuseA));
            long nextA = baseline.P.Sim.Scheduler.Schedule(7, new SimEvent(new EventTypeId(833), tag: 77));
            long nextB = subject.P.Sim.Scheduler.Schedule(7, new SimEvent(new EventTypeId(833), tag: 77));
            Assert.That(nextB, Is.EqualTo(5));
            Assert.That(nextB, Is.EqualTo(nextA));
            for (int tick = 3; tick <= 12; tick++)
            {
                if (tick == 4)
                {
                    baseline.P.Sim.Entities.Destroy(baseline.Recycled);
                    subject.P.Sim.Entities.Destroy(subject.Recycled);
                }

                Assert.That(subject.P.Sim.Rng.NextUInt64(subject.P.Sim.Rng.Stream("combined")), Is.EqualTo(baseline.P.Sim.Rng.NextUInt64(baseline.P.Sim.Rng.Stream("combined"))));
                var input = new InputFrame(default, SimVec3.Metres(1, 0, 0), skillSlot: tick);
                baseline.P.Sim.Step(input);
                subject.P.Sim.Step(input);
                if (tick == 6 || tick == 8)
                    CompareDue(baseline, subject);
                Assert.That(subject.P.Sim.StateHash(), Is.EqualTo(baseline.P.Sim.StateHash()), "combined tick " + tick);
                Assert.That(subject.P.Sim.Entities.Get(subject.P.Postures, subject.P.Unit).CurrentId, Is.EqualTo(tick < 5 ? 1 : 3));
                Assert.That(ExtensionContractTests.Count(subject.P.Sim.Events, tick, EventTypeRegistry.WorldChange), Is.Zero);
                PostureTests.CompareAll(subject.P.Grid, subject.P.Oracle(true), SimVec3.Metres(-4, 1, 0));
            }

            Assert.That(subject.P.Sim.Entities.Get(subject.Containers, subject.P.Unit).Count, Is.EqualTo(4));
            Assert.That(subject.P.Sim.Entities.Get(subject.Containers, subject.Second).Count, Is.EqualTo(7));
            Assert.That(subject.Action.Calls, Is.EqualTo(3));
            Assert.That(subject.P.Sim.Events.EventsDroppedTotal, Is.Zero);
            TestContext.WriteLine("Combined restore fresh={0}: save tick=2; posture due=5/id=1, collapsed latch, fired/pending triggers, reused generation=2, two unconsumed due tasks; ticks 3..12 hash/query/RNG match; next timer id=5; no collapse replay.", fresh);
        }

        private static void Prepare(ExtensionFixture f)
        {
            Assert.That(f.P.System.BeginTransition(f.P.Sim.Context, f.P.Unit, 3, 5), Is.EqualTo(1));
            f.P.Sim.Scheduler.Schedule(1, new SimEvent(new EventTypeId(833), tag: 21));
            f.P.Sim.Scheduler.Schedule(2, new SimEvent(new EventTypeId(833), tag: 22));
            f.P.Sim.Scheduler.Schedule(8, new SimEvent(new EventTypeId(833), tag: 28));
            f.P.Sim.Rng.NextUInt64(f.P.Sim.Rng.Stream("combined"));
            f.P.Sim.Step(new InputFrame(default, default, skillSlot: 1));
            f.P.Sim.Step(new InputFrame(default, default, skillSlot: 2));
        }

        private static void CompareDue(ExtensionFixture a, ExtensionFixture b)
        {
            int count = 0;
            while (a.P.Sim.Scheduler.TryDequeue(out ScheduledTask expected))
            {
                Assert.That(b.P.Sim.Scheduler.TryDequeue(out ScheduledTask actual), Is.True);
                Assert.That(actual.Id, Is.EqualTo(expected.Id));
                Assert.That(actual.Event, Is.EqualTo(expected.Event));
                if (count == 0)
                    Assert.That(actual.Id, Is.EqualTo(a.P.Sim.Clock.Tick == 6 ? 2 : 5));
                count++;
            }

            Assert.That(count, Is.EqualTo(2));
            Assert.That(b.P.Sim.Scheduler.TryDequeue(out _), Is.False);
        }

        [Test]
        public void EveryNewComponentFieldAffectsHashAndExplicitCodecRoundTrips()
        {
            var a = new EntityId(1, 2);
            var b = new EntityId(3, 4);
            Check(new PostureSerializer(), new Posture(1, 2, 3, 4), new Posture(2, 1, 3, 4),
                new Posture(1, 3, 3, 4), new Posture(1, 2, 4, 4), new Posture(1, 2, 3, 5));
            Check(new SeatSerializer(), new Seat(a, 3, b), new Seat(b, 3, b), new Seat(a, 4, b), new Seat(a, 3, a));
            Check(new OccupantSerializer(), new Occupant(a), new Occupant(b));
            Check(new ContainerSerializer(), new Container(10, 2), new Container(11, 2), new Container(10, 3));
            Check(new MassClassSerializer(), new MassClass(1), new MassClass(2));
            Check(new StructureHealthSerializer(), new StructureHealth(SimFix.FromInt(4), SimFix.FromInt(5)),
                new StructureHealth(SimFix.FromInt(3), SimFix.FromInt(5)),
                new StructureHealth(SimFix.FromInt(4), SimFix.FromInt(6)),
                new StructureHealth(SimFix.FromInt(4), SimFix.FromInt(5), true));
        }

        private static void Check<T>(IComponentSerializer<T> codec, T baseline, params T[] alternatives) where T : unmanaged, IStateHash
        {
            RoundTrip(codec, baseline);
            foreach (T alternative in alternatives)
            {
                Assert.That(Fixtures.Hash(alternative), Is.Not.EqualTo(Fixtures.Hash(baseline)), typeof(T).Name);
                RoundTrip(codec, alternative);
            }
        }

        private static void RoundTrip<T>(IComponentSerializer<T> codec, T value) where T : unmanaged, IStateHash
        {
            var writer = new SimStateWriter();
            codec.Write(writer, in value);
            var reader = new SimStateReader(writer.ToArray());
            Assert.That(codec.Read(reader), Is.EqualTo(value));
            reader.End();
        }
    }
}
