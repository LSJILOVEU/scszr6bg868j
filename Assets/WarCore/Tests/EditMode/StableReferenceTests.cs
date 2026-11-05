using NUnit.Framework;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    [TestFixture, Category("StableIdentity")]
    public sealed class StableReferenceTests
    {
        [Test]
        public void SeatsInputAttachmentsAndDeadEventSourcesSurviveSlotRemapping()
        {
            Simulation sim = StableSnapshotTests.Scene(false);
            var seats = sim.Entities.Register<Seat>("seat",new SeatSerializer());
            var occupants = sim.Entities.Register<Occupant>("occupant",new OccupantSerializer());
            EntityId carrier = sim.Entities.Resolve(new EntityLink(StableEntityKey.Placed(1,0).Id));
            EntityId passenger = sim.Entities.Resolve(new EntityLink(StableEntityKey.Placed(1,1).Id));
            EntityLink carrierLink = sim.Entities.Link(carrier);
            EntityLink passengerLink = sim.Entities.Link(passenger);
            sim.Entities.Set(seats,carrier,new Seat(carrierLink,0,passengerLink));
            sim.Entities.Set(occupants,passenger,new Occupant(carrierLink));
            var key = new StableRequestKey(1,0,carrierLink.Id,1,0,1);
            sim.Attach(passengerLink,carrierLink,SimVec3.Metres(0,2,0),key);
            sim.StepStable(new StableInputFrame(new InputFrame(default,default,squad:new SquadCommand(SquadCommandKind.Follow,1,default,default)),passengerLink));
            ulong hash = sim.StateHashStable();
            byte[] snapshot = sim.SaveStableSnapshot();
            sim.RestoreStableSnapshot(snapshot,true);
            Assert.That(sim.StateHashStable(),Is.EqualTo(hash));
            EntityId restoredCarrier = sim.Entities.Resolve(carrierLink);
            Seat seat = sim.Entities.Read(seats,restoredCarrier);
            Assert.That(seat.CarrierLink,Is.EqualTo(carrierLink));
            Assert.That(seat.OccupantLink,Is.EqualTo(passengerLink));
            Assert.That(sim.Context.Input.Squad.Value.TargetEntity,Is.EqualTo((EntityId)sim.Entities.Resolve(passengerLink)));
        }

        [Test]
        public void PositionAndStaticRequestsUseStableLastWriterRegardlessOfArrival()
        {
            Simulation a = StableSnapshotTests.Scene(false);
            Simulation b = StableSnapshotTests.Scene(true);
            var link = new EntityLink(StableEntityKey.Placed(1,0).Id);
            var low = new StableRequestKey(1,1,new StableId(1),1,0,1);
            var high = new StableRequestKey(2,1,new StableId(1),1,0,1);
            a.RequestPosition(link,SimVec3.Metres(12,0,0),high);
            a.RequestPosition(link,SimVec3.Metres(6,0,0),low);
            b.RequestPosition(link,SimVec3.Metres(6,0,0),low);
            b.RequestPosition(link,SimVec3.Metres(12,0,0),high);
            a.RequestStableColliderStatic(link,true,high);
            a.RequestStableColliderStatic(link,false,low);
            b.RequestStableColliderStatic(link,false,low);
            b.RequestStableColliderStatic(link,true,high);
            Assert.That(a.StateHashStable(),Is.EqualTo(b.StateHashStable()));
            a.Step(default);
            b.Step(default);
            Assert.That(a.StateHashStable(),Is.EqualTo(b.StateHashStable()));
            Assert.That(a.Entities.Read(a.Entities.Resolve<Position>(),a.Entities.Resolve(link)).Value.X.Raw,
                Is.EqualTo(12*SimFix.Scale));
            Assert.That(a.Entities.Read(a.Entities.Resolve<Collider>(),a.Entities.Resolve(link)).IsStatic,Is.True);
        }
    }
}
