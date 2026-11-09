using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace WarCore.Tests
{
    // F2a: every guard keeps its exception type and its full message, and the guards keep firing in the same order.
    public sealed class AssemblyGuardTests
    {
        private static IEnumerable<string> CaseNames() => AssemblyGuardCases.Cases.Select(c => c.Name);

        [TestCaseSource(nameof(CaseNames))]
        public void Guard_KeepsItsExceptionAndMessage(string name)
        {
            string recorded = AssemblyGuardGolden.Lines.Single(l => l.StartsWith(name + "|"));
            string actual = name + "|" + AssemblyGuardCases.Outcome(AssemblyGuardCases.Cases.Single(c => c.Name == name).Run);
            Assert.That(actual, Is.EqualTo(recorded));
        }

        [Test]
        public void EveryCaseIsRecorded_AndEveryGuardKindIsCovered()
        {
            Assert.That(AssemblyGuardGolden.Lines.Length, Is.EqualTo(AssemblyGuardCases.Cases.Length));
            string all = string.Join("\n", AssemblyGuardGolden.Lines);
            foreach (string piece in new[]
            {
                "Unsupported mover", "require map bytes", "Battle requires combat and crowd movement", "Player control requires InterimCrowd",
                "Terrain maps (height field or surface table) need a path or crowd mover",
                "A terrain map needs a content pack with terrain rules", "Wall layers must not share the mover layer",
                "is not on standable ground", "Player key does not match", "Player key must name a deployed unit",
                "A crowd pass rule requires crowd movement and combat", "The block AI needs combat with block specs and facing",
            })
                Assert.That(all, Does.Contain(piece), piece);
            Assert.That(AssemblyGuardGolden.Lines.Count(l => l.EndsWith("|OK")), Is.EqualTo(3));
        }
    }
}
