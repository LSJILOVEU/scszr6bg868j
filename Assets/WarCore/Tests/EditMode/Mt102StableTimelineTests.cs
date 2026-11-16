using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    [TestFixture, Category("StableIdentity")]
    public sealed class Mt102StableTimelineTests
    {
        [TestCase(0)]
        [TestCase(1)]
        public void DormantSnapshotRestoresElapsedTime(int activeTicks)
        {
            var sim = StableSnapshotTests.Scene(false);
            var tiers = sim.Entities.Resolve<SimTier>();
            foreach (EntityId entity in sim.Entities)
                sim.Entities.Set(tiers,entity,SimTier.Full);
            for (int i = 0; i < activeTicks; i++)
                sim.Step(default);
            var link = new EntityLink(StableEntityKey.Placed(1,1).Id);
            sim.Entities.Set(tiers,sim.Entities.Resolve(link),SimTier.Dormant);
            for (int i = 0; i < 3; i++)
                sim.Step(default);
            byte[] snapshot = sim.SaveStableSnapshot();

            Assert.DoesNotThrow(() => sim.RestoreStableSnapshot(snapshot,true));
            Assert.That(sim.SaveStableSnapshot(),Is.EqualTo(snapshot));
            EntityId restored = sim.Entities.Resolve(link);
            sim.Entities.Set(tiers,restored,SimTier.Full);
            sim.Step(default);
            long delta = SimFix.FromInt(3).Raw/30;
            Assert.That(sim.Entities.Read(sim.Entities.Resolve<Position>(),restored).Value.X.Raw,
                Is.EqualTo(SimFix.Scale+(activeTicks+4)*delta));
        }

        [Test]
        public void DuplicateTimelineIdentityIsRejected()
        {
            var store = new EntityStore(2,IdentityMode.StableWorld);
            EntityId entity = store.Create(StableEntityKey.Placed(1,1));
            var writer = new SimStateWriter(store,materialize:true);
            writer.Int32(2);
            for (int i = 0; i < 2; i++)
            {
                writer.Link(store.Link(entity));
                writer.Int64(1);
                writer.Int32((int)SimTierLevel.Full);
            }
            var timeline = new TieredTimeline(store);
            var reader = new SimStateReader(writer.ToArray(),store);
            Assert.Throws<SimSnapshotException>(() => timeline.Load(reader));
        }
    }
}
