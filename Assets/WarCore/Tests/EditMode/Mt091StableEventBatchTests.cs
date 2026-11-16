using System;
using NUnit.Framework;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    [TestFixture, Category("StableIdentity")]
    public sealed class Mt091StableEventBatchTests
    {
        [Test]
        public void EmptyBatchOutsideTickIsRejected()
        {
            var sim = new Simulation(1,identityMode:IdentityMode.StableWorld);
            Assert.Throws<InvalidOperationException>(() => sim.Context.EmitStableBatch(Span<StableProducedEvent>.Empty));
        }

        [Test]
        public void EmptyBatchInActiveLegacyWorldIsRejected()
        {
            var sim = Fixtures.Empty(1);
            ((SimEventBuffer)sim.Events).BeginTick(1);
            sim.Context.Begin(default);
            Assert.Throws<SimBoundaryException>(() => sim.Context.EmitStableBatch(Span<StableProducedEvent>.Empty));
            sim.Context.End();
            ((SimEventBuffer)sim.Events).EndTick();
        }

        [TestCase(true)]
        [TestCase(false)]
        public void UnknownLinkIsRejectedBeforePublication(bool source)
        {
            var sim = new Simulation(1,identityMode:IdentityMode.StableWorld);
            var link = new EntityLink(new StableId(123));
            var key = new StableRequestKey(1,0,new StableId(1),1,0,100);
            var item = new StableSimEvent(new EventTypeId(100),source:source ? link : default,
                target:source ? default : link);
            var batch = new[] { new StableProducedEvent(key,item) };
            ((SimEventBuffer)sim.Events).BeginTick(1);
            sim.Context.Begin(default);
            Assert.Throws<SimBoundaryException>(() => sim.Context.EmitStableBatch(batch));
            sim.Context.End();
            Assert.That(sim.Events.Read(1).Length,Is.Zero);
            Assert.That(sim.Events.NextSequence,Is.EqualTo(1));
            ((SimEventBuffer)sim.Events).EndTick();
        }

        [TestCase(true)]
        [TestCase(false)]
        public void RetiredLinkIsPublishedUnchanged(bool source)
        {
            var sim = new Simulation(1,identityMode:IdentityMode.StableWorld);
            EntityId entity = sim.Entities.Create(StableEntityKey.Placed(1,1));
            EntityLink link = sim.Entities.Link(entity);
            sim.Entities.Destroy(entity);
            var key = new StableRequestKey(1,0,link.Id,1,0,100);
            var item = new StableSimEvent(new EventTypeId(100),source:source ? link : default,
                target:source ? default : link);
            var batch = new[] { new StableProducedEvent(key,item) };
            ((SimEventBuffer)sim.Events).BeginTick(1);
            sim.Context.Begin(default);
            Assert.DoesNotThrow(() => sim.Context.EmitStableBatch(batch));
            sim.Context.End();
            Assert.That(sim.Events.Read(1).Length,Is.EqualTo(1));
            StableSimEvent published = sim.ReadStableEvent(1,0);
            Assert.That(published.Source,Is.EqualTo(item.Source));
            Assert.That(published.Target,Is.EqualTo(item.Target));
            Assert.That(published.Sequence,Is.EqualTo(1));
            ((SimEventBuffer)sim.Events).EndTick();
        }
    }
}
