using System;
using System.Numerics;
using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class FixedMathTests
    {
        [Test]
        public void Q16LongResolutionAndMultiplicationDivisionTruncateTowardZero()
        {
            Assert.That(SimFix.Scale, Is.EqualTo(65536));
            Assert.That(1000.0 / SimFix.Scale, Is.EqualTo(0.0152587890625));
            Assert.That((SimFix.FromRaw(-3) * SimFix.Ratio(1, 2)).Raw, Is.EqualTo(-1));
            Assert.That((SimFix.FromRaw(3) * SimFix.Ratio(1, 2)).Raw, Is.EqualTo(1));
            Assert.That((SimFix.FromRaw(-3) / SimFix.FromInt(2)).Raw, Is.EqualTo(-1));
            Assert.That((SimFix.FromRaw(3) / SimFix.FromInt(-2)).Raw, Is.EqualTo(-1));
            Assert.That(SimFix.Ratio(-1, 3).Raw, Is.EqualTo(-21845));
            Assert.That((SimFix.FromInt(5000) * SimFix.FromInt(5000)), Is.EqualTo(SimFix.FromInt(25000000)));
        }

        [Test]
        public void OverflowAndInvalidNumericInputsFailExplicitly()
        {
            Assert.Throws<SimOverflowException>(() => SimFix.FromInt(long.MaxValue));
            Assert.Throws<SimOverflowException>(() =>
            {
                var value = SimFix.FromRaw(long.MaxValue) + SimFix.FromRaw(1);
            }

            );
            Assert.Throws<SimOverflowException>(() =>
            {
                var value = SimFix.FromInt(32768) * SimFix.FromInt(65536);
            }

            );
            Assert.Throws<SimOverflowException>(() =>
            {
                var value = SimFix.FromInt(-32768) * SimFix.FromInt(65536);
            }

            );
            Assert.Throws<SimOverflowException>(() =>
            {
                var value = SimFix.FromRaw(long.MaxValue) / SimFix.FromRaw(1);
            }

            );
            Assert.Throws<SimDivideByZeroException>(() =>
            {
                var value = SimFix.FromInt(1) / default(SimFix);
            }

            );
            Assert.Throws<ArgumentOutOfRangeException>(() => SimMath.Sqrt(SimFix.FromRaw(-1)));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Position(SimVec3.Metres(16385, 0, 0)));
        }

        private static BigInteger Raw(SimWide value)
        {
            BigInteger result = 0;
            for (int i = 7; i >= 0; i--)
                result = (result << 32) + value.RawWord(i);
            return value.Sign < 0 ? -result : result;
        }

        [Test]
        public void BoundaryMidpointDifferenceSquaredLengthDotAndCrossRemainExact()
        {
            SimVec3 a = SimVec3.Metres(16384, 16384, 16384);
            SimVec3 b = SimVec3.Metres(-16384, -16384, -16384);
            SimVec3 d = a - b;
            Assert.That(SimVec3.Midpoint(a, b), Is.EqualTo(default(SimVec3)));
            Assert.That(SimVec3.Midpoint(a, SimVec3.Metres(16382, 16382, 16382)), Is.EqualTo(SimVec3.Metres(16383, 16383, 16383)));
            BigInteger expected = (BigInteger)(32768L * SimFix.Scale) * (32768L * SimFix.Scale);
            Assert.That(Raw(d.LengthSquared), Is.EqualTo(expected * 3));
            Assert.That(Raw(SimVec3.Dot(d, b)), Is.EqualTo(-expected * 3 / 2));
            SimWideVec3 cross = SimVec3.Cross(d, SimVec3.Metres(-32768, 32768, -32768));
            Assert.That(Raw(cross.X), Is.EqualTo(-expected * 2));
            Assert.That(Raw(cross.Y), Is.EqualTo(BigInteger.Zero));
            Assert.That(Raw(cross.Z), Is.EqualTo(expected * 2));
        }

        [Test]
        public void FixedArithmeticMatchesIndependentBigIntegerAcrossSignsAndLargeOperands()
        {
            var random = new Random(1729);
            for (int i = 0; i < 1000; i++)
            {
                long a = ((long)random.Next() << 20) + random.Next(1048576);
                long b = random.Next(1, int.MaxValue);
                if (i % 2 == 0)
                    a = -a;
                if (i % 3 == 0)
                    b = -b;
                BigInteger quotient = (BigInteger)a * SimFix.Scale / b;
                Assert.That((SimFix.FromRaw(a) / SimFix.FromRaw(b)).Raw, Is.EqualTo((long)quotient));
                BigInteger product = (BigInteger)a * b;
                if (BigInteger.Abs(product) < (BigInteger.One << 63))
                    Assert.That((SimFix.FromRaw(a) * SimFix.FromRaw(b)).Raw, Is.EqualTo((long)(product / SimFix.Scale)));
                else
                    Assert.Throws<SimOverflowException>(() =>
                    {
                        var value = SimFix.FromRaw(a) * SimFix.FromRaw(b);
                    }

                    );
            }
        }

        [Test]
        public void SqrtAndTrigonometricKnownVectorsAndReferenceErrorStayWithinTableResolution()
        {
            Assert.That(SimMath.Sqrt(default).Raw, Is.Zero);
            Assert.That(SimMath.Sqrt(SimFix.FromInt(25)), Is.EqualTo(SimFix.FromInt(5)));
            Assert.That(SimMath.Sqrt(SimFix.FromInt(2)).Raw, Is.EqualTo(92681));
            Assert.That(SimMath.Sin(SimFix.Ratio(1, 4)).Raw, Is.EqualTo(65536));
            Assert.That(SimMath.Sin(SimFix.Ratio(-1, 4)).Raw, Is.EqualTo(-65536));
            Assert.That(SimMath.Cos(default).Raw, Is.EqualTo(65536));
            Assert.That(SimMath.Atan2(SimFix.FromInt(1), SimFix.FromInt(1)).Raw, Is.EqualTo(8192));
            Assert.That(SimMath.Atan2(SimFix.FromInt(-1), SimFix.FromInt(-1)).Raw, Is.EqualTo(-24576));
            for (int raw = -65536; raw <= 65536; raw += 137)
            {
                double radians = raw / 65536.0 * 2 * Math.PI;
                Assert.That(Math.Abs(SimMath.Sin(SimFix.FromRaw(raw)).Raw - Math.Sin(radians) * 65536), Is.LessThan(0.6));
                Assert.That(Math.Abs(SimMath.Cos(SimFix.FromRaw(raw)).Raw - Math.Cos(radians) * 65536), Is.LessThan(0.6));
            }

            for (int x = -20; x <= 20; x++)
                for (int y = -20; y <= 20; y++)
                    Assert.That(Math.Abs(SimMath.Atan2(SimFix.FromInt(y), SimFix.FromInt(x)).Raw - Math.Atan2(y, x) / (2 * Math.PI) * 65536), Is.LessThan(0.6));
        }
    }
}
