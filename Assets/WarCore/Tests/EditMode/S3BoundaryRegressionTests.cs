using System;
using System.Reflection;
using NUnit.Framework;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    [Category("S32")]
    public sealed class S3BoundaryRegressionTests
    {
        [Test]
        public void ExhaustedTaskIdIsRejectedBeforeQueueMutation()
        {
            var scheduler = new SimScheduler(4);
            typeof(SimScheduler).GetField("nextId", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(scheduler, long.MaxValue);
            ulong before = Fixtures.Hash(scheduler);
            Assert.Throws<SimBoundaryException>(() => scheduler.Schedule(1, new SimEvent(new EventTypeId(1))));
            Assert.That(Fixtures.Hash(scheduler), Is.EqualTo(before));
        }

        [Test]
        public void ExhaustedSchedulerClockCannotWrapToMinValue()
        {
            var scheduler = new SimScheduler(4);
            typeof(SimScheduler).GetField("advancedThrough", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(scheduler, long.MaxValue);
            ulong before = Fixtures.Hash(scheduler);
            Assert.Throws<InvalidOperationException>(() => scheduler.Advance(long.MinValue));
            Assert.That(Fixtures.Hash(scheduler), Is.EqualTo(before));
        }

        [Test]
        public void ExhaustedSimClockRaisesExplicitBoundaryError()
        {
            var sim = new Simulation(1);
            typeof(SimClock).GetField("<Tick>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(sim.Clock, long.MaxValue);
            Assert.Throws<SimBoundaryException>(() => sim.Step(default));
            Assert.That(sim.Clock.Tick, Is.EqualTo(long.MaxValue));
        }
        [TestCase(1, 2)]
        [TestCase(int.MaxValue / 2, int.MaxValue - 1)]
        [TestCase(int.MaxValue / 2 + 1, 0)]
        [TestCase(int.MaxValue, 0)]
        public void CapacityGrowthChecksWidthWithoutAllocatingHugeArrays(int capacity, int expected)
        {
            MethodInfo grow = typeof(SimScheduler).GetMethod("GrownCapacity", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(grow, Is.Not.Null);
            if (expected != 0)
                Assert.That(grow.Invoke(null, new object[] { capacity }), Is.EqualTo(expected));
            else
            {
                var error = Assert.Throws<TargetInvocationException>(() => grow.Invoke(null, new object[] { capacity }));
                Assert.That(error.InnerException, Is.TypeOf<SimBoundaryException>());
            }
        }
    }
}
