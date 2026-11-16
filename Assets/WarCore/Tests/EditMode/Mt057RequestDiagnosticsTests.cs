using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    [TestFixture, Category("RequestDiagnostics")]
    public sealed class Mt057RequestDiagnosticsTests
    {
        [Test]
        public void RestoredFollowHostCleanupIsCancelledWhenOwnerRetires()
        {
            var sim = Round4ProtocolTests.World();
            var attachments = sim.Entities.Register<SpatialAttachment>("attachment",new SpatialAttachmentSerializer());
            EntityId owner = sim.Entities.Create(StableEntityKey.Placed(7,0));
            EntityId host = sim.Entities.Create(StableEntityKey.Placed(7,1));
            EntityLink ownerLink = sim.Entities.Link(owner);
            sim.Entities.Set(attachments,owner,new SpatialAttachment(host,default,sim.Entities.Link(host)));
            sim.Entities.Destroy(host);
            Assert.That(sim.RequestDiagnostics.Count(StableRequestKind.FollowHost),Is.EqualTo(1));

            sim.RestoreRetainedSnapshot(sim.SaveRetainedSnapshot(),reverseAllocation:true);
            Assert.That(sim.RequestDiagnostics.Count(StableRequestKind.FollowHost),Is.EqualTo(1));
            sim.Entities.Destroy(sim.Entities.Resolve(ownerLink));
            Assert.That(sim.RequestDiagnostics.Count(StableRequestKind.FollowHost),Is.Zero);
            Assert.That(sim.RequestDiagnostics.Total,Is.Zero);
            sim.Step(default);
            Assert.That(sim.Events.Read(1).Length,Is.Zero);
        }

        [TestCase(false), TestCase(true)]
        public void RepeatedCleanupKeepsTheSmallestRelatedIdentity(bool reverse)
        {
            var sim = Round4DeathTests.Scene();
            var seats = sim.Entities.Register<Seat>("seat",new SeatSerializer());
            EntityLink owner = Round4DeathTests.Link(0);
            EntityLink carrier = Round4DeathTests.Link(3);
            EntityLink first = Round4DeathTests.Link(1);
            EntityLink second = Round4DeathTests.Link(2);
            EntityLink smaller = first.Id.CompareTo(second.Id) < 0 ? first : second;
            EntityLink larger = first.Id.CompareTo(second.Id) < 0 ? second : first;
            foreach (EntityLink related in reverse ? new[] { larger,smaller } : new[] { smaller,larger })
            {
                sim.Entities.Set(seats,sim.Entities.Resolve(owner),new Seat(carrier,0,related));
                sim.Entities.Destroy(sim.Entities.Resolve(related));
            }
            sim.Step(default);

            Assert.That(sim.Events.Read(1).Length,Is.EqualTo(1));
            StableSimEvent item = sim.ReadStableEvent(1,0);
            Assert.That(item.Type.Value,Is.EqualTo(RequestDiagnostics.EventType));
            Assert.That(item.Target,Is.EqualTo(smaller));
            Assert.That(item.Source,Is.EqualTo(owner));
            Assert.That(item.Tag,Is.EqualTo((int)StableRequestKind.SeatOccupant));
            Assert.That(item.Payload.Magnitude.Raw,Is.EqualTo(2));
        }

        [Test]
        public void SingleSlotKeepsOneRequestAndSuppressesOnlyTheOverflow()
        {
            var sim = Round4DeathTests.Scene(1);
            EntityLink subject = Round4DeathTests.Link(0);
            sim.RequestPosition(subject,default,Round4DeathTests.Key(0));
            sim.RequestPosition(subject,default,Round4DeathTests.Key(1));
            sim.Entities.Destroy(sim.Entities.Resolve(subject));
            sim.Step(default);

            Assert.That(sim.RequestDiagnostics.Total,Is.EqualTo(2));
            Assert.That(sim.RequestDiagnostics.Count(StableRequestKind.Position),Is.EqualTo(2));
            Assert.That(sim.RequestDiagnostics.SuppressedTotal,Is.EqualTo(1));
            Assert.That(sim.Events.Read(1).Length,Is.EqualTo(2));
            StableSimEvent item = sim.ReadStableEvent(1,0);
            Assert.That(item.Type.Value,Is.EqualTo(RequestDiagnostics.EventType));
            Assert.That(item.Source,Is.EqualTo(subject));
            Assert.That(item.Tag,Is.EqualTo((int)StableRequestKind.Position));
            Assert.That(item.Payload.Magnitude.Raw,Is.EqualTo(1));
            StableSimEvent suppressed = sim.ReadStableEvent(1,1);
            Assert.That(suppressed.Type.Value,Is.EqualTo(RequestDiagnostics.EventType));
            Assert.That(suppressed.Tag,Is.Zero);
            Assert.That(suppressed.Payload.Magnitude.Raw,Is.EqualTo(1));
        }

        [Test]
        public void LoadRejectsTotalAboveTheSumOfKindCounts()
        {
            var sim = Round4DeathTests.Scene();
            var writer = new SimStateWriter(sim.Entities,materialize:true);
            writer.Int64(1); // Total exceeds the ten zero kind counts.
            writer.Int64(0);
            writer.Int64(0);
            for (int i = 1; i <= 10; i++)
                writer.Int64(0);
            writer.Int32(0);
            var reader = new SimStateReader(writer.ToArray(),sim.Entities);

            var error = Assert.Throws<SimSnapshotException>(() => sim.RequestDiagnostics.Load(reader));
            Assert.That(error.Message,Is.EqualTo("Incomplete diagnostic kind counts."));
        }

        [Test]
        public void PendingOrdinaryRequestRestoresAndPublishesOnce()
        {
            var sim = Round4ProtocolTests.World();
            EntityId target = sim.Entities.Create(StableEntityKey.Placed(7,0));
            EntityLink subject = sim.Entities.Link(target);
            sim.Entities.Destroy(target);
            sim.RequestDiagnostics.Record(StableRequestKind.Position,subject);
            byte[] snapshot = sim.SaveRetainedSnapshot();

            Assert.DoesNotThrow(() => sim.RestoreRetainedSnapshot(snapshot,reverseAllocation:true));
            Assert.That(sim.RequestDiagnostics.Total,Is.EqualTo(1));
            Assert.That(sim.RequestDiagnostics.Count(StableRequestKind.Position),Is.EqualTo(1));
            Assert.That(sim.RequestDiagnostics.SuppressedTotal,Is.Zero);
            sim.Step(default);
            Assert.That(sim.Events.Read(1).Length,Is.EqualTo(1));
            StableSimEvent item = sim.ReadStableEvent(1,0);
            Assert.That(item.Type.Value,Is.EqualTo(RequestDiagnostics.EventType));
            Assert.That(item.Source,Is.EqualTo(subject));
            Assert.That(item.Tag,Is.EqualTo((int)StableRequestKind.Position));
            Assert.That(item.Payload.Magnitude.Raw,Is.EqualTo(1));
            sim.Step(default);
            Assert.That(sim.Events.Read(2).Length,Is.Zero);
        }
    }
}
