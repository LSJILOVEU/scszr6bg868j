using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    [TestFixture, Category("StableIdentity")]
    public sealed class Mt090StableEntityStoreTests
    {
        [Test]
        public void FullEntityCapacityRejectsCreationAfterWarmup()
        {
            var store = new EntityStore(1,IdentityMode.StableWorld,identityCapacity:2);
            store.Create(StableEntityKey.Placed(1,1));
            store.CompleteWarmup();

            var error = Assert.Throws<SimBoundaryException>(() => store.Create(StableEntityKey.Placed(1,2)));
            Assert.That(error.Message,Is.EqualTo("Entity capacity exhausted; synchronize destroyed slots or increase entity capacity."));
        }

        [Test]
        public void RetiredIdentityMayArchiveAtRetentionDeadline()
        {
            var store = new EntityStore(1,IdentityMode.StableWorld,retiredRetentionTicks:2);
            var key = StableEntityKey.Placed(1,1);
            store.Destroy(store.Create(key));

            Assert.That(store.RetiredMayArchive(key.Id,1),Is.False);
            Assert.That(store.RetiredMayArchive(key.Id,2),Is.True);
        }
    }
}
