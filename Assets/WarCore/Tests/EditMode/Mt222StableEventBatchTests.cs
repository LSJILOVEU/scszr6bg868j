using System;
using NUnit.Framework;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    [TestFixture, Category("StableIdentity")]
    public sealed class Mt222StableEventBatchTests
    {
        [Test]
        public void BatchPublishesInAscendingKeyOrder()
        {
            var sim = new Simulation(1,identityMode:IdentityMode.StableWorld);
            var low = new StableRequestKey(1,0,new StableId(1),1,0,100);
            var high = new StableRequestKey(1,0,new StableId(1),1,1,100);
            var values = new[]
            {
                new StableProducedEvent(high,new StableSimEvent(new EventTypeId(100),tag:20)),
                new StableProducedEvent(low,new StableSimEvent(new EventTypeId(100),tag:10))
            };
            ((SimEventBuffer)sim.Events).BeginTick(1);
            sim.Context.Begin(default);
            sim.Context.EmitStableBatch(values);
            sim.Context.End();
            ((SimEventBuffer)sim.Events).EndTick();
            Assert.That(sim.Events.Read(1).Length,Is.EqualTo(2));
            Assert.That(sim.ReadStableEvent(1,0).Tag,Is.EqualTo(10));
            Assert.That(sim.ReadStableEvent(1,0).Sequence,Is.EqualTo(1));
            Assert.That(sim.ReadStableEvent(1,1).Tag,Is.EqualTo(20));
            Assert.That(sim.ReadStableEvent(1,1).Sequence,Is.EqualTo(2));
        }

        [Test]
        public void ProducedEventPreservesSourceAndTarget()
        {
            var source = new EntityLink(new StableId(1));
            var target = new EntityLink(new StableId(2));
            var key = new StableRequestKey(1,0,source.Id,1,0,100);
            var item = new StableSimEvent(new EventTypeId(100),source,target,tag:7);
            var produced = new StableProducedEvent(key,item);
            Assert.That(produced.Key,Is.EqualTo(key));
            Assert.That(produced.Event.Payload,Is.EqualTo(item.Payload));
            Assert.That(produced.Event.Source,Is.EqualTo(source));
            Assert.That(produced.Event.Target,Is.EqualTo(target));
        }

        [Test]
        public void EmptyBatchRequiresActiveContext()
        {
            var sim = new Simulation(1,identityMode:IdentityMode.StableWorld);
            Assert.Throws<InvalidOperationException>(() => sim.Context.EmitStableBatch(Array.Empty<StableProducedEvent>()));
        }

        [Test]
        public void EmptyBatchRequiresStableWorld()
        {
            var sim = new Simulation(1,identityMode:IdentityMode.Legacy);
            sim.Context.Begin(default);
            Assert.Throws<SimBoundaryException>(() => sim.Context.EmitStableBatch(Array.Empty<StableProducedEvent>()));
            sim.Context.End();
        }

        [TestCase(true)]
        [TestCase(false)]
        public void FirstEventRejectsUnknownLinkBeforePublishing(bool source)
        {
            var sim = new Simulation(1,identityMode:IdentityMode.StableWorld);
            var unknown = new EntityLink(new StableId(123));
            var key = new StableRequestKey(1,0,new StableId(1),1,0,100);
            var values = new[] { new StableProducedEvent(key,new StableSimEvent(new EventTypeId(100),
                source ? unknown : default,source ? default : unknown)) };
            ((SimEventBuffer)sim.Events).BeginTick(1);
            sim.Context.Begin(default);
            Assert.Throws<SimBoundaryException>(() => sim.Context.EmitStableBatch(values));
            sim.Context.End();
            Assert.That(sim.Events.Read(1).Length,Is.Zero);
            Assert.That(sim.Events.NextSequence,Is.EqualTo(1));
            ((SimEventBuffer)sim.Events).EndTick();
        }

        [TestCase(true)]
        [TestCase(false)]
        public void BatchPreservesRetiredWeakLink(bool source)
        {
            var sim = new Simulation(1,identityMode:IdentityMode.StableWorld);
            EntityId entity = sim.Entities.Create(StableEntityKey.Placed(1,1));
            EntityLink retired = sim.Entities.Link(entity);
            sim.Entities.Destroy(entity);
            var key = new StableRequestKey(1,0,retired.Id,1,0,100);
            var item = new StableSimEvent(new EventTypeId(100),
                source ? retired : default,source ? default : retired,tag:7);
            var values = new[] { new StableProducedEvent(key,item) };
            ((SimEventBuffer)sim.Events).BeginTick(1);
            sim.Context.Begin(default);
            Assert.DoesNotThrow(() => sim.Context.EmitStableBatch(values));
            sim.Context.End();
            ((SimEventBuffer)sim.Events).EndTick();
            Assert.That(sim.Events.Read(1).Length,Is.EqualTo(1));
            StableSimEvent published = sim.ReadStableEvent(1,0);
            Assert.That(published.Source,Is.EqualTo(item.Source));
            Assert.That(published.Target,Is.EqualTo(item.Target));
            Assert.That(published.Tag,Is.EqualTo(7));
            Assert.That(published.Sequence,Is.EqualTo(1));
        }
    }
}
