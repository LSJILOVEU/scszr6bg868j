using System;
using System.Numerics;
using System.Reflection;
using System.Runtime.ExceptionServices;
using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    [Category("S3Vector")]
    public sealed class S3VectorMathTests
    {
        private static SimVec3 Raw(long x, long y, long z) => new SimVec3(SimFix.FromRaw(x), SimFix.FromRaw(y), SimFix.FromRaw(z));
        private static T Property<T>(SimVec3 value, string name)
        {
            PropertyInfo property = typeof(SimVec3).GetProperty(name);
            Assert.That(property, Is.Not.Null, "SimVec3." + name);
            try
            {
                return (T)property.GetValue(value);
            }
            catch (TargetInvocationException error)
            {
                ExceptionDispatchInfo.Capture(error.InnerException).Throw();
                throw;
            }
        }

        private static SimVec3 Scale(SimVec3 value, long factor)
            => S3MathApi.Call<SimVec3>(typeof(SimVec3), "op_Multiply", value, SimFix.FromRaw(factor));
        private static SimFix Dot(SimVec3 a, SimVec3 b) => S3MathApi.Call<SimFix>(typeof(SimVec3), "DotFix", a, b);
        private static SimFix Distance(SimVec3 a, SimVec3 b) => S3MathApi.Call<SimFix>(typeof(SimVec3), "DistanceSquared", a, b);

        [Test]
        public void DimensionlessScalingUsesWideProductsAndFinalTowardZeroRounding()
        {
            Assert.That(Scale(Raw(-3, 5, 0), 32768), Is.EqualTo(Raw(-1, 2, 0)));
            Assert.That(Scale(Raw(long.MinValue, long.MaxValue, 0), 65536), Is.EqualTo(Raw(long.MinValue, long.MaxValue, 0)));
            Assert.That(S3MathApi.Call<SimVec3>(typeof(SimVec3), "op_Multiply", SimFix.FromRaw(32768), Raw(-3, 5, 0)), Is.EqualTo(Raw(-1, 2, 0)));
            Assert.Throws<SimOverflowException>(() => Scale(Raw(long.MinValue, 0, 0), -65536));
            Assert.Throws<SimOverflowException>(() => Scale(Raw(long.MaxValue, 0, 0), 65537));
        }

        [Test]
        public void DotNarrowsOnceAfterCancellationAndDistanceSquaresWideDifferences()
        {
            Assert.That(Dot(Raw(200, 200, 0), Raw(200, 200, 0)).Raw, Is.EqualTo(1));
            Assert.That(Dot(Raw(long.MaxValue, long.MaxValue, 0), Raw(long.MaxValue, -long.MaxValue, 0)).Raw, Is.Zero);
            SimVec3 low = SimVec3.Metres(-16384, -16384, -16384);
            SimVec3 high = SimVec3.Metres(16384, 16384, 16384);
            Assert.That(Distance(low, high).Raw, Is.EqualTo(211106232532992L));
            Assert.That(Distance(low, low).Raw, Is.Zero);
            Assert.That(Dot(high, high).Raw, Is.EqualTo(52776558133248L));
            Assert.Throws<SimOverflowException>(() => Dot(Raw(long.MinValue, 0, 0), Raw(long.MinValue, 0, 0)));
            Assert.Throws<SimOverflowException>(() => Distance(Raw(long.MinValue, 0, 0), Raw(long.MaxValue, 0, 0)));
        }

        [TestCase(196608L, 262144L, 0L, 327680L)]
        [TestCase(-196608L, 262144L, 786432L, 851968L)]
        [TestCase(1L, 1L, 1L, 1L)]
        [TestCase(long.MaxValue, 0L, 0L, long.MaxValue)]
        public void LengthIsTheFloorOfTheRawIntegerSquareRoot(long x, long y, long z, long expected)
            => Assert.That(Property<SimFix>(Raw(x, y, z), "Length").Raw, Is.EqualTo(expected));

        [Test]
        public void ZeroNormalizationIsZeroAndFullRangeDirectionsRemainRepresentable()
        {
            Assert.That(Property<SimVec3>(default, "Normalized"), Is.EqualTo(default(SimVec3)));
            Assert.That(Property<SimVec3>(SimVec3.Metres(3, 4, 0), "Normalized"), Is.EqualTo(Raw(39321, 52428, 0)));
            Assert.That(Property<SimVec3>(Raw(long.MinValue, 0, 0), "Normalized"), Is.EqualTo(SimVec3.Metres(-1, 0, 0)));
            Assert.That(Property<SimVec3>(Raw(long.MinValue, long.MinValue, long.MinValue), "Normalized"), Is.EqualTo(Raw(-37837, -37837, -37837)));
            Assert.That(Property<SimVec3>(Raw(1, 1, 0), "Normalized"), Is.EqualTo(Raw(46340, 46340, 0)));
            Assert.Throws<SimOverflowException>(() => Property<SimFix>(Raw(long.MinValue, 0, 0), "Length"));
        }

        private static BigInteger Root(BigInteger value)
        {
            if (value.IsZero)
                return value;
            BigInteger upper = value;
            BigInteger next = (upper + 1) / 2;
            while (next < upper)
            {
                upper = next;
                next = (upper + value / upper) / 2;
            }
            return upper;
        }

        private static long NormalizedRaw(long value, BigInteger norm2)
            => (long)Root(((BigInteger)value * value << 32) / norm2) * (value < 0 ? -1 : 1);

        [Test]
        public void TwentyThousandBoundaryVectorsMatchIndependentBigIntegerAnswers()
        {
            var input = new BoundaryBiasedInputs(0x53300003);
            for (int i = 0; i < 20000; i++)
            {
                input.Next("mul", i % 2 == 0 ? "endpoints" : "powers", i, out long x, out long y);
                var vector = Raw(x, y, i % 3 - 1);
                BigInteger norm2 = (BigInteger)x * x + (BigInteger)y * y + (i % 3 - 1) * (i % 3 - 1);
                BigInteger length = Root(norm2);
                if (length > long.MaxValue)
                    Assert.Throws<SimOverflowException>(() => Property<SimFix>(vector, "Length"));
                else
                    Assert.That(Property<SimFix>(vector, "Length").Raw, Is.EqualTo((long)length));
                SimVec3 normalized = Property<SimVec3>(vector, "Normalized");
                Assert.That(normalized, Is.EqualTo(norm2.IsZero ? default : Raw(NormalizedRaw(x, norm2),
                    NormalizedRaw(y, norm2), NormalizedRaw(i % 3 - 1, norm2))));
                BigInteger dot = norm2 / 65536;
                if (dot > long.MaxValue)
                    Assert.Throws<SimOverflowException>(() => Dot(vector, vector));
                else
                    Assert.That(Dot(vector, vector).Raw, Is.EqualTo((long)dot));
            }
            Assert.That(Dot(Raw(200, 200, 0), Raw(200, 200, 0)).Raw, Is.Not.EqualTo(0), "Per-component truncation must be detected.");
        }
    }
}
