using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Sim;
using WarCore.Tests.Oracle;

namespace WarCore.Tests
{
    public sealed partial class FixedMathOracleTests
    {
        private const int RandomCases = 20000;

        // The local generator fixes the bit stream across Mono and later runtimes.
        private struct Inputs
        {
            private ulong state;
            internal Inputs(ulong seed)
            {
                state = seed;
            }

            internal ulong Next()
            {
                unchecked
                {
                    state += 0x9E3779B97F4A7C15UL;
                    ulong value = state;
                    value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
                    value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
                    return value ^ (value >> 31);
                }
            }

            internal long Bits(int count)
            {
                ulong magnitude = Next() & ((1UL << count) - 1);
                return (Next() & 1) == 0 ? (long)magnitude : -(long)magnitude;
            }

            internal long Long() => unchecked((long)Next());

            internal long Sample(long[] edges)
            {
                switch (Next() % 5)
                {
                    case 0:
                        return Long();
                    case 1:
                        return Bits((int)(Next() % 64));
                    case 2:
                        return edges[Next() % (ulong)edges.Length];
                    case 3:
                        return long.MaxValue - (long)(Next() & 65535);
                    default:
                        return long.MinValue + (long)(Next() & 65535);
                }
            }
        }

        private static long[] Edges()
        {
            var values = new List<long>{long.MinValue, long.MinValue + 1, long.MinValue + 2, long.MaxValue, long.MaxValue - 1, long.MaxValue - 2, 0, 1, -1};
            for (int bit = 0; bit <= 62; bit++)
            {
                for (int delta = -1; delta <= 1; delta++)
                {
                    long value = (1L << bit) + delta;
                    values.Add(value);
                    values.Add(-value);
                }
            }

            return values.ToArray();
        }

        private readonly struct Outcome
        {
            internal readonly long Value;
            internal readonly Type Error;

            internal Outcome(long value, Type error)
            {
                Value = value;
                Error = error;
            }
        }

        private static Outcome Evaluate(string operation, bool oracle, long a, long b)
        {
            try
            {
                long value;
                switch (operation)
                {
                    case "mul":
                        value = oracle ? FixedMathOracle.Multiply(a, b) : (SimFix.FromRaw(a) * SimFix.FromRaw(b)).Raw;
                        break;
                    case "div":
                        value = oracle ? FixedMathOracle.Divide(a, b) : (SimFix.FromRaw(a) / SimFix.FromRaw(b)).Raw;
                        break;
                    case "sqrt":
                        value = oracle ? FixedMathOracle.Sqrt(a) : SimMath.Sqrt(SimFix.FromRaw(a)).Raw;
                        break;
                    default:
                        throw new ArgumentException("Unknown arithmetic operation.");
                }

                return new Outcome(value, null);
            }
            catch (Exception error)
            {
                return new Outcome(0, error.GetType());
            }
        }

        [TestCase("mul", 0xA11CE001UL)]
        [TestCase("div", 0xA11CE002UL)]
        [TestCase("sqrt", 0xA11CE003UL)]
        public void SeededArithmeticMatchesFrozenOracle(string operation, ulong seed)
        {
            long[] edges = Edges();
            var input = new Inputs(seed);
            int success = 0;
            int errors = 0;
            var failures = new List<string>();
            // Unity's bundled NUnit has no Assert.Multiple; collect mismatches to execute every input.
            for (int i = 0; i < edges.Length + RandomCases; i++)
            {
                long a = i < edges.Length ? edges[i] : input.Sample(edges);
                long b = i < edges.Length ? edges[(i * 37) % edges.Length] : input.Sample(edges);
                if (i >= edges.Length && operation == "mul" && i % 2 == 0)
                {
                    // Roughly half the random cases reach rescaling instead of the overflow guard.
                    int bits = (int)(input.Next() % 62) + 1;
                    a = input.Bits(bits);
                    b = input.Bits(63 - bits);
                }

                if (i >= edges.Length && operation == "sqrt")
                {
                    if (i % 4 == 0)
                    {
                        long root = (long)(input.Next() & ((1UL << 23) - 1));
                        a = root * root * 65536 + (long)(input.Next() % 3) - 1;
                    }
                    else if (i % 4 == 1)
                        a = (long)(input.Next() & long.MaxValue);
                }

                Outcome expected = Evaluate(operation, true, a, b);
                Outcome actual = Evaluate(operation, false, a, b);
                string context = operation + " seed=" + seed + " case=" + i + " a=" + a + " b=" + b;
                if (actual.Error != ArithmeticContractV2.Expected(expected.Error) || actual.Value != expected.Value)
                    failures.Add(context + " expected=" + expected.Value + "/" + expected.Error + " actual=" + actual.Value + "/" + actual.Error);
                if (expected.Error == null)
                    success++;
                else
                {
                    if (expected.Error != typeof(OverflowException) && expected.Error != typeof(DivideByZeroException) && expected.Error != typeof(ArgumentOutOfRangeException))
                        failures.Add(context + " unexpected oracle exception=" + expected.Error);
                    errors++;
                }
            }

            TestContext.WriteLine("Oracle {0}: seed=0x{1:X8}, random={2}, boundary={3}, executed={4}, success={5}, expected exceptions={6}, mismatches={7}", operation, seed, RandomCases, edges.Length, success + errors, success, errors, failures.Count);
            Assert.That(failures, Is.Empty);
            Assert.That(success, Is.GreaterThan(0));
            Assert.That(errors, Is.GreaterThan(0));
        }

        [Test]
        public void SeededMidpointsMatchAllThreeAxesAtLongBoundaries()
        {
            const ulong seed = 0xA11CE004UL;
            long[] edges = Edges();
            var input = new Inputs(seed);
            var failures = new List<string>();
            for (int i = 0; i < edges.Length + RandomCases; i++)
            {
                var a = new long[3];
                var b = new long[3];
                var expected = new long[3];
                for (int axis = 0; axis < 3; axis++)
                {
                    a[axis] = i < edges.Length ? edges[(i + axis) % edges.Length] : input.Sample(edges);
                    b[axis] = i < edges.Length ? edges[(i * 37 + axis) % edges.Length] : input.Sample(edges);
                    expected[axis] = FixedMathOracle.Midpoint(a[axis], b[axis]);
                }

                try
                {
                    SimVec3 actual = SimVec3.Midpoint(MathVectorChecks.Vector(a), MathVectorChecks.Vector(b));
                    if (!actual.Equals(MathVectorChecks.Vector(expected)))
                        failures.Add("midpoint seed=" + seed + " case=" + i + " expected=" + string.Join(",", expected) + " actual=" + actual.X.Raw + "," + actual.Y.Raw + "," + actual.Z.Raw);
                }
                catch (Exception error)
                {
                    failures.Add("midpoint seed=" + seed + " case=" + i + " unexpected exception=" + error);
                }
            }

            TestContext.WriteLine("Oracle midpoint: seed=0x{0:X8}, random={1}, boundary={2}, executed={3}, axis comparisons={4}, mismatches={5}", seed, RandomCases, edges.Length, RandomCases + edges.Length, (RandomCases + edges.Length) * 3, failures.Count);
            Assert.That(failures, Is.Empty);
        }
    }
}
