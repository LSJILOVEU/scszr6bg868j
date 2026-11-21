using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    // BASE-0 V1-V12: the stat line. result = sign(base) * floor(|base| * product(factors) / 1000^n); Apply clamps only the drags.
    public sealed class Base0StatLineTests
    {
        private static int[] Many(int value, int count) => Enumerable.Repeat(value, count).ToArray();
        private static readonly int[] None = new int[0];

        // Dividing after every factor: the order-dependent rounding the stat line must not do.
        private static long DivideEachTime(long value, int[] factors)
        {
            foreach (int factor in factors)
                value = value * factor / 1000;
            return value;
        }

        [Test]
        public void V1_NoFactorsReturnTheBase()
        {
            Assert.That(StatLine.Scale(6553, None), Is.EqualTo(6553));
            Assert.That(StatLine.Apply(6553, None, None, 400, 0), Is.EqualTo(6553), "no drags, no clamp");
            Assert.That(StatLine.Apply(-6553, None, None, 400, 1000), Is.EqualTo(-6553));
        }

        [Test]
        public void V2_OneFactor()
        {
            Assert.That(StatLine.Scale(6553, new[] { 500 }), Is.EqualTo(3276), "3276.5 rounds down");
            Assert.That(StatLine.Scale(6553, new[] { 250 }), Is.EqualTo(1638));
            Assert.That(StatLine.Scale(6553, new[] { 1000 }), Is.EqualTo(6553));
            Assert.That(StatLine.Scale(6553, new[] { 1 }), Is.EqualTo(6));
            Assert.That(StatLine.Scale(1, new[] { 4000 }), Is.EqualTo(4));
        }

        [Test]
        public void V3_UpperBounds()
        {
            Assert.That(StatLine.Scale(2147483647, Many(4000, 5)), Is.EqualTo(2199023254528L));
            Assert.That(StatLine.Scale(2147483647, Many(4000, 6)), Is.EqualTo(8796093018112L));
            Assert.That(StatLine.Scale(2147483647, Many(4000, 7)), Is.EqualTo(35184372072448L));
            Assert.That(35184372072448L, Is.EqualTo((1L << 45) - 16384));
            Assert.That(StatLine.Scale(-2147483647, Many(4000, 7)), Is.EqualTo(-35184372072448L));
            Assert.That(StatLine.Apply(2147483647, Many(4000, 3), Many(4000, 4), 0, 0), Is.EqualTo(35184372072448L));
            Assert.That(StatLine.Apply(2147483647, Many(4000, 3), Many(4000, 4), 0, 4000), Is.EqualTo(549755813632L), "the drags are cut to one 4000");
        }

        [Test]
        public void V3b_SevenFactorsThatDoNotDivide()
        {
            Assert.That(StatLine.Scale(2147483647, Many(999, 7)), Is.EqualTo(2132496283L));
            Assert.That(StatLine.Scale(-2147483647, Many(999, 7)), Is.EqualTo(-2132496283L));
        }

        [Test]
        public void V3c_SevenDifferentFactors()
        {
            int[] factors = { 1001, 999, 1337, 2500, 3999, 7, 4000 };
            Assert.That(StatLine.Scale(123456789, factors), Is.EqualTo(46205683L));
            long eachTime = DivideEachTime(123456789, factors);
            Assert.That(eachTime, Is.EqualTo(46205680L));
            Assert.That(eachTime, Is.Not.EqualTo(46205683L), "the fixture separates one division from one per factor");
        }

        [Test]
        public void V4_DividesOnce()
        {
            Assert.That(StatLine.Scale(9, new[] { 800, 700 }), Is.EqualTo(5), "9 * 0.56 = 5.04");
            long eachTime = DivideEachTime(9, new[] { 800, 700 });
            Assert.That(eachTime, Is.EqualTo(4));
            Assert.That(eachTime, Is.Not.EqualTo(5));
        }

        private static IEnumerable<int[]> Permutations(int[] items)
        {
            if (items.Length <= 1)
            {
                yield return items;
                yield break;
            }
            for (int i = 0; i < items.Length; i++)
            {
                int[] rest = items.Where((_, j) => j != i).ToArray();
                foreach (int[] tail in Permutations(rest))
                    yield return new[] { items[i] }.Concat(tail).ToArray();
            }
        }

        [TestCase(9)]
        [TestCase(13)]
        [TestCase(6553)]
        public void V5_OrderDoesNotMatter(long baseValue)
        {
            int[][] orders = Permutations(new[] { 800, 700, 1250 }).ToArray();
            Assert.That(orders.Length, Is.EqualTo(6));
            Assert.That(orders.Select(o => string.Join(",", o)).Distinct().Count(), Is.EqualTo(6));
            long expected = baseValue * 800 * 700 * 1250 / 1000000000L;
            foreach (int[] order in orders)
            {
                Assert.That(StatLine.Scale(baseValue, order), Is.EqualTo(expected), string.Join(",", order));
                Assert.That(StatLine.Apply(baseValue, new[] { order[0] }, new[] { order[1], order[2] }, 0, 0), Is.EqualTo(expected));
            }
        }

        [Test]
        public void V6_NegativeValuesMatchTheSurfaceArithmetic()
        {
            var bases = new List<long>();
            for (long b = -16384; b <= 16384; b += 8)
                bases.Add(b);
            bases.AddRange(new long[] { 1, -1, 7, -7 });
            Assert.That(bases.Count, Is.EqualTo(4101));
            int checkedCount = 0;
            foreach (int milli in new[] { 500, 700, 800, 900, 1000, 1250 })
                foreach (long b in bases)
                {
                    long expected = b * milli / 1000;
                    long actual = StatLine.Scale(b, new[] { milli });
                    if (actual != expected)
                        Assert.Fail("base " + b + " milli " + milli + ": " + actual + " != " + expected);
                    checkedCount++;
                }
            Assert.That(checkedCount, Is.EqualTo(4101 * 6));
            Assert.That(StatLine.Scale(-7, new[] { 500 }), Is.EqualTo(-3), "toward zero, not down");
        }

        [Test]
        public void V7_Floor()
        {
            int[] drags = { 800, 700, 850, 700 };
            Assert.That(StatLine.Apply(10000, None, drags, 0, 0), Is.EqualTo(3332), "unclamped");
            Assert.That(StatLine.Apply(10000, None, drags, 400, 0), Is.EqualTo(4000));
            Assert.That(StatLine.Apply(10000, None, drags, 300, 0), Is.EqualTo(3332));
            Assert.That(StatLine.Apply(-10000, None, drags, 400, 0), Is.EqualTo(-4000));
        }

        [Test]
        public void V8_Ceiling()
        {
            int[] drags = { 1250, 1500 };
            Assert.That(StatLine.Apply(10000, None, drags, 0, 0), Is.EqualTo(18750), "unclamped");
            Assert.That(StatLine.Apply(10000, None, drags, 0, 1500), Is.EqualTo(15000));
            Assert.That(StatLine.Apply(10000, None, drags, 0, 2000), Is.EqualTo(18750));
            Assert.That(StatLine.Apply(10000, None, drags, 400, 1500), Is.EqualTo(15000), "a floor too");
        }

        [Test]
        public void V9_GearsAreNotClamped()
        {
            Assert.That(StatLine.Apply(10000, new[] { 250 }, new[] { 800 }, 400, 0), Is.EqualTo(2000));
            Assert.That(StatLine.Apply(10000, new[] { 250 }, new[] { 300 }, 400, 0), Is.EqualTo(1000));
            Assert.That(StatLine.Apply(10000, new[] { 250 }, None, 400, 0), Is.EqualTo(2500), "a low gear alone is never raised");
            Assert.That(StatLine.Apply(10000, new[] { 3000 }, new[] { 1000 }, 0, 1500), Is.EqualTo(30000), "a high gear is never cut");
        }

        [Test]
        public void V9b_FloorOfSevenDrags()
        {
            Assert.That(StatLine.Apply(10000, None, Many(900, 7), 500, 0), Is.EqualTo(5000));
            Assert.That(StatLine.Apply(10000, None, Many(900, 7), 400, 0), Is.EqualTo(4782));
            Assert.That(StatLine.Explain(10000, None, Many(900, 7), 478, 0).Clamp, Is.EqualTo(StatClamp.None), "0.4783 is above 0.478");
            Assert.That(StatLine.Explain(10000, None, Many(900, 7), 479, 0).Clamp, Is.EqualTo(StatClamp.Floor), "0.4783 is below 0.479");
            Assert.That(StatLine.Apply(10000, None, Many(1100, 7), 0, 1948), Is.EqualTo(19480), "1.1^7 = 1.9487 is above 1.948");
            Assert.That(StatLine.Explain(10000, None, Many(1100, 7), 0, 1949).Clamp, Is.EqualTo(StatClamp.None), "1.9487 is below 1.949");
        }

        [Test]
        public void V10_ExactlyAtTheFloor()
        {
            StatReceipt at = StatLine.Explain(10000, None, new[] { 500, 800 }, 400, 0);
            Assert.That((at.Result, at.Clamp, at.ClampPermille), Is.EqualTo((4000L, StatClamp.None, 0)));
            StatReceipt below = StatLine.Explain(10000, None, new[] { 500, 799 }, 400, 0);
            Assert.That((below.Result, below.Clamp, below.ClampPermille), Is.EqualTo((4000L, StatClamp.Floor, 400)));
            StatReceipt atCeil = StatLine.Explain(10000, None, new[] { 1250, 1200 }, 0, 1500);
            Assert.That((atCeil.Result, atCeil.Clamp), Is.EqualTo((15000L, StatClamp.None)));
            StatReceipt aboveCeil = StatLine.Explain(10000, None, new[] { 1250, 1201 }, 0, 1500);
            Assert.That((aboveCeil.Result, aboveCeil.Clamp, aboveCeil.ClampPermille), Is.EqualTo((15000L, StatClamp.Ceil, 1500)));
        }

        private static void Rejects(TestDelegate call, string parameter)
        {
            var error = Assert.Throws<ArgumentOutOfRangeException>(call);
            Assert.That(error.ParamName, Is.EqualTo(parameter));
        }

        [Test]
        public void V11_Rejections()
        {
            foreach (int bad in new[] { 0, 4001, -1 })
            {
                Rejects(() => StatLine.Scale(100, new[] { bad }), "factors");
                Rejects(() => StatLine.Apply(100, new[] { bad }, None, 0, 0), "gears");
                Rejects(() => StatLine.Apply(100, None, new[] { bad }, 0, 0), "drags");
            }
            Assert.That(StatLine.Scale(1000, new[] { 1, 4000 }), Is.EqualTo(4), "1 and 4000 are valid");
            Rejects(() => StatLine.Scale(2147483648L, None), "baseValue");
            Rejects(() => StatLine.Scale(-2147483648L, None), "baseValue");
            Rejects(() => StatLine.Apply(2147483648L, None, None, 0, 0), "baseValue");
            Rejects(() => StatLine.Apply(-2147483648L, None, None, 0, 0), "baseValue");
            Rejects(() => StatLine.Scale(long.MinValue, new[] { 1000 }), "baseValue");
            Assert.That(StatLine.Scale(-2147483647L, None), Is.EqualTo(-2147483647L));
            Rejects(() => StatLine.Scale(1, Many(1000, 8)), "factors");
            Assert.That(StatLine.Scale(1, Many(1000, 7)), Is.EqualTo(1));
            Rejects(() => StatLine.Apply(1, Many(1000, 4), Many(1000, 4), 0, 0), "drags");
            Rejects(() => StatLine.Apply(1, Many(1000, 8), None, 0, 0), "gears");
            Rejects(() => StatLine.Apply(1, None, Many(1000, 8), 0, 0), "drags");
            Assert.That(StatLine.Apply(1, Many(1000, 4), Many(1000, 3), 0, 0), Is.EqualTo(1), "4 + 3 is valid");
            Rejects(() => StatLine.Apply(1, None, new[] { 1000 }, 4001, 0), "dragFloorPermille");
            Rejects(() => StatLine.Apply(1, None, new[] { 1000 }, -1, 0), "dragFloorPermille");
            Rejects(() => StatLine.Apply(1, None, new[] { 1000 }, 0, 4001), "dragCeilPermille");
            Rejects(() => StatLine.Apply(1, None, new[] { 1000 }, 0, -1), "dragCeilPermille");
            Rejects(() => StatLine.Apply(1, None, new[] { 1000 }, 500, 400), "dragFloorPermille");
            Rejects(() => StatLine.Apply(1, None, None, 4001, 0), "dragFloorPermille");
            Assert.That(StatLine.Apply(1000, None, new[] { 1000 }, 400, 400), Is.EqualTo(400), "floor equal to ceiling is valid");
            Assert.That(StatLine.Apply(1000, None, new[] { 1000 }, 4000, 0), Is.EqualTo(4000), "a floor of 4000 is valid");
            Assert.That(StatLine.Apply(1000, None, new[] { 4000 }, 0, 1), Is.EqualTo(1), "a ceiling of 1 is valid");
            Rejects(() => StatLine.Explain(1, None, new[] { 0 }, 0, 0), "drags");
        }

        [Test]
        public void V12_ReceiptsOfV7ToV9()
        {
            AssertReceipt(StatLine.Explain(10000, None, new[] { 800, 700, 850, 700 }, 400, 0),
                10000, None, new[] { 800, 700, 850, 700 }, 400, 0, StatClamp.Floor, 400, 4000);
            AssertReceipt(StatLine.Explain(10000, None, new[] { 1250, 1500 }, 0, 1500),
                10000, None, new[] { 1250, 1500 }, 0, 1500, StatClamp.Ceil, 1500, 15000);
            AssertReceipt(StatLine.Explain(10000, new[] { 250 }, new[] { 800 }, 400, 0),
                10000, new[] { 250 }, new[] { 800 }, 400, 0, StatClamp.None, 0, 2000);
            AssertReceipt(StatLine.Explain(10000, new[] { 250 }, new[] { 300 }, 400, 0),
                10000, new[] { 250 }, new[] { 300 }, 400, 0, StatClamp.Floor, 400, 1000);
            AssertReceipt(StatLine.Explain(6553, None, None, 400, 0), 6553, None, None, 400, 0, StatClamp.None, 0, 6553);
        }

        private static void AssertReceipt(StatReceipt receipt, long baseValue, int[] gears, int[] drags, int floor, int ceil,
            StatClamp clamp, int clampPermille, long result)
        {
            Assert.That(receipt.Base, Is.EqualTo(baseValue));
            Assert.That(receipt.Gears, Is.EqualTo(gears));
            Assert.That(receipt.Drags, Is.EqualTo(drags));
            Assert.That(receipt.DragFloorPermille, Is.EqualTo(floor));
            Assert.That(receipt.DragCeilPermille, Is.EqualTo(ceil));
            Assert.That(receipt.Clamp, Is.EqualTo(clamp));
            Assert.That(receipt.ClampPermille, Is.EqualTo(clampPermille));
            Assert.That(receipt.Result, Is.EqualTo(result));
            Assert.That(StatLine.Apply(baseValue, gears, drags, floor, ceil), Is.EqualTo(result));
        }

        [Test]
        public void V12_ReceiptCopiesItsInputs()
        {
            int[] drags = { 800 };
            StatReceipt receipt = StatLine.Explain(100, None, drags, 0, 0);
            drags[0] = 900;
            Assert.That(receipt.Drags, Is.EqualTo(new[] { 800 }));
        }

        // A small linear congruential generator (MMIX constants), so the cases are the same everywhere.
        private sealed class Lcg
        {
            private ulong state;
            internal Lcg(ulong seed) => state = seed;
            internal uint Next()
            {
                state = unchecked(state * 6364136223846793005UL + 1442695040888963407UL);
                return (uint)(state >> 33);
            }
            internal int Range(int low, int high) => low + (int)(Next() % (uint)(high - low + 1));
        }

        // The reference: the same rule in arbitrary precision.
        private static long Reference(long baseValue, int[] gears, int[] drags, int floor, int ceil, out StatClamp clamp)
        {
            clamp = StatClamp.None;
            var factors = new List<int>(gears);
            if (drags.Length > 0)
            {
                BigInteger product = BigInteger.One;
                foreach (int d in drags)
                    product *= d;
                BigInteger scale = BigInteger.Pow(1000, drags.Length);
                if (floor != 0 && product * 1000 < floor * scale)
                    clamp = StatClamp.Floor;
                else if (ceil != 0 && product * 1000 > ceil * scale)
                    clamp = StatClamp.Ceil;
                if (clamp == StatClamp.None)
                    factors.AddRange(drags);
                else
                    factors.Add(clamp == StatClamp.Floor ? floor : ceil);
            }
            BigInteger magnitude = BigInteger.Abs(baseValue);
            foreach (int f in factors)
                magnitude *= f;
            magnitude /= BigInteger.Pow(1000, factors.Count);
            return (long)(baseValue < 0 ? -magnitude : magnitude);
        }

        [Test]
        public void V12_RandomReceiptsMatchApplyAndAWideReference()
        {
            var random = new Lcg(20261121);
            var perCount = new int[8];
            int clampedFloor = 0, clampedCeil = 0;
            for (int i = 0; i < 1000; i++)
            {
                int count = i < 200 ? 7 : random.Range(0, 7);
                perCount[count]++;
                int gearCount = random.Range(0, count);
                int[] gears = new int[gearCount], drags = new int[count - gearCount];
                for (int g = 0; g < gears.Length; g++)
                    gears[g] = random.Range(1, 4000);
                for (int d = 0; d < drags.Length; d++)
                    drags[d] = random.Range(i % 3 == 0 ? 1 : 500, i % 3 == 0 ? 4000 : 1500);
                int floor = random.Range(0, 3) == 0 ? 0 : random.Range(1, 1000);
                int ceil = random.Range(0, 3) == 0 ? 0 : random.Range(floor == 0 ? 1 : floor, 4000);
                long baseValue = (long)random.Range(0, int.MaxValue) * (random.Range(0, 1) == 0 ? 1 : -1);
                if (i % 50 == 0)
                    baseValue = i % 100 == 0 ? StatLine.MaxBase : -StatLine.MaxBase;
                string label = "case " + i;
                StatReceipt receipt = StatLine.Explain(baseValue, gears, drags, floor, ceil);
                long expected = Reference(baseValue, gears, drags, floor, ceil, out StatClamp clamp);
                Assert.That(receipt.Result, Is.EqualTo(StatLine.Apply(baseValue, gears, drags, floor, ceil)), label);
                Assert.That(receipt.Result, Is.EqualTo(expected), label);
                Assert.That(receipt.Clamp, Is.EqualTo(clamp), label);
                if (gearCount == 0 && drags.Length > 0 && floor == 0 && ceil == 0)
                    Assert.That(StatLine.Scale(baseValue, drags), Is.EqualTo(expected), label);
                if (clamp == StatClamp.Floor) clampedFloor++;
                if (clamp == StatClamp.Ceil) clampedCeil++;
            }
            for (int count = 0; count <= 7; count++)
                Assert.That(perCount[count], Is.GreaterThan(0), "count " + count);
            Assert.That(perCount[7], Is.GreaterThanOrEqualTo(100));
            Assert.That(clampedFloor, Is.GreaterThan(20));
            Assert.That(clampedCeil, Is.GreaterThan(20));
        }

        [Test]
        public void Constants()
        {
            Assert.That((StatLine.MinPermille, StatLine.MaxPermille, StatLine.MaxFactors, StatLine.MaxBase),
                Is.EqualTo((1, 4000, 7, 2147483647L)));
            Assert.That(((int)StatClamp.None, (int)StatClamp.Floor, (int)StatClamp.Ceil), Is.EqualTo((0, 1, 2)));
        }
    }
}
