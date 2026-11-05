using System;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    [Category("S3Wide")]
    public sealed class S3WideComparisonTests
    {
        private static SimWide Product(long a, long b) => SimVec3.Dot(
            new SimVec3(SimFix.FromRaw(a), default, default),
            new SimVec3(SimFix.FromRaw(b), default, default));

        [Test]
        public void SignedComparisonsRetainAll128MagnitudeBits()
        {
            long[] values = { long.MinValue, long.MinValue + 1, -4294967296L, -1, 0, 1, 4294967296L, long.MaxValue };
            Assert.That(typeof(IComparable<SimWide>).IsAssignableFrom(typeof(SimWide)), Is.True);
            foreach (long a in values)
                foreach (long b in values)
                {
                    SimWide left = Product(a, a);
                    SimWide right = Product(a, b);
                    int expected = ((BigInteger)a * a).CompareTo((BigInteger)a * b);
                    Assert.That(Comparer<SimWide>.Default.Compare(left, right), Is.EqualTo(expected));
                    foreach (string name in new[] { "op_LessThan", "op_LessThanOrEqual", "op_GreaterThan", "op_GreaterThanOrEqual", "op_Equality", "op_Inequality" })
                    {
                        MethodInfo method = typeof(SimWide).GetMethod(name, BindingFlags.Public | BindingFlags.Static);
                        Assert.That(method, Is.Not.Null, name);
                        bool answer = name == "op_LessThan" ? expected < 0 : name == "op_LessThanOrEqual" ? expected <= 0
                            : name == "op_GreaterThan" ? expected > 0 : name == "op_GreaterThanOrEqual" ? expected >= 0
                            : name == "op_Equality" ? expected == 0 : expected != 0;
                        Assert.That(method.Invoke(null, new object[] { left, right }), Is.EqualTo(answer));
                    }
                }
            SimWide zero = Product(0, long.MinValue);
            Assert.That(zero.Equals(default(SimWide)), Is.True);
            Assert.That(zero.GetHashCode(), Is.EqualTo(default(SimWide).GetHashCode()));
            Assert.That(Product(-3, 7).CompareToViaInterface(Product(-2, 7)), Is.LessThan(0));
        }

        [Test]
        public void BoundaryBiasedComparisonMatchesIndependentIntegerOrder()
        {
            Assert.That(typeof(IComparable<SimWide>).IsAssignableFrom(typeof(SimWide)), Is.True);
            var inputs = new BoundaryBiasedInputs(0x53300001);
            for (int i = 0; i < 20000; i++)
            {
                inputs.Next("mul", i % 2 == 0 ? "endpoints" : "powers", i, out long a, out long b);
                int expected = ((BigInteger)a * b).CompareTo((BigInteger)a * a);
                Assert.That(Comparer<SimWide>.Default.Compare(Product(a, b), Product(a, a)), Is.EqualTo(expected));
            }
            // This positive control proves a changed order is observable through the same comparison path.
            Assert.That(Comparer<SimWide>.Default.Compare(Product(-1, 1), Product(1, 1)), Is.Not.EqualTo(1));
        }
    }

    internal static class S3WideTestComparison
    {
        internal static int CompareToViaInterface(this SimWide value, SimWide other)
            => ((IComparable<SimWide>)(object)value).CompareTo(other);
    }
}
