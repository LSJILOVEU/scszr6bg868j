using System;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using NUnit.Framework;
using WarCore.Sim;
using WarCore.Tests.Oracle;

namespace WarCore.Tests
{
    public sealed class A2GeometryIntegerTests
    {
        // Reflection tests full storage endpoints without adding a product API or validation bypass.
        private sealed class Arithmetic
        {
            internal readonly Type Type = typeof(SimFix).Assembly.GetType("WarCore.Sim.GeometryInt288", true);
            private readonly FieldInfo[] words = new FieldInfo[9];
            private readonly FieldInfo length;
            private readonly FieldInfo negative;

            internal Arithmetic()
            {
                for (int i = 0; i < words.Length; i++)
                {
                    words[i] = Type.GetField("w" + i, BindingFlags.NonPublic | BindingFlags.Instance);
                    Assert.That(Marshal.OffsetOf(Type, words[i].Name).ToInt32(), Is.EqualTo(i * 4));
                }

                length = Type.GetField("length", BindingFlags.NonPublic | BindingFlags.Instance);
                negative = Type.GetField("negative", BindingFlags.NonPublic | BindingFlags.Instance);
            }

            internal object Box(BigInteger value)
            {
                object result = Activator.CreateInstance(Type);
                negative.SetValue(result, value.Sign < 0);
                value = BigInteger.Abs(value);
                int used = 0;
                for (int i = 0; i < words.Length; i++)
                {
                    uint word = (uint)(value & uint.MaxValue);
                    words[i].SetValue(result, word);
                    if (word != 0)
                        used = i + 1;
                    value >>= 32;
                }

                Assert.That(value, Is.EqualTo(BigInteger.Zero));
                length.SetValue(result, used);
                return result;
            }

            internal BigInteger Read(object value)
            {
                BigInteger result = 0;
                int used = 0;
                for (int i = words.Length - 1; i >= 0; i--)
                {
                    uint word = (uint)words[i].GetValue(value);
                    if (used == 0 && word != 0)
                        used = i + 1;
                    result = (result << 32) + word;
                }

                Assert.That(length.GetValue(value), Is.EqualTo(used));
                if ((bool)negative.GetValue(value))
                {
                    Assert.That(result.IsZero, Is.False, "negative zero");
                    result = -result;
                }

                return result;
            }

            internal object Invoke(string method, object receiver, params object[] arguments)
            {
                try
                {
                    return Type.GetMethod(method, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).Invoke(receiver, arguments);
                }
                catch (TargetInvocationException error)
                {
                    throw error.InnerException;
                }
            }

            internal bool Check(string operation, BigInteger a, BigInteger b, string context)
            {
                object first = Box(a);
                object second = Box(b);
                if (operation == "op_Division" && b.IsZero)
                {
                    Assert.Throws<SimDivideByZeroException>(() => Invoke(operation, null, first, second), context);
                    return true;
                }

                BigInteger expected = operation == "op_Addition" ? a + b : operation == "op_Subtraction" ? a - b : operation == "op_Multiply" ? a * b : a / b;
                if (BigInteger.Abs(expected) >= (BigInteger.One << 288))
                {
                    Assert.Throws<SimOverflowException>(() => Invoke(operation, null, first, second), context);
                    return true;
                }

                Assert.That(Read(Invoke(operation, null, first, second)), Is.EqualTo(expected), context);
                return false;
            }
        }

        private static BigInteger Sample(ref A1MathInputs random, int bits)
        {
            BigInteger result = 0;
            for (int i = 0; i < bits; i += 64)
                result = (result << 64) + random.Next();
            return result & ((BigInteger.One << bits) - 1);
        }

        private static BigInteger OracleSqrt(BigInteger value)
        {
            WideInt input = default;
            WideInt radix = WideInt.From(1L << 32);
            for (int word = 8; word >= 0; word--)
                input = input * radix + WideInt.From((long)((value >> (word * 32)) & uint.MaxValue));
            WideInt root = input.Sqrt();
            BigInteger result = 0;
            for (int word = 8; word >= 0; word--)
                result = (result << 32) + root.Word(word);
            return result;
        }

        [Test]
        public void Full288BitArithmeticMatchesIndependentIntegersAndFrozenSqrt()
        {
            const ulong seed = 0xA2288001UL;
            var random = new A1MathInputs(seed);
            var arithmetic = new Arithmetic();
            int exceptions = 0;
            for (int i = 0; i < 20000; i++)
            {
                int bits = 1 + (int)(random.Next() % 288);
                BigInteger a = Sample(ref random, bits);
                int otherBits = i % 3 == 0 ? 289 - bits : 1 + (int)(random.Next() % 288);
                BigInteger b = Sample(ref random, otherBits);
                if (i % 7 == 0)
                    a = (BigInteger.One << (bits - 1)) + i % 3 - 1;
                if (i % 11 == 0)
                    b = (BigInteger.One << otherBits) - 1;
                if ((i & 1) != 0)
                    a = -a;
                if ((i & 2) != 0)
                    b = -b;
                if (i % 101 == 0)
                    b = 0;
                string context = "seed=" + seed + " case=" + i + " a=" + a + " b=" + b;
                foreach (string operation in new[]{"op_Addition", "op_Subtraction", "op_Multiply", "op_Division"})
                    exceptions += arithmetic.Check(operation, a, b, operation + " " + context) ? 1 : 0;
                BigInteger magnitude = BigInteger.Abs(a);
                object root = arithmetic.Invoke("Sqrt", arithmetic.Box(magnitude));
                Assert.That(arithmetic.Read(root), Is.EqualTo(OracleSqrt(magnitude)), "sqrt " + context);
                int actualComparison = (int)arithmetic.Invoke("CompareTo", arithmetic.Box(a), arithmetic.Box(b));
                Assert.That(Math.Sign(actualComparison), Is.EqualTo(a.CompareTo(b)), "compare " + context);
            }

            TestContext.WriteLine("A2 GeometryInt288: seed=0x{0:X8}, 20000 pairs, 80000 add/subtract/multiply/divide checks, 20000 frozen-oracle sqrt checks, 20000 comparisons, expected arithmetic exceptions={1}, mismatches=0; all nine limb offsets verified.", seed, exceptions);
        }

        [Test]
        public void EveryLimbCarriesBorrowsAndSquareNeighboursRemainExact()
        {
            var arithmetic = new Arithmetic();
            BigInteger maximum = (BigInteger.One << 288) - 1;
            int cases = 0;
            for (int bit = 1; bit <= 288; bit++)
            {
                BigInteger below = (BigInteger.One << bit) - 1;
                arithmetic.Check("op_Addition", below, 1, "carry " + bit);
                arithmetic.Check("op_Subtraction", maximum, below, "borrow " + bit);
                arithmetic.Check("op_Subtraction", below, below, "cancel " + bit);
                arithmetic.Check("op_Multiply", below, below, "product " + bit);
                arithmetic.Check("op_Division", maximum, below, "divide " + bit);
                cases += 5;
                if (bit < 288)
                {
                    arithmetic.Check("op_Subtraction", BigInteger.One << bit, 1, "propagated borrow " + bit);
                    cases++;
                }
            }

            for (int bit = 1; bit <= 144; bit++)
            {
                BigInteger root = (BigInteger.One << bit) - 1;
                BigInteger square = root * root;
                for (int delta = -1; delta <= 1; delta++)
                {
                    BigInteger value = square + delta;
                    BigInteger expected = delta == -1 ? root - 1 : root;
                    Assert.That(arithmetic.Read(arithmetic.Invoke("Sqrt", arithmetic.Box(value))), Is.EqualTo(expected), "square bit=" + bit + " delta=" + delta);
                    cases++;
                }
            }

            Assert.Throws<ArgumentOutOfRangeException>(() => arithmetic.Invoke("Sqrt", arithmetic.Box(-1)));
            Assert.That(arithmetic.Read(arithmetic.Invoke("Sqrt", arithmetic.Box(maximum))), Is.EqualTo((BigInteger.One << 144) - 1));
            arithmetic.Check("op_Addition", -maximum, -1, "negative magnitude overflow");
            foreach (long value in new[]{long.MinValue, long.MinValue + 1, -1L, 0L, 1L, long.MaxValue})
            {
                object number = arithmetic.Invoke("From", null, value);
                Assert.That(arithmetic.Read(number), Is.EqualTo(new BigInteger(value)));
                Assert.That(arithmetic.Invoke("ToLong", number), Is.EqualTo(value));
            }

            Assert.Throws<SimOverflowException>(() => arithmetic.Invoke("ToLong", arithmetic.Box(BigInteger.One << 63)));
            Assert.Throws<SimOverflowException>(() => arithmetic.Invoke("ToLong", arithmetic.Box(-(BigInteger.One << 63) - 1)));
            TestContext.WriteLine("A2 GeometryInt288 boundaries: {0} carry/borrow/cancel/product/divide/square-neighbour checks; negative sqrt and signed long conversion endpoints verified.", cases);
        }

        [Test]
        public void WordDivisionCoversNormalizationSaturationAndAddBack()
        {
            var arithmetic = new Arithmetic();
            BigInteger radix = BigInteger.One << 32;
            int cases = 0;
            for (int shift = 0; shift < 32; shift++)
            {
                BigInteger divisor = ((BigInteger.One << (31 - shift)) << 64) + 1;
                foreach (BigInteger quotient in new[]{BigInteger.One, radix - 1, radix, radix + 1, radix * radix - 1})
                {
                    foreach (BigInteger remainder in new[]{BigInteger.Zero, BigInteger.One, divisor - 1})
                    {
                        arithmetic.Check("op_Division", divisor * quotient + remainder, divisor, "normalization=" + shift);
                        cases++;
                    }
                }
            }

            BigInteger addBackDivisor = (BigInteger.One << 95) + 1;
            arithmetic.Check("op_Division", 2 * addBackDivisor - 1, addBackDivisor, "D6 add-back after lower-word borrow");
            BigInteger saturatedDivisor = (BigInteger.One << 63) + 1;
            arithmetic.Check("op_Division", saturatedDivisor * radix - 1, saturatedDivisor, "top numerator word equals divisor top");
            arithmetic.Check("op_Division", BigInteger.Parse("37545944571153338352496107710"), BigInteger.Parse("9741231941167204904"), "D3 two estimate corrections");
            TestContext.WriteLine("A2 word division: {0} normalization/quotient/remainder cases (all 32 shifts) plus explicit saturated estimate, two D3 corrections and D6 add-back cases.", cases);
        }

        [Test]
        public void SectorUpperBoundsRequireNineWordsAndLeaveCarryHeadroom()
        {
            var arithmetic = new Arithmetic();
            BigInteger world = BigInteger.One << 30;
            BigInteger trigonometricScale = 1000 * (BigInteger.One << 32);
            BigInteger excess = 9 * world * world;
            BigInteger direction2 = 2 * world * world * trigonometricScale * trigonometricScale;
            BigInteger endpoint = excess * excess * direction2;
            Assert.That(endpoint, Is.GreaterThan(BigInteger.One << 256));
            Assert.That(endpoint, Is.LessThan(BigInteger.One << 272));
            arithmetic.Check("op_Multiply", excess, excess, "sector excess square bound");
            arithmetic.Check("op_Multiply", excess * excess, direction2, "sector endpoint bound");
            BigInteger radix = BigInteger.One << 32;
            Assert.That((radix - 1) * (radix - 1) + 2 * (radix - 1), Is.EqualTo((BigInteger.One << 64) - 1));
            TestContext.WriteLine("A2 width proof checks: sector excess^2*d2 bound=162000000*2^244 (<2^272, >2^256); nine words required; worst multiply temporary=(2^32-1)^2+2*(2^32-1)=2^64-1.");
        }
    }
}
