using NUnit.Framework;
using static WarCore.Tests.E2GateTests;

namespace WarCore.Tests
{
    public sealed class Mt142MapProblemTests
    {
        [TestCase("GATE_REF", true)]
        [TestCase("GATE_LAYER", false)]
        public void ObjectEqualsComparesProblemValues(string code, bool expected)
        {
            var problem = P("GATE_REF", "unit 1", 2, 3, 2500, 3500, 7);
            object other = P(code, "unit 1", 2, 3, 2500, 3500, 7);
            Assert.That(problem.Equals(other), Is.EqualTo(expected));
        }

        [TestCase(true, "GATE_REF [unit 1] cell (2, 3) mm (2500, 3500) line 7")]
        [TestCase(false, "GATE_REF [unit 1] mm (2500, 3500) line 7")]
        public void ToStringIncludesCellOnlyWhenPresent(bool hasCell, string expected)
        {
            var problem = P("GATE_REF", "unit 1", hasCell ? 2 : (int?)null, 3, 2500, 3500, 7);
            Assert.That(problem.ToString(), Is.EqualTo(expected));
        }

        [TestCase(true, "MAPDESC_FORMAT line 7")]
        [TestCase(false, "MAPDESC_FORMAT")]
        public void ToStringIncludesLineOnlyWhenPresent(bool hasLine, string expected)
        {
            var problem = P("MAPDESC_FORMAT", line: hasLine ? 7 : (int?)null);
            Assert.That(problem.ToString(), Is.EqualTo(expected));
        }
    }
}
