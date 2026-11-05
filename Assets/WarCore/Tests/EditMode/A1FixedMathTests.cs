using System;
using System.Collections.Generic;
using System.Numerics;
using NUnit.Framework;
using WarCore.Sim;
using WarCore.Tests.Oracle;
using ProductWide = WarCore.Sim.SimWide;
using ReferenceWide = WarCore.Tests.Oracle.SimWide;

namespace WarCore.Tests
{
    public sealed class A1FixedMathTests
    {
        private const int RandomCases = 20000;

        private readonly struct Outcome
        {
            internal readonly long Value;
            internal readonly Type Error;

            internal Outcome(Func<long> operation)
            {
                Value = 0;
                Error = null;
                try
                {
                    Value = operation();
                }
                catch (Exception error)
                {
                    Error = error.GetType();
                }
            }
        }

        private static void Compare(Outcome actual, Outcome expected, string context, List<string> failures)
        {
            if (actual.Value != expected.Value || actual.Error != ArithmeticContractV2.Expected(expected.Error))
                failures.Add(context + " expected=" + expected.Value + "/" + expected.Error + " actual=" + actual.Value + "/" + actual.Error);
            if (expected.Error != null && expected.Error != typeof(OverflowException) && expected.Error != typeof(DivideByZeroException) && expected.Error != typeof(ArgumentOutOfRangeException))
                failures.Add(context + " unexpected oracle exception=" + expected.Error);
        }

        [TestCase("ratio", 0xA11CE005UL)]
        [TestCase("atan2", 0xA11CE006UL)]
        public void AdditionalScalarPathsMatchActiveContractOracle(string operation, ulong seed)
        {
            long[] edges = A1MathInputs.Edges();
            var input = new A1MathInputs(seed);
            // S1 changes only the atan2 contract; keep the same seed, inputs and exact comparisons.
            var trig = operation == "atan2" ? new Q32TrigReference() : null;
            var failures = new List<string>();
            int success = 0;
            int errors = 0;
            for (int i = 0; i < RandomCases + edges.Length; i++)
            {
                long a = i < edges.Length ? edges[i] : input.Sample(edges);
                long b = i < edges.Length ? edges[(i * 37) % edges.Length] : input.Sample(edges);
                Outcome expected = operation == "ratio"
                    ? new Outcome(() => FixedMathOracle.Divide(a, b))
                    : new Outcome(() => trig.Atan2(a, b));
                Outcome actual = operation == "ratio"
                    ? new Outcome(() => SimFix.Ratio(a, b).Raw)
                    : new Outcome(() => SimMath.Atan2(SimFix.FromRaw(a), SimFix.FromRaw(b)).Raw);
                Compare(actual, expected, operation + " seed=" + seed + " case=" + i + " a=" + a + " b=" + b, failures);
                if (expected.Error == null)
                    success++;
                else
                    errors++;
            }

            TestContext.WriteLine("A1/S1 oracle {0}: seed=0x{1:X8}, random={2}, boundary={3}, executed={4}, success={5}, expected exceptions={6}, mismatches={7}", operation, seed, RandomCases, edges.Length, success + errors, success, errors, failures.Count);
            Assert.That(failures, Is.Empty);
        }

        private static void CompareWide(ProductWide actual, ReferenceWide expected, string context, List<string> failures, ref int conversions, ref int exceptions)
        {
            if (actual.Sign != expected.Sign)
                failures.Add(context + " sign expected=" + expected.Sign + " actual=" + actual.Sign);
            for (int word = 0; word < 12; word++)
                if (actual.RawWord(word) != expected.RawWord(word))
                    failures.Add(context + " word=" + word + " expected=" + expected.RawWord(word) + " actual=" + actual.RawWord(word));
            Outcome narrow = new Outcome(expected.ToSimFixRaw);
            Outcome root = new Outcome(expected.SqrtRaw);
            Compare(new Outcome(() => actual.ToSimFix().Raw), narrow, context + " ToSimFix", failures);
            Compare(new Outcome(() => actual.Sqrt().Raw), root, context + " Sqrt", failures);
            conversions += 2;
            exceptions += narrow.Error == null ? 0 : 1;
            exceptions += root.Error == null ? 0 : 1;
        }

        [Test]
        public void FullLongVectorsMatchEveryWordAndConversionOfFrozenOracle()
        {
            const ulong seed = 0xA11CE007UL;
            long[] edges = A1MathInputs.Edges();
            var input = new A1MathInputs(seed);
            var failures = new List<string>();
            var a = new long[3];
            var b = new long[3];
            int conversions = 0;
            int exceptions = 0;
            for (int i = 0; i < RandomCases + edges.Length; i++)
            {
                for (int axis = 0; axis < 3; axis++)
                {
                    a[axis] = i < edges.Length ? edges[i] : input.Sample(edges);
                    b[axis] = i < edges.Length ? edges[(i * 37 + axis) % edges.Length] : input.Sample(edges);
                    if (i >= edges.Length && i % 4 == 0)
                    {
                        // World-sized inputs also exercise successful narrowing, not just overflow checks.
                        a[axis] = input.Bits(31);
                        b[axis] = input.Bits(31);
                    }
                }

                string context = "vector seed=" + seed + " case=" + i + " a=" + string.Join(",", a) + " b=" + string.Join(",", b);
                try
                {
                    SimVec3 first = MathVectorChecks.Vector(a);
                    SimVec3 second = MathVectorChecks.Vector(b);
                    CompareWide(SimVec3.Dot(first, second), FixedMathOracle.Dot(a, b), context + " dot", failures, ref conversions, ref exceptions);
                    CompareWide(first.LengthSquared, FixedMathOracle.Dot(a, a), context + " lengthSquared", failures, ref conversions, ref exceptions);
                    var actual = SimVec3.Cross(first, second);
                    var expected = FixedMathOracle.Cross(a, b);
                    CompareWide(actual.X, expected.X, context + " cross.X", failures, ref conversions, ref exceptions);
                    CompareWide(actual.Y, expected.Y, context + " cross.Y", failures, ref conversions, ref exceptions);
                    CompareWide(actual.Z, expected.Z, context + " cross.Z", failures, ref conversions, ref exceptions);
                }
                catch (Exception error)
                {
                    failures.Add(context + " unexpected exception=" + error);
                }
            }

            TestContext.WriteLine("A1 oracle vec3: seed=0x{0:X8}, random={1}, boundary={2}, executed={3}, wide values={4}, word comparisons={5}, conversions={6}, expected conversion exceptions={7}, mismatches={8}", seed, RandomCases, edges.Length, RandomCases + edges.Length, (RandomCases + edges.Length) * 5, (RandomCases + edges.Length) * 60, conversions, exceptions, failures.Count);
            Assert.That(conversions, Is.EqualTo((RandomCases + edges.Length) * 10));
            Assert.That(failures, Is.Empty);
        }

        private static BigInteger Raw(ProductWide value)
        {
            BigInteger magnitude = 0;
            for (int word = 11; word >= 0; word--)
                magnitude = (magnitude << 32) + value.RawWord(word);
            return value.Sign < 0 ? -magnitude : magnitude;
        }

        [Test]
        public void SignedMagnitudeRetainsTopBitCarriesBorrowsAndCancellation()
        {
            SimVec3 minimum = MathVectorChecks.Vector(new[]{long.MinValue, long.MinValue, long.MinValue});
            SimVec3 maximum = MathVectorChecks.Vector(new[]{long.MaxValue, long.MaxValue, long.MaxValue});
            Assert.That(Raw(minimum.LengthSquared), Is.EqualTo(3 * (BigInteger.One << 126)));
            Assert.That(Raw(SimVec3.Dot(minimum, maximum)), Is.EqualTo(3 * (BigInteger)long.MinValue * long.MaxValue));
            var cross = SimVec3.Cross(minimum, maximum);
            Assert.That(Raw(cross.X), Is.EqualTo(BigInteger.Zero));
            Assert.That(cross.X.Sign, Is.Zero);
            Assert.That(Raw(cross.Y), Is.EqualTo(BigInteger.Zero));
            Assert.That(Raw(cross.Z), Is.EqualTo(BigInteger.Zero));
            SimVec3 a = MathVectorChecks.Vector(new[]{long.MinValue, long.MaxValue, 1L});
            SimVec3 b = MathVectorChecks.Vector(new[]{long.MaxValue, long.MinValue, -1L});
            cross = SimVec3.Cross(a, b);
            Assert.That(Raw(cross.X), Is.EqualTo(BigInteger.One));
            Assert.That(Raw(cross.Y), Is.EqualTo(-BigInteger.One));
            Assert.That(Raw(cross.Z), Is.EqualTo((BigInteger.One << 64) - 1));
            Assert.That(SimVec3.Dot(a, b).RawWord(-1), Is.Zero);
            Assert.That(SimVec3.Dot(a, b).RawWord(12), Is.Zero);
        }

        [Test]
        public void NarrowingAcceptsNegativeEndpointAndSquareRootRejectsPositiveOverflow()
        {
            SimVec3 unit = MathVectorChecks.Vector(new[]{65536L, 0L, 0L});
            SimVec3 minimum = MathVectorChecks.Vector(new[]{long.MinValue, 0L, 0L});
            SimVec3 maximum = MathVectorChecks.Vector(new[]{long.MaxValue, 0L, 0L});
            Assert.That(SimVec3.Dot(minimum, unit).ToSimFix().Raw, Is.EqualTo(long.MinValue));
            Assert.That(SimVec3.Dot(maximum, unit).ToSimFix().Raw, Is.EqualTo(long.MaxValue));
            Assert.That(maximum.LengthSquared.Sqrt().Raw, Is.EqualTo(long.MaxValue));
            Assert.Throws<SimOverflowException>(() => minimum.LengthSquared.Sqrt());
            Assert.Throws<SimOverflowException>(() => SimVec3.Dot(minimum, default(SimVec3) - unit).ToSimFix());
        }
    }
}
