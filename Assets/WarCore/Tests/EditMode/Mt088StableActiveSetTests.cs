using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    [TestFixture, Category("StableIdentity")]
    public sealed class Mt088StableActiveSetTests
    {
        [Test]
        public void PublicationAdvancesOncePerPublishedChange()
        {
            var store = new EntityStore(1, IdentityMode.StableWorld);
            Assert.That(store.ActiveSetPublication, Is.EqualTo(0UL));
            store.SynchronizeStableEntities();
            Assert.That(store.ActiveSetPublication, Is.EqualTo(1UL));
            store.SynchronizeStableEntities();
            Assert.That(store.ActiveSetPublication, Is.EqualTo(1UL));
            store.Create(StableEntityKey.Placed(1, 1));
            store.SynchronizeStableEntities();
            Assert.That(store.ActiveSetPublication, Is.EqualTo(2UL));
        }
    }
}
