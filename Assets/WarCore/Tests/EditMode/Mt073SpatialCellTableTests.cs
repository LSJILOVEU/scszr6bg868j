using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    [Category("Step2bB")]
    public sealed class Mt073SpatialCellTableTests
    {
        [Test]
        public void RejectsNewCellAtHalfCapacityWithSpareMembers()
        {
            var table = new SpatialCellTable(4, 4, false);
            table.CountMember(0, 0, 0, 0);
            table.CountMember(1, 0, 0, 0);
            Assert.That(table.Count, Is.EqualTo(2));
            Assert.That(table.MemberCount, Is.EqualTo(2));
            Assert.Throws<SimBoundaryException>(() => table.CountMember(2, 0, 0, 0));
        }

        [TestCase(false, 1)]
        [TestCase(true, 0)]
        public void LookupCountsFirstProbeAndOnlyMissesAsEmpty(bool occupied, int emptyLookups)
        {
            var table = new SpatialCellTable(4, 4, false);
            if (occupied)
                table.CountMember(0, 0, 0, 0);
            var diagnostics = new SpatialQueryDiagnostics();
            table.Find(0, 0, 0, 0, diagnostics);
            Assert.That(diagnostics.DictionaryLookups, Is.EqualTo(1));
            Assert.That(diagnostics.HashProbes, Is.EqualTo(1));
            Assert.That(diagnostics.EmptyLookups, Is.EqualTo(emptyLookups));
        }

        [Test]
        public void UsedBytesCountsOccupiedCellsAndMembers()
        {
            var table = new SpatialCellTable(8, 4, false);
            table.CountMember(0, 0, 0, 0);
            table.CountMember(0, 0, 0, 0);
            table.CountMember(1, 0, 0, 0);
            Assert.That(table.UsedBytes, Is.EqualTo(116L));
        }
    }
}
