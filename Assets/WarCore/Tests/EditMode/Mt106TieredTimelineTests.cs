using System;
using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class Mt106TieredTimelineTests
    {
        [Test]
        public void DormantEntityRejectsTickZero()
        {
            var scene = new ElapsedMovementRegressionTests.Scene(1, SimTier.Dormant);
            var timeline = new TieredTimeline(scene.Sim.Entities);
            var movement = new MovementSystem(scene.Sim.Entities, scene.Positions, scene.Movements,
                new NavProviderRegistry(), 2);

            Assert.Throws<InvalidOperationException>(() => timeline.Advance(scene.Sim.Context,
                new EntityId(0, 1), SimTierLevel.Dormant, 1, movement));
        }

        [Test]
        public void CapacityGrowthBeforeWarmupLeavesTimelineHashEmpty()
        {
            var scene = new ElapsedMovementRegressionTests.Scene(1, SimTier.Dormant);
            var timeline = new TieredTimeline(scene.Sim.Entities);
            EntityId added = scene.Sim.Entities.Create();
            Assert.That(added.Id, Is.EqualTo(1));
            var hash = new StateHash();

            Assert.DoesNotThrow(() => timeline.WriteState(hash));
            Assert.That(hash.Value, Is.EqualTo(14695981039346656037UL));
        }

        [Test]
        public void DuplicateDeferredEntityIdsAreRejected()
        {
            var scene = new ElapsedMovementRegressionTests.Scene(2, SimTier.Dormant);
            var timeline = new TieredTimeline(scene.Sim.Entities);
            var writer = new SimStateWriter();
            writer.Int32(2);
            for (int i = 0; i < 2; i++)
            {
                SnapshotValues.Entity(writer, new EntityId(0, 1));
                writer.Int64(0);
                writer.Int32((int)SimTierLevel.Dormant);
            }

            Assert.Throws<SimSnapshotException>(() => timeline.Load(new SimStateReader(writer.ToArray())));
        }
    }
}
