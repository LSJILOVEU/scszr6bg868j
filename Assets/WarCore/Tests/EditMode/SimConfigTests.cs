using System;
using System.Runtime.InteropServices;
using NUnit.Framework;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    public sealed class SimConfigTests
    {
        [Test]
        public void DefaultsUse16384AndEstimateIncludesOneSummarySlotPerTick()
        {
            var config = new SimConfig();
            var sim = new Simulation(1);
            Assert.That(config.EventRetainedTicks, Is.EqualTo(8));
            Assert.That(config.EventInitialCapacity, Is.EqualTo(4096));
            Assert.That(config.EventHardLimit, Is.EqualTo(16384));
            Assert.That(sim.Config.EventHardLimit, Is.EqualTo(16384));
            Assert.That(new SimEventBuffer().HardLimit, Is.EqualTo(16384));
            Assert.That(Marshal.SizeOf<SimEvent>(), Is.EqualTo(128));
            Assert.That(config.EstimateEventPayloadBytes(false), Is.EqualTo(8L * (4096 + 1) * 128));
            Assert.That(config.EstimateEventPayloadBytes(true), Is.EqualTo(8L * (16384 + 1) * 128));
            Assert.That(sim.Entities.Capacity, Is.EqualTo(4));
            Assert.That(sim.Scheduler.Capacity, Is.EqualTo(1024));
        }

        [TestCase(0, 1, 1, 1, 1)]
        [TestCase(1, 0, 1, 1, 1)]
        [TestCase(1, 2, 1, 1, 1)]
        [TestCase(1, 1, 1, 0, 1)]
        [TestCase(1, 1, 1, 1, 0)]
        [TestCase(-1, 1, 1, 1, 1)]
        [TestCase(1, 1, int.MaxValue, 1, 1)]
        public void InvalidConfigurationFailsBeforeAllocation(int retained, int initial, int limit, int entities, int timers)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new SimConfig(retained, initial, limit, entities, timers));
        }

        [Test]
        public void MemoryEstimateOverflowHasAnExplicitSimulationException()
        {
            Assert.Throws<SimBoundaryException>(() => new SimConfig(int.MaxValue, 1, int.MaxValue - 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SimEventBuffer(1, 1, int.MaxValue));
        }

        [Test]
        public void ExplicitLargerCapacityRetainsAllEventsAndFreezesAllRetainedSlots()
        {
            var windows = new AllocationWindow();
            do
            {
                var config = new SimConfig(2, 1, 32768, 3, 2);
                var buffer = new SimEventBuffer(config.EventRetainedTicks, config.EventInitialCapacity, config.EventHardLimit);
                buffer.BeginTick(1);
                for (int i = 0; i < 20000; i++)
                    Assert.That(buffer.Publish(new SimEvent(new EventTypeId(80), tag: i)), Is.True);
                buffer.EndTick();
                buffer.CompleteWarmup();
                var meter = new AllocationMeter();
                windows.Begin(meter);
                buffer.BeginTick(2);
                for (int i = 0; i < 20000; i++)
                    buffer.Publish(new SimEvent(new EventTypeId(80), tag: i));
                buffer.EndTick();
                AllocationSample bytes = windows.End();
                if (!windows.NeedsRetry)
                    AllocationAssertions.Zero(bytes);
                Assert.That(buffer.Read(1).Length, Is.EqualTo(20000));
                Assert.That(buffer.Read(2).Length, Is.EqualTo(20000));
                Assert.That(buffer.EventsDroppedTotal, Is.Zero);
                var sim = new Simulation(1, config: config);
                Assert.That(sim.Entities.Capacity, Is.EqualTo(3));
                Assert.That(sim.Scheduler.Capacity, Is.EqualTo(2));
                sim.CompleteWarmup();
                Assert.Throws<InvalidOperationException>(() => sim.Entities.Reserve(4));
                Assert.Throws<InvalidOperationException>(() => sim.Scheduler.Reserve(3));
                Assert.Throws<InvalidOperationException>(() => sim.Entities.Register<Position>("late"));
            }
            while (windows.NeedsRetry);
        }

        [Test]
        public void ConfiguredHardLimitDropsDeterministicallyWithReservedSummary()
        {
            var config = new SimConfig(2, 1, 2);
            var buffer = new SimEventBuffer(config.EventRetainedTicks, config.EventInitialCapacity, config.EventHardLimit);
            buffer.CompleteWarmup();
            buffer.BeginTick(1);
            for (int i = 0; i < 5; i++)
                Assert.That(buffer.Publish(new SimEvent(new EventTypeId(80))), Is.EqualTo(i < 2));
            buffer.EndTick();
            Assert.That(buffer.EventsDroppedThisTick, Is.EqualTo(3));
            Assert.That(buffer.EventsDroppedTotal, Is.EqualTo(3));
            Assert.That(buffer.Read(1).Length, Is.EqualTo(3));
            Assert.That(buffer.Read(1)[2].Type.Value, Is.EqualTo(EventTypeRegistry.EventsDropped));
            Assert.That(buffer.Read(1)[2].Sequence, Is.EqualTo(6));
            Assert.That(buffer.NextSequence, Is.EqualTo(7));
        }

        [Test]
        public void ActualNameHashCollisionNamesBothEntriesAndPreservesOriginalRegistration()
        {
            const string first = "event-169vmnbxrkzz";
            const string second = "event-hw45kee4h3ci";
            var registry = new EventTypeRegistry();
            EventTypeId original = registry.Register(first);
            Assert.That(original.Value, Is.EqualTo(447815528));
            Assert.That(new EventTypeRegistry().Register(second).Value, Is.EqualTo(original.Value));
            ulong before = Fixtures.Hash(registry);
            var error = Assert.Throws<InvalidOperationException>(() => registry.Register(second));
            Assert.That(error.Message, Does.Contain(first));
            Assert.That(error.Message, Does.Contain(second));
            Assert.That(registry.Register(first), Is.EqualTo(original));
            Assert.That(Fixtures.Hash(registry), Is.EqualTo(before));
            Assert.Throws<InvalidOperationException>(() => registry.Register(second));
        }
    }
}
