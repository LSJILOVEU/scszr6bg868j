using System;
using System.Reflection;
using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    [Category("G25")]
    public sealed class ExplicitBoundaryTests
    {
        private static SimFix Raw(long value) => SimFix.FromRaw(value);

        [TestCase("add")]
        [TestCase("subtract")]
        [TestCase("negate")]
        [TestCase("multiply")]
        [TestCase("divide")]
        [TestCase("ratio")]
        [TestCase("zero")]
        [TestCase("from-int")]
        [TestCase("wide-convert")]
        [TestCase("wide-root")]
        public void ScalarFailuresUseExplicitSimulationExceptions(string operation)
        {
            Exception error = Assert.Catch(() =>
            {
                switch (operation)
                {
                    case "add":
                        _ = Raw(long.MaxValue) + Raw(1);
                        break;
                    case "subtract":
                        _ = Raw(long.MinValue) - Raw(1);
                        break;
                    case "negate":
                        _ = -Raw(long.MinValue);
                        break;
                    case "multiply":
                        _ = Raw(long.MinValue) * Raw(-1);
                        break;
                    case "divide":
                        _ = Raw(long.MinValue) / Raw(-1);
                        break;
                    case "ratio":
                        _ = SimFix.Ratio(long.MinValue, -1);
                        break;
                    case "zero":
                        _ = Raw(1) / Raw(0);
                        break;
                    case "from-int":
                        _ = SimFix.FromInt(long.MaxValue);
                        break;
                    case "wide-convert":
                        _ = new SimVec3(Raw(long.MaxValue), default, default).LengthSquared.ToSimFix();
                        break;
                    case "wide-root":
                        _ = new SimVec3(Raw(long.MinValue), default, default).LengthSquared.Sqrt();
                        break;
                    default:
                        Assert.Fail(operation);
                        break;
                }
            });
            Assert.That(error, Is.InstanceOf<SimBoundaryException>());
            Assert.That(error.GetType().Name, Is.EqualTo(operation == "zero" ? "SimDivideByZeroException" : "SimOverflowException"));
        }

        [TestCase(0, 1UL, 0x8000000000000001UL)]
        [TestCase(1, 0x8000000000000000UL, 0x4000000000000000UL)]
        [TestCase(63, 2UL, 1UL)]
        [TestCase(64, 0x8000000000000001UL, 0UL)]
        [TestCase(65, 0x4000000000000000UL, 0UL)]
        [TestCase(127, 1UL, 0UL)]
        [TestCase(128, 0UL, 0UL)]
        [TestCase(129, 0UL, 0UL)]
        [TestCase(int.MaxValue, 0UL, 0UL)]
        public void WideShiftHasLogicalWidthInsteadOfPlatformMasking(int bits, ulong low, ulong high)
        {
            Type type = typeof(SimFix).Assembly.GetType("WarCore.Sim.FixedUInt128", true);
            object value = Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.NonPublic, null,
                new object[] { 1UL, 0x8000000000000001UL }, null);
            object result = type.GetMethod("ShiftRight", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(value, new object[] { bits });
            Assert.That(type.GetField("Low", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(result), Is.EqualTo(low));
            Assert.That(type.GetField("High", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(result), Is.EqualTo(high));
        }

        [TestCase(-1)]
        [TestCase(int.MinValue)]
        public void NegativeWideShiftIsRejected(int bits)
        {
            Type type = typeof(SimFix).Assembly.GetType("WarCore.Sim.FixedUInt128", true);
            object value = Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.NonPublic, null,
                new object[] { 1UL, 2UL }, null);
            var error = Assert.Throws<TargetInvocationException>(() => type.GetMethod("ShiftRight", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(value, new object[] { bits }));
            Assert.That(error.InnerException, Is.TypeOf<SimBoundaryException>());
        }

        [Test]
        public void AccumulatorRejectsMultiplyAddOverflowBeforeChangingRemainder()
        {
            var accumulator = new TickAccumulator(31);
            Assert.That(accumulator.AddElapsed(1), Is.Zero);
            Assert.That(Assert.Catch(() => accumulator.AddElapsed(long.MaxValue)), Is.InstanceOf<SimBoundaryException>());
            Assert.That(Assert.Catch(() => accumulator.AddElapsed(long.MaxValue / 30)), Is.InstanceOf<SimBoundaryException>());
            Assert.That(accumulator.AddElapsed(1), Is.EqualTo(1));
        }

        [TestCase("NextSequence")]
        [TestCase("EventsDroppedThisTick")]
        [TestCase("EventsDroppedTotal")]
        public void EventCounterExhaustionRejectsBeforeMutation(string counter)
        {
            var events = new SimEventBuffer(1, 1, 1);
            events.BeginTick(1);
            var item = new SimEvent(new EventTypeId(5));
            events.Publish(item);
            typeof(SimEventBuffer).GetField("<" + counter + ">k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(events, long.MaxValue);
            ulong before = Fixtures.Hash(events);
            Assert.That(Assert.Catch(() => events.Publish(item)), Is.InstanceOf<SimBoundaryException>());
            Assert.That(Fixtures.Hash(events), Is.EqualTo(before));
        }
    }
}
