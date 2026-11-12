using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    [TestFixture, Category("StableIdentity")]
    public sealed class Mt221StableEntityStoreTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void ResolveRejectsEmptyOrUnknownLink(bool hasValue)
        {
            var store = new EntityStore(1,IdentityMode.StableWorld);
            EntityLink link = hasValue ? new EntityLink(StableEntityKey.Placed(1,1).Id) : default;
            Assert.Throws<SimBoundaryException>(() => store.Resolve(link));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ReactivateRejectsUnknownOrActiveIdentity(bool registered)
        {
            var store = new EntityStore(1,IdentityMode.StableWorld);
            var key = StableEntityKey.Placed(1,1);
            if (registered)
                store.Create(key);
            Assert.Throws<SimBoundaryException>(() => store.Reactivate(key.Id));
        }

        [Test]
        public void EmptyRequiredCarrierFailsBeforeComponentWrite()
        {
            var store = new EntityStore(1,IdentityMode.StableWorld);
            var seats = store.Register<Seat>("seat",new SeatSerializer());
            EntityId entity = store.Create(StableEntityKey.Placed(1,1));
            Assert.Throws<SimBoundaryException>(() => store.Set(seats,entity,new Seat(default(EntityLink),0)));
            Assert.That(store.TryGet(seats,entity,out _),Is.False);
        }
    }
}
