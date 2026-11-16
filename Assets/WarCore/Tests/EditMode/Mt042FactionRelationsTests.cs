using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class Mt042FactionRelationsTests
    {
        [Test]
        public void CompleteMatrixWithoutTrailingBytesLoads()
        {
            var writer = new SimStateWriter();
            writer.Int32(1);
            writer.Int32((int)FactionRelation.Hostile);
            var reader = new SimStateReader(writer.ToArray());
            FactionRelations restored = null;

            Assert.DoesNotThrow(() => restored = FactionRelations.LoadRelations(reader, null));
            Assert.That(restored.Count, Is.EqualTo(1));
            Assert.That(restored.Get(0, 0), Is.EqualTo(FactionRelation.Hostile));
            Assert.That(reader.Remaining, Is.Zero);
        }
    }
}
