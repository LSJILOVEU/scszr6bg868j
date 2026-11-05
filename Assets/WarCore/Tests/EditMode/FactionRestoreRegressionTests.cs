using NUnit.Framework;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    [Category("S34")]
    public sealed class FactionRestoreRegressionTests
    {
        [TestCase(1)]
        [TestCase(2)]
        public void CachedReferenceSeesRestoredContentsAndDimensions(int size)
        {
            var savedValues = new FactionRelation[size * size];
            savedValues[0] = FactionRelation.Hostile;
            var source = new Simulation(1, factions: new FactionRelations(size, savedValues));
            var replay = new Simulation(1, factions: new FactionRelations(1, new[] { FactionRelation.Friendly }));
            FactionRelations cached = replay.Context.Factions;
            replay.RestoreSnapshot(source.SaveSnapshot());
            Assert.That(replay.Context.Factions, Is.SameAs(cached));
            Assert.That(cached.Count, Is.EqualTo(size));
            Assert.That(cached.Get(0, 0), Is.EqualTo(FactionRelation.Hostile));
            Assert.That(replay.StateHash(), Is.EqualTo(source.StateHash()));
        }

        [Test]
        public void RestoringAbsentRelationsClearsExistingReferenceWithoutAddingHashBytes()
        {
            var source = new Simulation(1);
            var replay = new Simulation(1, factions: new FactionRelations(1, new[] { FactionRelation.Hostile }));
            FactionRelations cached = replay.Factions;
            replay.RestoreSnapshot(source.SaveSnapshot());
            Assert.That(replay.Context.Factions, Is.SameAs(cached));
            Assert.That(cached.Count, Is.Zero);
            Assert.That(replay.StateHash(), Is.EqualTo(source.StateHash()));
            Assert.That(replay.SaveSnapshot(), Is.EqualTo(source.SaveSnapshot()));
        }
        [Test]
        public void RestoringOneSimDoesNotMutateAnotherSimThatUsedTheSameContentTable()
        {
            var content = new FactionRelations(1, new[] { FactionRelation.Friendly });
            var first = new Simulation(1, factions: content);
            var second = new Simulation(1, factions: content);
            var saved = new Simulation(1, factions: new FactionRelations(1, new[] { FactionRelation.Hostile }));
            ulong secondHash = second.StateHash();
            first.RestoreSnapshot(saved.SaveSnapshot());
            Assert.That(first.Factions.Get(0, 0), Is.EqualTo(FactionRelation.Hostile));
            Assert.That(second.StateHash(), Is.EqualTo(secondHash));
            Assert.That(content.Get(0, 0), Is.EqualTo(FactionRelation.Friendly));
        }
    }
}
