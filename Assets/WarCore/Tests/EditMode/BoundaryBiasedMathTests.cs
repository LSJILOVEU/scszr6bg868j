using System;
using System.Collections;
using System.Numerics;
using NUnit.Framework;
using WarCore.Sim;
using WarCore.Tests.Oracle;

namespace WarCore.Tests
{
    [Category("R22")]
    public sealed class BoundaryBiasedMathTests
    {
        private const int Samples = 20000;
        public static IEnumerable Cases
        {
            get
            {
                int index = 0;
                foreach (string operation in new[] { "mul", "div", "sqrt", "midpoint" })
                    foreach (string distribution in new[] { "endpoints", "powers", "special", "band" })
                        yield return new TestCaseData(operation, distribution, 0xB0250000UL + (ulong)index++);
            }
        }

        // BigInteger is only a coverage classifier. Every product answer uses the frozen 384-bit oracle.
        private static bool Member(string operation, string distribution, long a, long b)
        {
            BigInteger magnitude = BigInteger.Abs(a);
            if (distribution == "endpoints")
                return a - (BigInteger)long.MinValue <= 65535 || (BigInteger)long.MaxValue - a <= 65535;
            if (distribution == "special")
                return a == long.MinValue || a == long.MaxValue || a == 0 || a == 1 || a == -1;
            if (distribution == "powers")
            {
                for (int bit = 0; bit <= 62; bit++)
                    if (BigInteger.Abs(magnitude - (BigInteger.One << bit)) <= 1)
                        return true;
                return false;
            }
            if (operation == "sqrt")
            {
                if (a < 0)
                    return false;
                BigInteger root = FixedMathOracle.Sqrt(a);
                BigInteger radicand = (BigInteger)a * 65536;
                return BigInteger.Min(radicand - root * root, (root + 1) * (root + 1) - radicand) <= 131072;
            }
            BigInteger value = operation == "mul" ? (BigInteger)a * b
                : operation == "div" ? b == 0 ? BigInteger.Zero : ((BigInteger)a << 16) / b
                : ((BigInteger)a + b) / 2;
            return BigInteger.Abs(BigInteger.Abs(value) - (BigInteger.One << 63)) <= (BigInteger.One << 63) / 1000;
        }

        private static long Evaluate(string operation, bool oracle, long a, long b)
        {
            switch (operation)
            {
                case "mul":
                    return oracle ? FixedMathOracle.Multiply(a, b) : (SimFix.FromRaw(a) * SimFix.FromRaw(b)).Raw;
                case "div":
                    return oracle ? FixedMathOracle.Divide(a, b) : (SimFix.FromRaw(a) / SimFix.FromRaw(b)).Raw;
                case "sqrt":
                    return oracle ? FixedMathOracle.Sqrt(a) : SimMath.Sqrt(SimFix.FromRaw(a)).Raw;
                default:
                    return oracle ? FixedMathOracle.Midpoint(a, b) : SimVec3.Midpoint(
                        new SimVec3(SimFix.FromRaw(a), default, default), new SimVec3(SimFix.FromRaw(b), default, default)).X.Raw;
            }
        }

        [TestCaseSource(nameof(Cases))]
        public void TwentyThousandPerDistributionMatch384BitAnswers(string operation, string distribution, ulong seed)
        {
            var input = new BoundaryBiasedInputs(seed);
            var first = new long[Samples];
            var second = new long[Samples];
            int covered = 0;
            int minima = 0;
            int maxima = 0;
            int zeros = 0;
            for (int i = 0; i < Samples; i++)
            {
                input.Next(operation, distribution, i, out first[i], out second[i]);
                if (Member(operation, distribution, first[i], second[i]))
                    covered++;
                if (first[i] == long.MinValue)
                    minima++;
                if (first[i] == long.MaxValue)
                    maxima++;
                if (first[i] == 0)
                    zeros++;
            }
            TestContext.WriteLine("R2-2 coverage: operation={0}; distribution={1}; seed=0x{2:X8}; samples={3}; actual_members={4}; min={5}; max={6}; zero={7}",
                operation, distribution, seed, Samples, covered, minima, maxima, zeros);
            Assert.That(covered, Is.EqualTo(Samples), "Distribution must actually reach its promised boundary.");
            int errors = 0;
            int mismatches = 0;
            string firstMismatch = null;
            for (int i = 0; i < Samples; i++)
            {
                long expected = 0;
                long actual = 0;
                Type expectedError = null;
                Type actualError = null;
                try
                {
                    expected = Evaluate(operation, true, first[i], second[i]);
                }
                catch (Exception error)
                {
                    expectedError = ArithmeticContractV2.Expected(error.GetType());
                    errors++;
                }
                try
                {
                    actual = Evaluate(operation, false, first[i], second[i]);
                }
                catch (Exception error)
                {
                    actualError = error.GetType();
                }
                if (expected != actual || expectedError != actualError)
                {
                    mismatches++;
                    if (firstMismatch == null)
                        firstMismatch = "case=" + i + "; a=" + first[i] + "; b=" + second[i]
                            + "; expected=" + expected + "/" + expectedError + "; actual=" + actual + "/" + actualError;
                }
            }
            TestContext.WriteLine("R2-2 comparison: operation={0}; distribution={1}; success={2}; errors={3}; mismatches={4}; first={5}",
                operation, distribution, Samples - errors, errors, mismatches, firstMismatch);
            Assert.That(mismatches, Is.Zero, firstMismatch);
        }
    }
}
