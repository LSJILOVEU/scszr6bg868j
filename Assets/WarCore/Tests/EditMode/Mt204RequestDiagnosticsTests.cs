using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    [TestFixture, Category("Mt204RequestDiagnostics")]
    public sealed class Mt204RequestDiagnosticsTests
    {
        [Test]
        public void SuppressedRequestsAccumulateAndPublishAnAggregateEachTick()
        {
            var sim = Round4DeathTests.Scene(1);
            for (ulong batch = 0; batch < 2; batch++)
            {
                EntityLink target = Round4DeathTests.Link(batch);
                for (ulong request = 0; request < 3; request++)
                    sim.RequestPosition(target,default,Round4DeathTests.Key(request));
                sim.Entities.Destroy(sim.Entities.Resolve(target));
                sim.Step(default);

                long tick = (long)batch+1;
                Assert.That(sim.RequestDiagnostics.Total,Is.EqualTo(3*tick));
                Assert.That(sim.RequestDiagnostics.Count(StableRequestKind.Position),Is.EqualTo(3*tick));
                Assert.That(sim.RequestDiagnostics.SuppressedTotal,Is.EqualTo(2*tick));
                Assert.That(sim.Events.Read(tick).Length,Is.EqualTo(2));
                StableSimEvent summary = sim.ReadStableEvent(tick,1);
                Assert.That(summary.Type.Value,Is.EqualTo(RequestDiagnostics.EventType));
                Assert.That(summary.Tag,Is.Zero);
                Assert.That(summary.Payload.Magnitude.Raw,Is.EqualTo(2));
                Assert.That(summary.Source.HasValue,Is.False);
                Assert.That(summary.Target.HasValue,Is.False);
            }
        }

        [Test]
        public void SnapshotRejectsAnOrdinaryPendingDiagnosticAmountGreaterThanOne()
        {
            var sim = Round4ProtocolTests.World();
            EntityId entity = sim.Entities.Create(StableEntityKey.Placed(7,0));
            sim.RequestDiagnostics.Record(StableRequestKind.Position,sim.Entities.Link(entity));
            byte[] snapshot = sim.SaveRetainedSnapshot();
            Assert.DoesNotThrow(() => sim.RestoreRetainedSnapshot(snapshot));
            Assert.That(sim.RequestDiagnostics.Count(StableRequestKind.Position),Is.EqualTo(1));
            Assert.That(M0c2S2ProtocolTests.Word(snapshot,snapshot.Length-8),Is.EqualTo(1UL));

            M0c2S2ProtocolTests.Put(snapshot,snapshot.Length-8,2);
            var error = Assert.Throws<SimSnapshotException>(() => sim.RestoreRetainedSnapshot(snapshot));
            Assert.That(error.Message,Is.EqualTo("Request diagnostic capacity exceeded."));
        }
    }
}
