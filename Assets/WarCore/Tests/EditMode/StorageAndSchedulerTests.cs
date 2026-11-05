using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    public sealed class StorageAndSchedulerTests
    {
        [Test]
        public void ReusedEntityRejectsStaleHandlesAndClearsEveryComponent()
        {
            var store = new EntityStore();
            store.Register<Position>("position");
            store.Register<Facing>("facing");
            store.Register<Health>("health");
            EntityId old = store.Create();
            store.Set(old, new Position(SimVec3.Metres(8, 0, 0)));
            store.Set(old, new Facing());
            store.Set(old, new Health{Value = 20});
            store.Destroy(old);
            store.RecycleDestroyed();
            EntityId fresh = store.Create();
            Assert.That(fresh.Id, Is.EqualTo(old.Id));
            Assert.That(fresh.Generation, Is.EqualTo(old.Generation + 1));
            Assert.That(store.IsAlive(old), Is.False);
            Assert.That(store.IsAlive(default), Is.False);
            Assert.Throws<InvalidOperationException>(() => store.Set(old, new Health()));
            Assert.Throws<InvalidOperationException>(() => store.Get<Position>(old));
            Assert.Throws<InvalidOperationException>(() => store.Destroy(old));
            Assert.That(store.TryGet<Position>(fresh, out _), Is.False);
            Assert.That(store.TryGet<Facing>(fresh, out _), Is.False);
            Assert.That(store.TryGet<Health>(fresh, out _), Is.False);
        }

        [Test]
        public void ArraysGrowAndLateComponentRegistrationAndValueSnapshotsRemainSafe()
        {
            var store = new EntityStore();
            store.Register<Position>("position");
            var ids = new List<EntityId>();
            for (int i = 0; i < 40; i++)
            {
                var id = store.Create();
                ids.Add(id);
                store.Set(id, new Position(SimVec3.Metres(i, 0, 0)));
            }

            store.Register<Health>("health");
            foreach (var id in ids)
            {
                Assert.That(store.Get<Position>(id).Value.X.Raw / SimFix.Scale, Is.EqualTo(id.Id));
                store.Set(id, new Health{Value = 7});
            }

            var snapshot = store.SnapshotEntitiesForTests();
            var copy = store.Get<Position>(ids[0]);
            copy.Value = SimVec3.Metres(100, 0, 0);
            Assert.That(store.Get<Position>(ids[0]).Value.X.Raw / SimFix.Scale, Is.Zero);
            store.Destroy(ids[3]);
            store.Destroy(ids[1]);
            store.RecycleDestroyed();
            Assert.That(snapshot.Count, Is.EqualTo(40));
            Assert.That(store.Create().Id, Is.EqualTo(1));
            Assert.That(store.Create().Id, Is.EqualTo(3));
            store.Remove<Health>(ids[0]);
            Assert.That(store.TryGet<Health>(ids[0], out _), Is.False);
            Assert.Throws<InvalidOperationException>(() => store.Register<Health>("other"));
            Assert.Throws<InvalidOperationException>(() => store.Register<Facing>("position"));
            Assert.Throws<InvalidOperationException>(() => store.Get<Facing>(ids[0]));
        }

        [Test]
        public void ComponentRegistrationOrderDoesNotAffectHashButValuesPresenceAndGenerationsDo()
        {
            var a = new EntityStore();
            var b = new EntityStore();
            a.Register<Position>("position");
            a.Register<Health>("health");
            b.Register<Health>("health");
            b.Register<Position>("position");
            var id = a.Create();
            b.Create();
            Assert.That(Fixtures.Hash(a), Is.EqualTo(Fixtures.Hash(b)));
            b.Set(id, new Position());
            Assert.That(Fixtures.Hash(a), Is.Not.EqualTo(Fixtures.Hash(b)));
            a.Set(id, new Position());
            Assert.That(Fixtures.Hash(a), Is.EqualTo(Fixtures.Hash(b)));
            b.Set(id, new Position(SimVec3.Metres(0, 1, 0)));
            Assert.That(Fixtures.Hash(a), Is.Not.EqualTo(Fixtures.Hash(b)));
            b.Remove<Position>(id);
            a.Remove<Position>(id);
            b.Destroy(id);
            b.RecycleDestroyed();
            b.Create();
            Assert.That(Fixtures.Hash(a), Is.Not.EqualTo(Fixtures.Hash(b)));
        }

        [Test]
        public void TimersFireAtExactTickInCreationOrderAndCancellationIsIdempotent()
        {
            var sim = Fixtures.Empty(1);
            sim.Scheduler.Schedule(3, new SimEvent(Fixtures.Type("later"), tag: 99));
            sim.Scheduler.Schedule(2, new SimEvent(Fixtures.Type("timer"), tag: 1));
            long canceled = sim.Scheduler.Schedule(2, new SimEvent(Fixtures.Type("timer"), tag: 2));
            sim.Scheduler.Schedule(2, new SimEvent(Fixtures.Type("timer"), tag: 3));
            Assert.That(sim.Scheduler.Cancel(canceled), Is.True);
            Assert.That(sim.Scheduler.Cancel(canceled), Is.False);
            sim.Step(default);
            Assert.That(sim.Events.Read(1).Length, Is.Zero);
            sim.Step(default);
            var events = sim.Events.Read(2).ToArray();
            Assert.That(events.Length, Is.EqualTo(2));
            Assert.That(events[0].Tag, Is.EqualTo(1));
            Assert.That(events[1].Tag, Is.EqualTo(3));
            Assert.That(events[0].Tick, Is.EqualTo(2));
            // Consume authoritative work; this test observes mirrors, not an intentionally abandoned queue.
            Assert.That(sim.Scheduler.TryDequeue(out ScheduledTask first), Is.True);
            Assert.That(first.Event.Tag, Is.EqualTo(1));
            Assert.That(sim.Scheduler.TryDequeue(out ScheduledTask third), Is.True);
            Assert.That(third.Event.Tag, Is.EqualTo(3));
            sim.Step(default);
            Assert.That(sim.Events.Read(3)[0].Tag, Is.EqualTo(99));
            sim.Step(default);
            Assert.That(sim.Events.Read(4).Length, Is.Zero);
            Assert.That(events.Length, Is.EqualTo(2));
            Assert.Throws<ArgumentOutOfRangeException>(() => sim.Scheduler.Schedule(4, new SimEvent(Fixtures.Type("past"))));
            Assert.Throws<ArgumentOutOfRangeException>(() => sim.Scheduler.Schedule(0, new SimEvent(Fixtures.Type("past"))));
        }

        [Test]
        public void SchedulerHashIncludesDeadlinePayloadCreationSequenceAndCancellation()
        {
            var a = Fixtures.Empty(1);
            var b = Fixtures.Empty(1);
            ulong initial = a.StateHash();
            long job = a.Scheduler.Schedule(5, new SimEvent(Fixtures.Type("timer"), tag: 1));
            Assert.That(a.StateHash(), Is.Not.EqualTo(initial));
            b.Scheduler.Schedule(6, new SimEvent(Fixtures.Type("timer"), tag: 1));
            Assert.That(a.StateHash(), Is.Not.EqualTo(b.StateHash()));
            var c = Fixtures.Empty(1);
            c.Scheduler.Schedule(5, new SimEvent(Fixtures.Type("timer"), tag: 2));
            Assert.That(a.StateHash(), Is.Not.EqualTo(c.StateHash()));
            ulong pending = a.StateHash();
            a.Scheduler.Cancel(job);
            Assert.That(a.StateHash(), Is.Not.EqualTo(pending));
            Assert.That(a.StateHash(), Is.Not.EqualTo(initial));
            var d = Fixtures.Empty(1);
            var e = Fixtures.Empty(1);
            d.Scheduler.Schedule(3, new SimEvent(Fixtures.Type("a")));
            d.Scheduler.Schedule(3, new SimEvent(Fixtures.Type("b")));
            e.Scheduler.Schedule(3, new SimEvent(Fixtures.Type("b")));
            e.Scheduler.Schedule(3, new SimEvent(Fixtures.Type("a")));
            Assert.That(d.StateHash(), Is.Not.EqualTo(e.StateHash()));
        }

        [Test]
        public void EventViewsAreImmutableAndReadingDoesNotChangeState()
        {
            var sim = Fixtures.Empty(1);
            sim.Scheduler.Schedule(1, new SimEvent(Fixtures.Type("event")));
            sim.Step(default);
            ulong hash = sim.StateHash();
            var view = sim.Events.Read(1);
            Assert.That(typeof(ISimEventReader).GetMethod("Read").ReturnType, Is.EqualTo(typeof(ReadOnlySpan<SimEvent>)));
            SimEvent copy = view[0];
            copy = new SimEvent(Fixtures.Type("changed"), 1);
            Assert.That(sim.Events.Read(1)[0].Type, Is.EqualTo(Fixtures.Type("event")));
            Assert.That(sim.Events.Read(99).Length, Is.Zero);
            Assert.That(sim.StateHash(), Is.EqualTo(hash));
            Assert.Throws<InvalidOperationException>(() => sim.Context.Emit(new SimEvent(Fixtures.Type("outside"))));
        }

        [Test]
        public void HashCoversInputRngSystemStateAndModeState()
        {
            var a = Fixtures.Empty(1);
            var b = Fixtures.Empty(1);
            a.Step(default);
            b.Step(new InputFrame(default, default, block: true));
            Assert.That(a.StateHash(), Is.Not.EqualTo(b.StateHash()));
            var c = Fixtures.Empty(1);
            ulong hash = c.StateHash();
            c.Rng.Stream("combat");
            Assert.That(c.StateHash(), Is.Not.EqualTo(hash));
            hash = c.StateHash();
            c.Rng.NextUInt64(c.Rng.Stream("combat"));
            Assert.That(c.StateHash(), Is.Not.EqualTo(hash));
            var pulse = new PulseSystem();
            c.Register("pulse", pulse);
            hash = c.StateHash();
            pulse.Step(c.Context);
            Assert.That(c.StateHash(), Is.Not.EqualTo(hash));
            var mode = new TestMode();
            var d = new Simulation(1, mode, "test-mode");
            hash = d.StateHash();
            mode.Counter++;
            Assert.That(d.StateHash(), Is.Not.EqualTo(hash));
        }

        private sealed class TestMode : IMatchMode
        {
            public int Counter;
            public int Starts;
            public bool IsFinished => Counter >= 2;
            public void Start(SimContext context)
            {
                Starts++;
            }

            public void Step(SimContext context)
            {
                Counter++;
            }

            public void WriteState(StateHash hash)
            {
                hash.Add(Counter);
                hash.Add(Starts);
            }
        }

        [Test]
        public void MatchModeStartsOnceAndStopsAtCompletion()
        {
            var mode = new TestMode();
            var sim = new Simulation(1, mode, "test-mode");
            sim.Step(default);
            sim.Step(default);
            Assert.That(mode.Starts, Is.EqualTo(1));
            Assert.That(mode.Counter, Is.EqualTo(2));
            Assert.That(sim.IsFinished, Is.True);
            Assert.Throws<InvalidOperationException>(() => sim.Step(default));
            Assert.That(sim.Clock.Tick, Is.EqualTo(2));
        }
    }
}
