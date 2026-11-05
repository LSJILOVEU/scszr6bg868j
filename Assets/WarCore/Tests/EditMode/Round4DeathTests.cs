using NUnit.Framework;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    [TestFixture, Category("Round4Death")]
    public sealed class Round4DeathTests
    {
        internal static Simulation Scene(int diagnosticCapacity = 4096)
        {
            var sim = new Simulation(1,config:new SimConfig(entityCapacity:16,eventHardLimit:4096,requestDiagnosticCapacity:diagnosticCapacity),
                identityMode:IdentityMode.StableWorld,identityCapacity:64);
            var positions = sim.Entities.Register<Position>("position",new PositionSerializer());
            var colliders = sim.Entities.Register<Collider>("collider",new ColliderSerializer());
            for (ulong i = 0; i < 4; i++)
            {
                EntityId entity = sim.Entities.Create(StableEntityKey.Placed(7,i));
                sim.Entities.Set(positions,entity,new Position(SimVec3.Metres((int)i,0,0)));
                sim.Entities.Set(colliders,entity,new Collider(SimFix.Ratio(1,4),SimFix.FromInt(2)));
            }
            sim.ConfigureSpatial(positions,colliders,SimFix.FromInt(4));
            return sim;
        }
        internal static EntityLink Link(ulong index) => new EntityLink(StableEntityKey.Placed(7,index).Id);
        internal static StableRequestKey Key(ulong ordinal = 0) => new StableRequestKey(1,1,new StableId(1),1,ordinal,9);

        [Test]
        public void DeadSpawnerStillCreatesItsChildWithCapturedChunk()
        {
            var sim = Scene();
            EntityLink source = Link(0);
            sim.Spawns.Enqueue(source,Key());
            sim.Entities.MoveToChunk(sim.Entities.Resolve(source),99);
            sim.Entities.Destroy(sim.Entities.Resolve(source));
            Assert.DoesNotThrow(() => sim.Step(default));
            StableId child = StableEntityKey.Spawned(source.Id,0,9).Id;
            Assert.That(sim.Entities.TryResolve(child,out _),Is.True);
            Assert.That(sim.Entities.ChunkOf(child),Is.EqualTo(7));
        }

        [TestCase(0), TestCase(1), TestCase(2), TestCase(3)]
        public void DeadMotionTargetOrHostDiscardsTheRequestWithoutFault(int kind)
        {
            var sim = Scene();
            if (kind == 0)
                sim.RequestPosition(Link(0),SimVec3.Metres(10,0,0),Key());
            else if (kind == 1 || kind == 2)
                sim.Attach(Link(0),Link(1),default,Key());
            else
                sim.RequestStableColliderStatic(Link(0),true,Key());
            sim.Entities.Destroy(sim.Entities.Resolve(Link(kind == 2 ? 1UL : 0UL)));
            Assert.DoesNotThrow(() => sim.Step(default));
            Assert.DoesNotThrow(() => sim.Step(default));
            Assert.That(sim.Events.Read(1).Length,Is.GreaterThan(0),"Death must be diagnosed.");
        }

        [Test]
        public void AFollowedHostDeathClearsTheStrongRelationship()
        {
            var sim = Scene();
            sim.Attach(Link(0),Link(1),SimVec3.Metres(0,2,0),Key());
            sim.Step(default);
            SimVec3 before = sim.Entities.Read(sim.Entities.Resolve<Position>(),sim.Entities.Resolve(Link(0))).Value;
            sim.Entities.Destroy(sim.Entities.Resolve(Link(1)));
            Assert.That(sim.Entities.TryGet(sim.Entities.Resolve<SpatialAttachment>(),sim.Entities.Resolve(Link(0)),out _),Is.False);
            Assert.DoesNotThrow(() => sim.Step(default));
            Assert.That(sim.Entities.Read(sim.Entities.Resolve<Position>(),sim.Entities.Resolve(Link(0))).Value,Is.EqualTo(before));
            Assert.DoesNotThrow(() => sim.StateHashStable());
            Assert.That(sim.Events.Read(2).Length,Is.GreaterThan(0));
        }

        [Test]
        public void FollowGuardDiscardsAStaleRelationshipWithoutFaulting()
        {
            var sim = Scene();
            EntityId passenger = sim.Entities.Resolve(Link(0));
            EntityId host = sim.Entities.Resolve(Link(1));
            sim.Entities.Destroy(host);
            // This injected stale component isolates the follow guard from the earlier destroy cleanup.
            sim.Entities.Set(sim.Entities.Resolve<SpatialAttachment>(),passenger,new SpatialAttachment(host,default,Link(1)));
            Assert.DoesNotThrow(() => sim.Step(default));
            Assert.That(sim.RequestDiagnostics.Count(StableRequestKind.FollowHost),Is.EqualTo(1));
            Assert.That(sim.Entities.TryGet(sim.Entities.Resolve<SpatialAttachment>(),passenger,out _),Is.False);
        }

        [TestCase(0), TestCase(1), TestCase(2)]
        public void EachSeatReferenceDeathHasItsOwnCountAndStableDiagnostic(int kind)
        {
            var sim = Scene();
            var seats = sim.Entities.Register<Seat>("seat",new SeatSerializer());
            var occupants = sim.Entities.Register<Occupant>("occupant",new OccupantSerializer());
            EntityId seat = sim.Entities.Resolve(Link(2));
            EntityId passenger = sim.Entities.Resolve(Link(1));
            sim.Entities.Set(seats,seat,new Seat(Link(0),0,Link(1)));
            sim.Entities.Set(occupants,passenger,new Occupant(Link(2)));
            sim.Entities.Destroy(sim.Entities.Resolve(Link((ulong)kind)));
            sim.Step(default);
            var expected = kind == 0 ? StableRequestKind.SeatCarrier : kind == 1 ? StableRequestKind.SeatOccupant : StableRequestKind.OccupantSeat;
            Assert.That(sim.RequestDiagnostics.Count(expected),Is.EqualTo(1));
            StableSimEvent item = sim.ReadStableEvent(1,0);
            Assert.That(item.Type.Value,Is.EqualTo(RequestDiagnostics.EventType));
            Assert.That(item.Tag,Is.EqualTo((int)expected));
            Assert.That(item.Source,Is.EqualTo(Link(kind == 2 ? 1UL : 2UL)));
            Assert.That(item.Target,Is.EqualTo(Link((ulong)kind)));
        }

        [Test]
        public void DestroyClearsSeatAndOccupantStrongLinksBeforeHashing()
        {
            var sim = Scene();
            var seats = sim.Entities.Register<Seat>("seat",new SeatSerializer());
            var occupants = sim.Entities.Register<Occupant>("occupant",new OccupantSerializer());
            EntityId carrier = sim.Entities.Resolve(Link(0));
            EntityId passenger = sim.Entities.Resolve(Link(1));
            EntityId seat = sim.Entities.Resolve(Link(2));
            sim.Entities.Set(seats,seat,new Seat(Link(0),0,Link(1)));
            sim.Entities.Set(occupants,passenger,new Occupant(Link(2)));
            sim.Entities.Destroy(passenger);
            Assert.That(sim.Entities.Read(seats,seat).OccupantLink.HasValue,Is.False);
            sim.Entities.Destroy(carrier);
            Assert.That(sim.Entities.TryGet(seats,seat,out _),Is.False);
            Assert.DoesNotThrow(() => sim.StateHashStable());
        }
    }
}
