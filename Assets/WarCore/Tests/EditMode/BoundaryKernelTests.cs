using System;
using System.Reflection;
using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    [Category("G25")]
    public sealed class BoundaryKernelTests
    {
        [Test]
        public void WideCarryBorrowAndScalingRejectUnrepresentableResults()
        {
            Assert.Throws<SimOverflowException>(() => _ = new FixedUInt128(ulong.MaxValue, ulong.MaxValue) + new FixedUInt128(1));
            Assert.Throws<SimOverflowException>(() => _ = new FixedUInt128(0, ulong.MaxValue) + new FixedUInt128(0, 1));
            Assert.Throws<SimOverflowException>(() => _ = new FixedUInt128() - new FixedUInt128(1));
            Assert.Throws<SimOverflowException>(() => new FixedUInt128(0, 1UL << 62).TimesFour());
            FixedUInt128 carry = new FixedUInt128(ulong.MaxValue) + new FixedUInt128(1);
            Assert.That(carry.Low, Is.Zero);
            Assert.That(carry.High, Is.EqualTo(1));
            FixedUInt128 borrow = carry - new FixedUInt128(1);
            Assert.That(borrow.Low, Is.EqualTo(ulong.MaxValue));
            Assert.That(borrow.High, Is.Zero);
            FixedUInt128 scale = new FixedUInt128(ulong.MaxValue, (1UL << 62) - 1).TimesFour();
            Assert.That(scale.Low, Is.EqualTo(ulong.MaxValue - 3));
            Assert.That(scale.High, Is.EqualTo(ulong.MaxValue));
        }

        [Test]
        public void UnsignedDivisionChecksZeroAndHighLimbBeforeNativeDivision()
        {
            Assert.Throws<SimDivideByZeroException>(() => new FixedUInt128(1).DivideToUInt64(0));
            Assert.Throws<SimOverflowException>(() => new FixedUInt128(0, 1).DivideToUInt64(1));
            Assert.That(new FixedUInt128(ulong.MaxValue, 1).DivideToUInt64(2), Is.EqualTo(ulong.MaxValue));
            Assert.That(new FixedUInt128(ulong.MaxValue).DivideToUInt64(ulong.MaxValue), Is.EqualTo(1));
        }

        [Test]
        public void SignedMagnitudeNarrowingDistinguishesBothEndpoints()
        {
            Assert.That(FixedInt128.ToLong(1UL << 63, true), Is.EqualTo(long.MinValue));
            Assert.That(FixedInt128.ToLong(long.MaxValue, false), Is.EqualTo(long.MaxValue));
            Assert.Throws<SimOverflowException>(() => FixedInt128.ToLong(1UL << 63, false));
            Assert.Throws<SimOverflowException>(() => FixedInt128.ToLong((1UL << 63) + 1, true));
            Assert.Throws<SimOverflowException>(() => new FixedInt128(new FixedUInt128(0, 1UL << 16), false).Shift16ToLong());
        }

        [Test]
        public void WordDivisionValidatesZeroAndNormalizationWidths()
        {
            uint[] quotient = new uint[2];
            Assert.Throws<SimDivideByZeroException>(() => GeometryWordDivision.Divide(new uint[] { 1 }, new uint[] { 0 }, quotient));
            Assert.Throws<SimDivideByZeroException>(() => GeometryWordDivision.Divide(new uint[] { 1 }, Array.Empty<uint>(), quotient));
            Assert.Throws<SimBoundaryException>(() => GeometryWordDivision.Divide(new uint[] { 1 }, new uint[] { 1 }, Array.Empty<uint>()));
            GeometryWordDivision.Divide(new uint[] { uint.MaxValue, uint.MaxValue }, new uint[] { 1 }, quotient);
            Assert.That(quotient, Is.EqualTo(new uint[] { uint.MaxValue, uint.MaxValue }));
            foreach (uint top in new uint[] { 1, 0x80000000U, uint.MaxValue })
            {
                Array.Clear(quotient, 0, quotient.Length);
                GeometryWordDivision.Divide(new uint[] { 17, top }, new uint[] { 17, top }, quotient);
                Assert.That(quotient[0], Is.EqualTo(1));
                Assert.That(quotient[1], Is.Zero);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void EventSummaryChecksSequenceAndFixedConversionBeforePublishing(bool conversion)
        {
            var events = new SimEventBuffer(1, 1, 1);
            events.BeginTick(1);
            events.Publish(new SimEvent(new EventTypeId(5)));
            events.Publish(new SimEvent(new EventTypeId(5)));
            string property = conversion ? "EventsDroppedThisTick" : "NextSequence";
            typeof(SimEventBuffer).GetField("<" + property + ">k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(events, long.MaxValue);
            ulong before = Fixtures.Hash(events);
            Assert.That(Assert.Catch(() => events.EndTick()), Is.InstanceOf<SimBoundaryException>());
            Assert.That(Fixtures.Hash(events), Is.EqualTo(before));
        }

        [TestCase(long.MinValue, 1L, long.MinValue)]
        [TestCase(long.MinValue, long.MinValue, 1L)]
        [TestCase(-7L, 3L, -2L)]
        [TestCase(7L, -3L, -2L)]
        public void SignedDivisionRetainsTruncationAndTheNegativeEndpoint(long a, long b, long expected)
            => Assert.That(SimInteger.Divide(a, b), Is.EqualTo(expected));

        [TestCase(0)]
        [TestCase(-1)]
        [TestCase(long.MinValue)]
        public void GridRejectsInvalidDivisorsBeforeFloorOrDda(long raw)
        {
            var store = new EntityStore(1);
            var p = store.Register<Position>("p");
            var c = store.Register<Collider>("c");
            Assert.Throws<ArgumentOutOfRangeException>(() => new GridSpatialQuery(store, p, c, SimFix.FromRaw(raw)));
        }

        [Test]
        public void FinishedEntityEnumerationDoesNotIncrementAnExhaustedCursor()
        {
            object cursor = new EntityStore(1).GetEnumerator();
            var type = typeof(EntityStore.EntityEnumerator);
            type.GetField("index", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(cursor, int.MaxValue);
            Assert.That(type.GetMethod("MoveNext").Invoke(cursor, null), Is.False);
            Assert.That(type.GetMethod("MoveNext").Invoke(cursor, null), Is.False);
        }

        [Test]
        public void IntegerAndFixedMathWarmPathsRemainStrictlyAllocationFree()
        {
            var meter = new AllocationMeter();
            var windows = new AllocationWindow();
            long expected = Batch();
            do
            {
                windows.Begin(meter);
                long actual = Batch();
                windows.End();
                Assert.That(actual, Is.EqualTo(expected));
            }
            while (windows.NeedsRetry);
            AllocationAssertions.Zero(windows.Result);
            TestContext.WriteLine("G25 arithmetic allocation: " + windows.Result.Describe() + "; positive control required.");
        }

        private static long Batch()
        {
            long total = 0;
            for (int i = 1; i <= 4096; i++)
            {
                SimFix value = SimFix.FromInt(i);
                total += (value + value - value).Raw;
                total += SimMath.Sqrt(value).Raw;
                total += SimInteger.Remainder(long.MinValue, -1);
                total += SimInteger.ShiftRight(-i, i);
                total += SimInteger.ToInt32(i);
            }
            return total;
        }
    }
}
