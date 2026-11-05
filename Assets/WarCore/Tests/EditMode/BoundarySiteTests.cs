using System;
using System.IO;
using System.Reflection;
using System.Numerics;
using System.Text.RegularExpressions;
using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    [Category("G25")]
    public sealed class BoundarySiteTests
    {
        internal static object Call(string type, string method, params object[] arguments)
        {
            try
            {
                return typeof(SimFix).Assembly.GetType("WarCore.Sim." + type, true)
                    .GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, arguments);
            }
            catch (TargetInvocationException error)
            {
                throw error.InnerException;
            }
        }

        [TestCase(long.MinValue, -1L, 0L)]
        [TestCase(long.MinValue, 1L, 0L)]
        [TestCase(long.MinValue, 3L, -2L)]
        [TestCase(-7L, 3L, -1L)]
        [TestCase(7L, -3L, 1L)]
        public void SignedRemainderNeverExecutesTheNativeOverflowPair(long a, long b, long expected)
            => Assert.That(Call("SimInteger", "Remainder", a, b), Is.EqualTo(expected));

        [TestCase("Divide", long.MinValue, -1L)]
        [TestCase("Multiply", long.MinValue, -1L)]
        [TestCase("Add", long.MaxValue, 1L)]
        [TestCase("Subtract", long.MinValue, 1L)]
        public void SignedOverflowIsExplicit(string method, long a, long b)
            => Assert.Throws<SimOverflowException>(() => Call("SimInteger", method, a, b));

        [TestCase("Divide")]
        [TestCase("Remainder")]
        public void ZeroDivisorIsExplicit(string method)
            => Assert.Throws<SimDivideByZeroException>(() => Call("SimInteger", method, long.MinValue, 0L));

        [TestCase("Negate")]
        [TestCase("Abs")]
        public void SignedMinValueHasNoPositiveLongRepresentation(string method)
            => Assert.Throws<SimOverflowException>(() => Call("SimInteger", method, long.MinValue));

        [TestCase(0, -3L)]
        [TestCase(1, -2L)]
        [TestCase(63, -1L)]
        [TestCase(64, -3L)]
        [TestCase(65, -2L)]
        [TestCase(-1, -1L)]
        public void SignedShiftMasksCountThenFillsSignExplicitly(int count, long expected)
        {
            Assert.That(Call("SimInteger", "ShiftRight", -3L, count), Is.EqualTo(expected));
            long left = (long)Call("SimInteger", "ShiftLeft", long.MinValue | 1, count);
            ulong bits = unchecked((ulong)(long.MinValue | 1));
            Assert.That(left, Is.EqualTo(unchecked((long)(bits << (count & 63)))));
        }

        [TestCase(long.MinValue)]
        [TestCase(-1L)]
        [TestCase(0L)]
        [TestCase(long.MaxValue)]
        public void BitEncodingAndMagnitudeRetainEveryBit(long value)
        {
            ulong bits = (ulong)Call("SimInteger", "ToBits", value);
            Assert.That(bits, Is.EqualTo(unchecked((ulong)value)));
            Assert.That(Call("SimInteger", "FromBits", bits), Is.EqualTo(value));
            ulong magnitude = value == long.MinValue ? 0x8000000000000000UL : (ulong)(value < 0 ? -value : value);
            Assert.That(Call("FixedInt128", "Abs", value), Is.EqualTo(magnitude));
            var writer = new SimStateWriter();
            writer.Int64(value);
            Assert.That(new SimStateReader(writer.ToArray()).Int64(), Is.EqualTo(value));
        }

        [TestCase((long)int.MinValue, true)]
        [TestCase((long)int.MaxValue, true)]
        [TestCase((long)int.MinValue - 1, false)]
        [TestCase((long)int.MaxValue + 1, false)]
        [TestCase(long.MinValue, false)]
        [TestCase(long.MaxValue, false)]
        public void NarrowingChecksRangeBeforeCasting(long value, bool fits)
        {
            if (fits)
                Assert.That(Call("SimInteger", "ToInt32", value), Is.EqualTo((int)value));
            else
                Assert.Throws<SimOverflowException>(() => Call("SimInteger", "ToInt32", value));
        }

        [TestCase(0U, 32)]
        [TestCase(1U, 31)]
        [TestCase(0x80000000U, 0)]
        [TestCase(uint.MaxValue, 0)]
        public void LeadingZerosDefinesZeroWithoutMaskedShift(uint value, int expected)
            => Assert.That(Call("GeometryWordDivision", "LeadingZeros", value), Is.EqualTo(expected));

        [TestCase(long.MinValue / 65536)]
        [TestCase(long.MaxValue / 65536)]
        [TestCase(-1L)]
        [TestCase(0L)]
        [TestCase(1L)]
        public void FromIntPreservesAllValidEndpoints(long value)
            => Assert.That(SimFix.FromInt(value).Raw, Is.EqualTo((long)((BigInteger)value * 65536)));

        [TestCase(long.MinValue / 65536 - 1)]
        [TestCase(long.MaxValue / 65536 + 1)]
        public void FromIntRejectsOneUnitOutside(long value)
            => Assert.Throws<SimOverflowException>(() => SimFix.FromInt(value));

        [Test]
        public void ErrorContractV2DoesNotAcceptRuntimeOrUnrelatedExceptions()
        {
            Assert.That(ArithmeticContractV2.Version, Is.EqualTo(2));
            Assert.That(ArithmeticContractV2.Expected("OverflowException"), Is.EqualTo(typeof(SimOverflowException)));
            Assert.That(ArithmeticContractV2.Expected("DivideByZeroException"), Is.EqualTo(typeof(SimDivideByZeroException)));
            Assert.That(ArithmeticContractV2.Expected("OverflowException", false), Is.EqualTo(typeof(OverflowException)));
            Assert.Throws<InvalidOperationException>(() => ArithmeticContractV2.Expected("InvalidOperationException"));
        }

        [Test]
        public void SimCannotReintroduceRuntimeOverflowChecks()
        {
            const string pattern = @"\bchecked\b|new\s+(?:OverflowException|DivideByZeroException)\b";
            Assert.That(Regex.IsMatch("checked(a+b)", pattern), Is.True);
            Assert.That(Regex.IsMatch("new OverflowException()", pattern), Is.True);
            Assert.That(Regex.IsMatch("unchecked(a+b)", pattern), Is.False);
            foreach (string path in Directory.GetFiles(Path.Combine(ArchitectureTests.AssetRoot(), "WarCore/Sim"), "*.cs"))
                Assert.That(Regex.IsMatch(SourceRules.StripComments(File.ReadAllText(path)), pattern), Is.False, path);
        }

        [Test]
        public void FinalEventTickCanBeHashedWithoutIncrementingPastMaxValue()
        {
            var events = new SimEventBuffer(1, 1, 1);
            typeof(SimEventBuffer).GetField("currentTick", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(events, long.MaxValue);
            Assert.DoesNotThrow(() => Fixtures.Hash(events));
            Assert.That(events.OldestAvailableSequence, Is.EqualTo(1));
            Assert.Throws<SimBoundaryException>(() => events.BeginTick(long.MinValue));
        }
    }
}
