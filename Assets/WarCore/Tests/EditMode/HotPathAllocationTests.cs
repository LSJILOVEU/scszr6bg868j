using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace WarCore.Tests
{
    public sealed class HotPathAllocationTests
    {
        [Test]
        public void TickQueryMovementAndHashReachableMethodsRejectAllocations()
        {
            MethodBase[] methods = HotPathAllocationRules.HotMethods().ToArray();
            Assert.That(methods.Length, Is.GreaterThan(100), "An empty or narrowed scan is not evidence.");
            string[] failures = methods.SelectMany(HotPathAllocationRules.Violations).ToArray();
            Assert.That(failures, Is.Empty);
            TestContext.WriteLine("Hot-path IL scan: methods={0}; reference new, arrays, boxing, LINQ, delegate/closure, params and allocating string/encoding calls checked.", methods.Length);
        }

        [TestCase(nameof(ArrayAllocation), "array allocation")]
        [TestCase(nameof(ObjectAllocation), "reference allocation")]
        [TestCase(nameof(BoxAllocation), "boxing")]
        [TestCase(nameof(LinqAllocation), "LINQ")]
        [TestCase(nameof(ClosureAllocation), "delegate/closure construction")]
        [TestCase(nameof(ParamsAllocation), "params call")]
        public void EveryAllocationRuleRejectsACompiledCounterexample(string name, string expected)
        {
            MethodInfo method = typeof(HotPathAllocationTests).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(HotPathAllocationRules.Violations(method).Any(text => text.Contains(expected)), Is.True, name);
        }

        [Test]
        public void ValueConstructionAndExplicitErrorBranchesStayAllowed()
        {
            MethodInfo method = typeof(HotPathAllocationTests).GetMethod(nameof(ValueOrThrow), BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(HotPathAllocationRules.Violations(method), Is.Empty);
        }

        private static object ArrayAllocation() => new byte[64];
        private static object ObjectAllocation() => new object();
        private static object BoxAllocation(int value) => value;
        private static object LinqAllocation(int[] values) => values.Select(value => value + 1);
        private static object ClosureAllocation(int value) => (Func<int>)(() => value);
        private static object ParamsAllocation() => ParamsTarget(1, 2);
        private static object ParamsTarget(params int[] values) => values;
        private static int ValueOrThrow(int value)
        {
            if (value < 0)
                throw new ArgumentOutOfRangeException(nameof(value));
            return new WarCore.Sim.SimVec3(default, default, default).X.Raw == 0 ? value : 0;
        }
    }
}
