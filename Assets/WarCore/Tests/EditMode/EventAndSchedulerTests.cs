using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class EventAndSchedulerTests
    {
        private static SimEvent Event(int value = 0) => new SimEvent(new EventTypeId(10), tag: value);
        [Test]
        public void RegistryIdsIgnoreRegistrationOrderAndEventsHaveNoReferenceFields()
        {
            var a = new EventTypeRegistry();
            var b = new EventTypeRegistry();
            EventTypeId alpha = a.Register("alpha");
            a.Register("beta");
            b.Register("beta");
            Assert.That(b.Register("alpha"), Is.EqualTo(alpha));
            Assert.That(a.Register("alpha"), Is.EqualTo(alpha));
            Assert.That(Fixtures.Hash(a), Is.EqualTo(Fixtures.Hash(b)));
            Assert.That(a.Register("events-dropped").Value, Is.EqualTo(EventTypeRegistry.EventsDropped));
            Assert.That(a.Register("world-change").Value, Is.EqualTo(EventTypeRegistry.WorldChange));
            RequireUnmanaged<SimEvent>();
            SimEvent world = SimEvent.WorldChangeEvent(3, SimVec3.Metres(16384, 0, 0), SimVec3.Metres(20, 2, 3), 7);
            Assert.That(world.Position.X, Is.EqualTo(SimFix.FromInt(16384)));
            Assert.That(world.Aux.X, Is.EqualTo(SimFix.FromInt(20)));
            Assert.That(world.Tag, Is.EqualTo(7));
        }

        private static void RequireUnmanaged<T>()
            where T : unmanaged
        {
        }

        [Test]
        public void ExpiredPayloadsCannotBeReadAndNoLongerAffectHash()
        {
            var a = new SimEventBuffer(2, 1, 4);
            var b = new SimEventBuffer(2, 1, 4);
            a.BeginTick(1);
            b.BeginTick(1);
            a.Publish(Event(1));
            b.Publish(Event(2));
            a.EndTick();
            b.EndTick();
            Assert.That(Fixtures.Hash(a), Is.Not.EqualTo(Fixtures.Hash(b)));
            for (int tick = 2; tick <= 3; tick++)
            {
                a.BeginTick(tick);
                b.BeginTick(tick);
                a.EndTick();
                b.EndTick();
            }

            Assert.That(a.Read(1).Length, Is.Zero);
            Assert.That(a.Read(0).Length, Is.Zero);
            Assert.That(a.Read(100).Length, Is.Zero);
            Assert.That(a.OldestAvailableSequence, Is.EqualTo(a.NextSequence));
            Assert.That(Fixtures.Hash(a), Is.EqualTo(Fixtures.Hash(b)));
        }

        [Test]
        public void EveryAttemptAndOverflowSummaryConsumeSequencesAndReaderCanCountLoss()
        {
            var buffer = new SimEventBuffer(2, 1, 2);
            buffer.CompleteWarmup();
            buffer.BeginTick(1);
            for (int i = 0; i < 5; i++)
                Assert.That(buffer.Publish(Event(i)), Is.EqualTo(i < 2));
            buffer.EndTick();
            ReadOnlySpan<SimEvent> items = buffer.Read(1);
            Assert.That(items.Length, Is.EqualTo(3));
            Assert.That(items[0].Sequence, Is.EqualTo(1));
            Assert.That(items[1].Sequence, Is.EqualTo(2));
            Assert.That(items[2].Sequence, Is.EqualTo(6));
            Assert.That(items[2].Type.Value, Is.EqualTo(EventTypeRegistry.EventsDropped));
            Assert.That(items[2].Magnitude, Is.EqualTo(SimFix.FromInt(3)));
            Assert.That(items[2].Sequence - items[1].Sequence - 1, Is.EqualTo(3));
            Assert.That(buffer.EventsDroppedThisTick, Is.EqualTo(3));
            Assert.That(buffer.EventsDroppedTotal, Is.EqualTo(3));
            buffer.BeginTick(2);
            buffer.Publish(Event());
            buffer.EndTick();
            buffer.BeginTick(3);
            buffer.EndTick();
            Assert.That(buffer.OldestAvailableSequence, Is.EqualTo(7));
            Assert.That(buffer.OldestAvailableSequence - 1 - 1, Is.EqualTo(5), "A reader last at sequence 1 missed sequences 2 through 6.");
            buffer.BeginTick(4);
            buffer.EndTick();
            Assert.That(buffer.OldestAvailableSequence, Is.EqualTo(8));
            Assert.That(buffer.EventsDroppedThisTick, Is.Zero);
            Assert.That(buffer.EventsDroppedTotal, Is.EqualTo(3));
        }

        [Test]
        public void TwoThousandEventsSurviveAfterWarmupWithZeroAllocatedBytes()
        {
            var windows = new AllocationWindow();
            do
            {
                var buffer = new SimEventBuffer(8, 1, 2000);
                buffer.BeginTick(1);
                for (int i = 0; i < 100; i++)
                    buffer.Publish(Event(i));
                buffer.EndTick();
                buffer.CompleteWarmup();
                var meter = new AllocationMeter();
                windows.Begin(meter);
                buffer.BeginTick(2);
                for (int i = 0; i < 2000; i++)
                    buffer.Publish(Event(i));
                buffer.EndTick();
                AllocationSample allocation = windows.End();
                if (!windows.NeedsRetry)
                    AllocationAssertions.Zero(allocation);
                Assert.That(buffer.Read(2).Length, Is.EqualTo(2000));
                for (int i = 0; i < 2000; i++)
                    Assert.That(buffer.Read(2)[i].Tag, Is.EqualTo(i));
                Assert.That(buffer.EventsDroppedTotal, Is.Zero);
                TestContext.WriteLine("2000 events allocation: " + allocation.Describe());
            }
            while (windows.NeedsRetry);
        }

        [Test]
        public void DueQueueSurvivesUnreadTicksAndCarriesTheCompleteTemplateEvenWithFullEvents()
        {
            var scheduler = new SimScheduler(8);
            SimEvent template = new SimEvent(new EventTypeId(20), source: new EntityId(1, 2), target: new EntityId(3, 4), position: SimVec3.Metres(1, 2, 3), aux: SimVec3.Metres(4, 5, 6), magnitude: SimFix.Ratio(3, 2), radius: SimFix.FromInt(9), tag: 17);
            long first = scheduler.Schedule(1, template);
            scheduler.Schedule(1, Event(2));
            var buffer = new SimEventBuffer(1, 1, 1);
            buffer.BeginTick(1);
            buffer.Publish(Event());
            scheduler.Advance(1, buffer);
            buffer.EndTick();
            scheduler.Advance(2);
            scheduler.Advance(3);
            Assert.That(scheduler.DueCount, Is.EqualTo(2));
            Assert.That(buffer.EventsDroppedTotal, Is.EqualTo(2));
            Assert.That(scheduler.TryDequeue(out ScheduledTask due), Is.True);
            Assert.That(due.Id, Is.EqualTo(first));
            Assert.That(Fixtures.Hash(due.Event), Is.EqualTo(Fixtures.Hash(template.At(1))));
            Assert.That(scheduler.TryDequeue(out due), Is.True);
            Assert.That(due.Event.Tag, Is.EqualTo(2));
            Assert.That(scheduler.TryDequeue(out _), Is.False);
        }

        [Test]
        public void HeapMatchesSortedOracleUnderRandomInsertionAndCancellation()
        {
            var random = new Random(7281);
            var scheduler = new SimScheduler(4000);
            var expected = new List<(long Id, int Tick, int Value)>();
            for (int i = 0; i < 3000; i++)
            {
                int tick = random.Next(1, 100);
                long id = scheduler.Schedule(tick, Event(i));
                if (i % 7 == 0)
                    Assert.That(scheduler.Cancel(id), Is.True);
                else
                    expected.Add((id, tick, i));
            }

            expected.Sort((a, b) => a.Tick != b.Tick ? a.Tick.CompareTo(b.Tick) : a.Id.CompareTo(b.Id));
            int index = 0;
            scheduler.CompleteWarmup();
            for (int tick = 1; tick <= 100; tick++)
            {
                scheduler.Advance(tick);
                while (scheduler.TryDequeue(out ScheduledTask task))
                {
                    Assert.That(task.Id, Is.EqualTo(expected[index].Id));
                    Assert.That(task.Event.Tick, Is.EqualTo(expected[index].Tick));
                    Assert.That(task.Event.Tag, Is.EqualTo(expected[index].Value));
                    index++;
                }
            }

            Assert.That(index, Is.EqualTo(expected.Count));
        }

        [Test]
        public void TypedHandlesRefsEnumerationAndFreeSlotsAreSafeAndAllocationFree()
        {
            var windows = new AllocationWindow();
            do
            {
                var store = new EntityStore(16);
                ComponentType<Health> health = store.Register<Health>("health");
                var foreign = new EntityStore().Register<Health>("health");
                EntityId id = store.Create();
                store.Set(health, id, new Health{Value = 2});
                store.Get(health, id).Value = 7;
                Assert.That(store.Get(health, id).Value, Is.EqualTo(7));
                Assert.Throws<InvalidOperationException>(() => store.Set(foreign, id, default));
                Assert.Throws<InvalidOperationException>(() => store.Set(default(ComponentType<Health>), id, default));
                store.CompleteWarmup();
                var meter = new AllocationMeter();
                windows.Begin(meter);
                int sum = 0;
                for (int i = 0; i < 1000; i++)
                {
                    foreach (EntityId entity in store)
                        sum += store.Get(health, entity).Value;
                    store.Destroy(id);
                    store.RecycleDestroyed();
                    id = store.Create();
                    store.Set(health, id, new Health{Value = 7});
                }

                AllocationSample allocation = windows.End();
                if (!windows.NeedsRetry)
                    AllocationAssertions.Zero(allocation);
                Assert.That(sum, Is.EqualTo(7000));
                Assert.Throws<InvalidOperationException>(() => store.Reserve(100));
                var full = new SimScheduler(1);
                full.Schedule(1, Event());
                full.CompleteWarmup();
                full.Advance(1);
                Assert.Throws<InvalidOperationException>(() => full.Schedule(2, Event()));
                Assert.That(full.TryDequeue(out _), Is.True);
                Assert.DoesNotThrow(() => full.Schedule(2, Event()));
            }
            while (windows.NeedsRetry);
        }
    }
}
