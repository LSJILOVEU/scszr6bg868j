using System;
using System.Collections;
using System.Globalization;
using System.Numerics;
using NUnit.Framework;
using WarCore.Sim;
using WarCore.Tests.Oracle;
using ProductWide = WarCore.Sim.SimWide;
using ReferenceWide = WarCore.Tests.Oracle.SimWide;

namespace WarCore.Tests
{
    public sealed class LegacyFixedMathVectorTests
    {
        public static IEnumerable Cases => MathVectorChecks.Cases("fixed-math-vectors.json", "Q16_product", true);

        [TestCaseSource(nameof(Cases))]
        public void ProductMatchesTheActiveContractForEveryLegacyInput(string operation, object row, object replacement)
        {
            VectorJson legacy = (VectorJson)row;
            if (replacement == null)
                MathVectorChecks.Verify(operation, legacy, null);
            else
            {
                // S1 supersedes only Q16 trig expectations. Preserve every legacy input and
                // assert the product against its paired Q32 contract instead of ignoring the case.
                Assert.That(MathVectorChecks.IsTrig(operation), Is.True);
                VectorJson current = (VectorJson)replacement;
                int inputs = operation == "atan2" ? 2 : 1;
                for (int i = 0; i < inputs; i++)
                    Assert.That(legacy[i].Integer, Is.EqualTo(current[i].Integer), "Superseded Q16 input must have the same Q32 input.");
                MathVectorChecks.Verify(operation, current, null);
            }
        }
    }

    public sealed class Q32ProductVectorTests
    {
        public static IEnumerable Cases => MathVectorChecks.Cases("fixed-math-vectors-q32.json", "Q32_product");

        [TestCaseSource(nameof(Cases))]
        public void ProductMatchesFrozenQ32Contract(string operation, object row)
        {
            MathVectorChecks.Verify(operation, (VectorJson)row, null);
        }
    }

    public sealed class Q32SpecificationVectorTests
    {
        private Q32TrigReference reference;
        public static IEnumerable Cases => MathVectorChecks.Cases("fixed-math-vectors-q32.json", "Q32_specification");

        [OneTimeSetUp]
        public void CreateIndependentReference()
        {
            reference = new Q32TrigReference();
        }

        // Keep the independent reference checks alongside direct product assertions so a
        // product-table error cannot silently redefine the vector contract.
        [TestCaseSource(nameof(Cases))]
        public void ProposedQ32ContractIsInternallyConsistent(string operation, object row)
        {
            MathVectorChecks.Verify(operation, (VectorJson)row, reference);
        }
    }

    internal static class MathVectorChecks
    {
        internal static bool IsTrig(string operation) => operation == "sin" || operation == "cos" || operation == "atan2";

        internal static IEnumerable Cases(string file, string prefix, bool legacy = false)
        {
            VectorJson document = VectorJson.Load(file);
            VectorJson current = legacy ? VectorJson.Load("fixed-math-vectors-q32.json") : null;
            foreach (string operation in document.Keys)
            {
                if (operation == "meta")
                    continue;
                VectorJson rows = document[operation];
                for (int i = 0; i < rows.Count; i++)
                {
                    bool superseded = legacy && IsTrig(operation);
                    TestCaseData data = legacy
                        ? new TestCaseData(operation, rows[i], superseded ? current[operation][i] : null)
                        : new TestCaseData(operation, rows[i]);
                    string activePrefix = superseded ? "Q16_trig_superseded_by_Q32" : prefix;
                    if (superseded)
                        data.SetProperty("Contract", "Q16 trig expectation superseded by fixed-math-vectors-q32.json; product checked against Q32 for the same input.");
                    yield return data.SetName(activePrefix + "." + operation + "[" + i.ToString("D4", CultureInfo.InvariantCulture) + "]");
                }
            }
        }

        internal static SimVec3 Vector(long[] raw) => new SimVec3(SimFix.FromRaw(raw[0]), SimFix.FromRaw(raw[1]), SimFix.FromRaw(raw[2]));

        internal static void Expect(VectorJson expected, Func<long> operation, bool product = true)
        {
            long actual = 0;
            Exception error = null;
            try
            {
                actual = operation();
            }
            catch (Exception caught)
            {
                error = caught;
            }

            if (expected.IsString)
                Assert.That(error == null ? null : error.GetType(), Is.EqualTo(ArithmeticContractV2.Expected(expected.Text, product)));
            else
            {
                Assert.That(error, Is.Null);
                Assert.That(actual, Is.EqualTo(expected.Integer));
            }
        }

        internal static void Verify(string operation, VectorJson row, Q32TrigReference reference)
        {
            bool specification = reference != null;
            if (operation == "midpoint")
            {
                long[] a = row["a"].Integers();
                long[] b = row["b"].Integers();
                long[] expected = row["expect"].Integers();
                if (specification)
                {
                    for (int axis = 0; axis < 3; axis++)
                        Assert.That(FixedMathOracle.Midpoint(a[axis], b[axis]), Is.EqualTo(expected[axis]));
                }
                else
                    Assert.That(SimVec3.Midpoint(Vector(a), Vector(b)), Is.EqualTo(Vector(expected)));
                return;
            }

            if (operation == "vec3_wide")
            {
                VerifyWide(row, specification);
                return;
            }

            long x = row[0].Integer;
            if (operation == "sqrt")
                Expect(row[1], () => specification ? FixedMathOracle.Sqrt(x) : SimMath.Sqrt(SimFix.FromRaw(x)).Raw, !specification);
            else if (operation == "sin")
                Expect(row[1], () => specification ? reference.Sin(x) : SimMath.Sin(SimFix.FromRaw(x)).Raw, !specification);
            else if (operation == "cos")
                Expect(row[1], () => specification ? reference.Cos(x) : SimMath.Cos(SimFix.FromRaw(x)).Raw, !specification);
            else
            {
                long y = row[1].Integer;
                switch (operation)
                {
                    case "mul":
                        Expect(row[2], () => specification ? FixedMathOracle.Multiply(x, y) : (SimFix.FromRaw(x) * SimFix.FromRaw(y)).Raw, !specification);
                        break;
                    case "div":
                        Expect(row[2], () => specification ? FixedMathOracle.Divide(x, y) : (SimFix.FromRaw(x) / SimFix.FromRaw(y)).Raw, !specification);
                        break;
                    case "ratio":
                        Expect(row[2], () => specification ? FixedMathOracle.Divide(x, y) : SimFix.Ratio(x, y).Raw, !specification);
                        break;
                    case "atan2":
                        Expect(row[2], () => specification ? reference.Atan2(x, y) : SimMath.Atan2(SimFix.FromRaw(x), SimFix.FromRaw(y)).Raw, !specification);
                        break;
                    default:
                        Assert.Fail("Unrecognized math vector operation: " + operation);
                        break;
                }
            }
        }

        private static void Magnitude(string expectedText, int sign, Func<int, uint> word)
        {
            BigInteger expected = BigInteger.Parse(expectedText, CultureInfo.InvariantCulture);
            Assert.That(sign, Is.EqualTo(expected.Sign));
            BigInteger magnitude = BigInteger.Abs(expected);
            // All twelve words are checked, including high zero words that could hide stale carries.
            for (int i = 0; i < 12; i++)
            {
                Assert.That(word(i), Is.EqualTo((uint)(magnitude & uint.MaxValue)), "word " + i);
                magnitude >>= 32;
            }

            Assert.That(magnitude.IsZero, Is.True);
        }

        private static void Wide(VectorJson row, string prefix, ProductWide value)
        {
            Magnitude(row[prefix].Text, value.Sign, value.RawWord);
            Expect(row[prefix + "_toSimFix"], () => value.ToSimFix().Raw);
            Expect(row[prefix + "_sqrt"], () => value.Sqrt().Raw);
        }

        private static void Wide(VectorJson row, string prefix, ReferenceWide value)
        {
            Magnitude(row[prefix].Text, value.Sign, value.RawWord);
            Expect(row[prefix + "_toSimFix"], value.ToSimFixRaw, false);
            Expect(row[prefix + "_sqrt"], value.SqrtRaw, false);
        }

        private static void VerifyWide(VectorJson row, bool specification)
        {
            long[] a = row["a"].Integers();
            long[] b = row["b"].Integers();
            VectorJson cross = row["cross"];
            if (specification)
            {
                Wide(row, "dot", FixedMathOracle.Dot(a, b));
                Wide(row, "lengthSquaredA", FixedMathOracle.Dot(a, a));
                var value = FixedMathOracle.Cross(a, b);
                Magnitude(cross[0].Text, value.X.Sign, value.X.RawWord);
                Magnitude(cross[1].Text, value.Y.Sign, value.Y.RawWord);
                Magnitude(cross[2].Text, value.Z.Sign, value.Z.RawWord);
            }
            else
            {
                SimVec3 first = Vector(a);
                SimVec3 second = Vector(b);
                Wide(row, "dot", SimVec3.Dot(first, second));
                Wide(row, "lengthSquaredA", first.LengthSquared);
                var value = SimVec3.Cross(first, second);
                Magnitude(cross[0].Text, value.X.Sign, value.X.RawWord);
                Magnitude(cross[1].Text, value.Y.Sign, value.Y.RawWord);
                Magnitude(cross[2].Text, value.Z.Sign, value.Z.RawWord);
            }
        }
    }
}
