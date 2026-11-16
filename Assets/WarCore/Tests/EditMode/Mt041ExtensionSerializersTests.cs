using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    [TestFixture, Category("StableIdentity")]
    public sealed class Mt041ExtensionSerializersTests
    {
        [Test]
        public void SeatRejectsRetiredCarrierBeforeComponentWrite()
        {
            var sim = Round4DeathTests.Scene();
            var seats = sim.Entities.Register<Seat>("seat",new SeatSerializer());
            EntityLink carrierLink = Round4DeathTests.Link(0);
            EntityId seat = sim.Entities.Resolve(Round4DeathTests.Link(2));
            sim.Entities.Destroy(sim.Entities.Resolve(carrierLink));

            Assert.That(sim.Entities.LifeOf(carrierLink.Id),Is.EqualTo(IdentityLife.Retired));
            Assert.Throws<SimBoundaryException>(() => sim.Entities.Set(seats,seat,new Seat(carrierLink,0)));
            Assert.That(sim.Entities.TryGet(seats,seat,out _),Is.False);
        }

        [Test]
        public void OccupantRejectsEmptySeatLinkBeforeComponentWrite()
        {
            var sim = Round4DeathTests.Scene();
            var occupants = sim.Entities.Register<Occupant>("occupant",new OccupantSerializer());
            EntityId passenger = sim.Entities.Resolve(Round4DeathTests.Link(1));

            Assert.Throws<SimBoundaryException>(() => sim.Entities.Set(occupants,passenger,new Occupant(default(EntityLink))));
            Assert.That(sim.Entities.TryGet(occupants,passenger,out _),Is.False);
        }
    }
}
