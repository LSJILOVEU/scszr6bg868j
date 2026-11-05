using System;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.ExceptionServices;
using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    internal static class S3MathApi
    {
        // Public reflection lets missing APIs fail as NUnit assertions before their implementation exists.
        internal static T Call<T>(Type type, string name, params object[] values)
        {
            MethodInfo method = type.GetMethod(name, values.Select(value => value.GetType()).ToArray());
            Assert.That(method, Is.Not.Null, type.Name + "." + name);
            try
            {
                return (T)method.Invoke(null, values);
            }
            catch (TargetInvocationException error)
            {
                ExceptionDispatchInfo.Capture(error.InnerException).Throw();
                throw;
            }
        }

        internal static SimFix Scalar(string name, params long[] values)
            => Call<SimFix>(typeof(SimFix), name, values.Select(value => (object)SimFix.FromRaw(value)).ToArray());
    }

    [Category("S3Scalar")]
    public sealed class S3ScalarMathTests
    {
        [TestCase(0L, 0L)]
        [TestCase(-1L, 1L)]
        [TestCase(long.MinValue + 1, long.MaxValue)]
        [TestCase(long.MaxValue, long.MaxValue)]
        public void AbsoluteValuePreservesRepresentableMagnitude(long input, long expected)
            => Assert.That(S3MathApi.Scalar("Abs", input).Raw, Is.EqualTo(expected));

        [Test]
        public void AbsoluteMinimumHasTheExplicitG25Failure()
            => Assert.Throws<SimOverflowException>(() => S3MathApi.Scalar("Abs", long.MinValue));

        [Test]
        public void MinMaxAndClampHandleBothEndpointsAndRejectReversedBounds()
        {
            Assert.That(S3MathApi.Scalar("Min", long.MinValue, long.MaxValue).Raw, Is.EqualTo(long.MinValue));
            Assert.That(S3MathApi.Scalar("Max", long.MinValue, long.MaxValue).Raw, Is.EqualTo(long.MaxValue));
            Assert.That(S3MathApi.Scalar("Min", 7, 7).Raw, Is.EqualTo(7));
            Assert.That(S3MathApi.Scalar("Max", -7, -7).Raw, Is.EqualTo(-7));
            foreach (long value in new[] { long.MinValue, -8, 0, 8, long.MaxValue })
                Assert.That(S3MathApi.Scalar("Clamp", value, -7, 7).Raw, Is.EqualTo(value < -7 ? -7 : value > 7 ? 7 : value));
            Assert.That(S3MathApi.Scalar("Clamp", 8, 7, 7).Raw, Is.EqualTo(7));
            Assert.Throws<ArgumentException>(() => S3MathApi.Scalar("Clamp", 0, 1, -1));
        }

        [TestCase(0L, 0L, 0L)]
        [TestCase(32767L, 0L, 0L)]
        [TestCase(32768L, 0L, 65536L)]
        [TestCase(32769L, 0L, 65536L)]
        [TestCase(-32767L, -65536L, 0L)]
        [TestCase(-32768L, -65536L, -65536L)]
        [TestCase(-32769L, -65536L, -65536L)]
        [TestCase(98304L, 65536L, 131072L)]
        [TestCase(-98304L, -131072L, -131072L)]
        [TestCase(long.MinValue, long.MinValue, long.MinValue)]
        [TestCase(long.MinValue + 1, long.MinValue, long.MinValue)]
        public void FloorRoundsDownAndRoundUsesHalfAwayFromZero(long input, long floor, long round)
        {
            Assert.That(S3MathApi.Scalar("Floor", input).Raw, Is.EqualTo(floor));
            Assert.That(S3MathApi.Scalar("Round", input).Raw, Is.EqualTo(round));
        }

        [Test]
        public void RoundingPastThePositiveEndpointFailsBeforeAddition()
        {
            Assert.That(S3MathApi.Scalar("Floor", long.MaxValue).Raw, Is.EqualTo(9223372036854710272L));
            Assert.That(S3MathApi.Scalar("Round", 9223372036854743039L).Raw, Is.EqualTo(9223372036854710272L));
            Assert.Throws<SimOverflowException>(() => S3MathApi.Scalar("Round", 9223372036854743040L));
            Assert.Throws<SimOverflowException>(() => S3MathApi.Scalar("Round", long.MaxValue));
        }

        [TestCase(65536L, 196608L, 32768L, 131072L)]
        [TestCase(65536L, 196608L, -32768L, 0L)]
        [TestCase(65536L, 196608L, 98304L, 262144L)]
        [TestCase(long.MinValue, long.MaxValue, 0L, long.MinValue)]
        [TestCase(long.MinValue, long.MaxValue, 65536L, long.MaxValue)]
        [TestCase(long.MinValue, long.MaxValue, 32768L, 0L)]
        [TestCase(-1L, 0L, 32768L, 0L)]
        public void LerpExtrapolatesAndTruncatesTheFinalWideNumeratorOnce(long a, long b, long t, long expected)
            => Assert.That(S3MathApi.Scalar("Lerp", a, b, t).Raw, Is.EqualTo(expected));

        [Test]
        public void LerpRejectsAnUnrepresentableResultWithG25Exception()
            => Assert.Throws<SimOverflowException>(() => S3MathApi.Scalar("Lerp", long.MinValue, long.MaxValue, 65537));

        [Test]
        public void BoundaryBiasedScalarAnswersMatchIndependentIntegerArithmetic()
        {
            var input = new BoundaryBiasedInputs(0x53300002);
            for (int i = 0; i < 20000; i++)
            {
                input.Next("midpoint", i % 2 == 0 ? "endpoints" : "powers", i, out long a, out long b);
                long t = i % 65537;
                long expected = (long)(((BigInteger)a * (65536 - t) + (BigInteger)b * t) / 65536);
                Assert.That(S3MathApi.Scalar("Lerp", a, b, t).Raw, Is.EqualTo(expected));
                BigInteger floor = (BigInteger)a / 65536;
                if (a < 0 && a % 65536 != 0)
                    floor--;
                Assert.That(S3MathApi.Scalar("Floor", a).Raw, Is.EqualTo((long)(floor * 65536)));
                BigInteger rounded = (BigInteger.Abs(a) + 32768) / 65536 * 65536 * (a < 0 ? -1 : 1);
                if (rounded > long.MaxValue)
                    Assert.Throws<SimOverflowException>(() => S3MathApi.Scalar("Round", a));
                else
                    Assert.That(S3MathApi.Scalar("Round", a).Raw, Is.EqualTo((long)rounded));
            }
            Assert.That(S3MathApi.Scalar("Lerp", -1, 0, 32768).Raw, Is.Not.EqualTo(-1), "A floor mutation must be observable.");
        }

        [Test]
        public void ExistingConversionsAndMidpointsSpecifyTheirRoundingDirections()
        {
            Assert.That(SimFix.FromInt(-3).Raw, Is.EqualTo(-196608));
            Assert.That(SimFix.FromInt(-140737488355328L).Raw, Is.EqualTo(long.MinValue));
            Assert.Throws<SimOverflowException>(() => SimFix.FromInt(140737488355328L));
            Assert.That(SimFix.Ratio(1, 30).Raw, Is.EqualTo(2184));
            Assert.That(SimFix.Ratio(-1, 30).Raw, Is.EqualTo(-2184));
            Assert.That(SimVec3.Midpoint(new SimVec3(SimFix.FromRaw(-2), default, default),
                new SimVec3(SimFix.FromRaw(-1), default, default)).X.Raw, Is.EqualTo(-1));
            Assert.That(SimVec3.Midpoint(new SimVec3(SimFix.FromRaw(long.MinValue), default, default),
                new SimVec3(SimFix.FromRaw(long.MaxValue), default, default)).X.Raw, Is.Zero);
        }
    }
}
