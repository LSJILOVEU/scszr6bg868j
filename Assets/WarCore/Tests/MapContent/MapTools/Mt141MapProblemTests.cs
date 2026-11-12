using NUnit.Framework;
using WarCore.Content.MapTools;

namespace WarCore.Tests
{
    public sealed class Mt141MapProblemTests
    {
        [Test]
        public void ConstructorWithoutOptionalPayload()
        {
            var problem = new MapProblem("GATE_MATCH");
            Assert.That(problem.HasMm, Is.False);
            Assert.That(problem.HasLine, Is.False);
        }

        [Test]
        public void ConstructorPreservesMillimetres()
        {
            var problem = new MapProblem("GATE_REF", mm: new MapMm(-2500, 4500));
            Assert.That(problem.HasMm, Is.True);
            Assert.That(problem.XMm, Is.EqualTo(-2500L));
            Assert.That(problem.ZMm, Is.EqualTo(4500L));
        }

        [Test]
        public void ConstructorPreservesLine()
        {
            var problem = new MapProblem("MAPDESC_FORMAT", line: 7);
            Assert.That(problem.HasLine, Is.True);
            Assert.That(problem.Line, Is.EqualTo(7));
        }
    }
}
