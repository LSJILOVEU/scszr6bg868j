using System;
using NUnit.Framework;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    public sealed class FactionRelationTests
    {
        private static FactionRelation[] Data() => new[]
        {
            FactionRelation.Friendly, FactionRelation.Hostile, FactionRelation.Neutral, FactionRelation.Friendly,
            FactionRelation.Hostile, FactionRelation.Friendly, FactionRelation.Hostile, FactionRelation.Neutral,
            FactionRelation.Neutral, FactionRelation.Friendly, FactionRelation.Friendly, FactionRelation.Neutral,
            FactionRelation.Hostile, FactionRelation.Neutral, FactionRelation.Neutral, FactionRelation.Friendly
        };

        [Test]
        public void G13_FourFactionDataSupportsSymmetricAndAsymmetricRelationsWithoutAliasing()
        {
            var windows = new AllocationWindow();
            do
            {
                FactionRelation[] data = Data();
                var table = new FactionRelations(4, data);
                for (int observer = 0; observer < 4; observer++)
                    for (int target = 0; target < 4; target++)
                    {
                        FactionRelation relation = data[observer * 4 + target];
                        Assert.That(table.Get(observer, target), Is.EqualTo(relation));
                        Assert.That(table.IsFriendly(observer, target), Is.EqualTo(relation == FactionRelation.Friendly));
                        Assert.That(table.IsHostile(observer, target), Is.EqualTo(relation == FactionRelation.Hostile));
                        Assert.That(table.IsNeutral(observer, target), Is.EqualTo(relation == FactionRelation.Neutral));
                    }

                Assert.That(table.Get(0, 1), Is.EqualTo(table.Get(1, 0)));
                Assert.That(table.Get(1, 2), Is.Not.EqualTo(table.Get(2, 1)));
                data[0] = FactionRelation.Hostile;
                Assert.That(table.Get(0, 0), Is.EqualTo(FactionRelation.Friendly));
                var meter = new AllocationMeter();
                windows.Begin(meter);
                int sum = 0;
                for (int i = 0; i < 10000; i++)
                    sum += (int)table.Get(i % 4, (i / 4) % 4);
                AllocationSample allocation = windows.End();
                if (!windows.NeedsRetry)
                    AllocationAssertions.Zero(allocation);
                Assert.That(sum, Is.GreaterThan(0));
            }
            while (windows.NeedsRetry);
        }

        [Test]
        public void G13_FactionDataIsHashedAndRestoredIntoAnotherSimulation()
        {
            var source = new Simulation(1, config: new SimConfig(1, 1, 1), factions: new FactionRelations(4, Data()));
            var target = new Simulation(1, config: new SimConfig(1, 1, 1));
            Assert.That(source.StateHash(), Is.Not.EqualTo(target.StateHash()));
            byte[] snapshot = source.SaveSnapshot();
            target.RestoreSnapshot(snapshot);
            Assert.That(target.Context.Factions, Is.SameAs(target.Factions));
            Assert.That(target.StateHash(), Is.EqualTo(source.StateHash()));
            for (int observer = 0; observer < 4; observer++)
                for (int other = 0; other < 4; other++)
                    Assert.That(target.Factions.Get(observer, other), Is.EqualTo(source.Factions.Get(observer, other)));
            FactionRelation[] changed = Data();
            changed[7] = FactionRelation.Hostile;
            var different = new Simulation(1, config: new SimConfig(1, 1, 1), factions: new FactionRelations(4, changed));
            Assert.That(source.StateHash(), Is.Not.EqualTo(different.StateHash()));
            for (int tick = 1; tick <= 5; tick++)
            {
                source.Step(default);
                target.Step(default);
                Assert.That(target.StateHash(), Is.EqualTo(source.StateHash()));
            }
        }

        [Test]
        public void InvalidDimensionsValuesAndIndicesFailBeforeArithmeticOrIndexing()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new FactionRelations(0, new FactionRelation[0]));
            Assert.Throws<ArgumentOutOfRangeException>(() => new FactionRelations(-1, null));
            Assert.Throws<SimBoundaryException>(() => new FactionRelations(int.MaxValue, null));
            Assert.Throws<ArgumentException>(() => new FactionRelations(4, new FactionRelation[15]));
            Assert.Throws<ArgumentOutOfRangeException>(() => new FactionRelations(1, new[] { (FactionRelation)3 }));
            var table = new FactionRelations(1, new[] { FactionRelation.Neutral });
            Assert.That(table.Get(0, 0), Is.EqualTo(FactionRelation.Neutral));
            Assert.Throws<ArgumentOutOfRangeException>(() => table.Get(-1, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => table.Get(0, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => table.Get(int.MaxValue, int.MaxValue));
        }
    }
}
