using System;
using NUnit.Framework;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    [TestFixture]
    public sealed class Mt034ControlTargetTests : IControlInputHandler
    {
        public string StateTypeId => "test.unversioned-control";
        public int StateVersion => 0;
        public void Apply(SimContext context, EntityId target, in InputFrame input) { }
        public void WriteState(StateHash hash) { }
        public void SaveState(SimStateWriter writer) { }
        public void LoadState(SimStateReader reader) { }

        [Test]
        public void StateVersionIsOne()
        {
            var f = new ExtensionFixture();
            Assert.That(f.Control.StateVersion, Is.EqualTo(1));
        }

        [Test]
        public void ConstructorRejectsDefaultEventType()
        {
            var f = new ExtensionFixture();
            Assert.Throws<ArgumentException>(() =>
                new ControlTargetSystem(f.P.Sim.Entities, f.P.Unit, f.Input, default, 20));
        }

        [Test]
        public void ConstructorRejectsZeroHandlerVersion()
        {
            var f = new ExtensionFixture();
            Assert.Throws<ArgumentException>(() =>
                new ControlTargetSystem(f.P.Sim.Entities, f.P.Unit, this, f.SwitchType, 20));
        }

        [Test]
        public void ReactivatedTargetDoesNotEmitSwitchEvent()
        {
            var sim = new Simulation(1, identityMode: IdentityMode.StableWorld);
            EntityId initial = sim.Entities.Create(StableEntityKey.Placed(1, 0));
            EntityLink link = sim.Entities.Link(initial);
            var control = new ControlTargetSystem(sim.Entities, initial,
                new StableSystemTests.Handler(), new EventTypeId(600), 2);
            sim.Entities.MakeCold(initial);
            EntityId active = sim.Entities.Reactivate(link.Id);

            ((SimEventBuffer)sim.Events).BeginTick(1);
            sim.Context.Begin(default);
            control.Switch(sim.Context, active);
            sim.Context.End();
            ((SimEventBuffer)sim.Events).EndTick();

            Assert.That(control.Current, Is.EqualTo(active));
            Assert.That(control.CurrentLink, Is.EqualTo(link));
            Assert.That(sim.Events.Read(1).Length, Is.Zero);
        }

        [Test]
        public void StableSwitchPreservesEventEndpoints()
        {
            var sim = new Simulation(1, identityMode: IdentityMode.StableWorld);
            EntityId initial = sim.Entities.Create(StableEntityKey.Placed(1, 0));
            EntityId target = sim.Entities.Create(StableEntityKey.Placed(1, 1));
            EntityLink previousLink = sim.Entities.Link(initial);
            EntityLink targetLink = sim.Entities.Link(target);
            var switchType = new EventTypeId(600);
            var control = new ControlTargetSystem(sim.Entities, initial,
                new StableSystemTests.Handler(), switchType, 2);

            ((SimEventBuffer)sim.Events).BeginTick(1);
            sim.Context.Begin(default);
            control.Switch(sim.Context, target);
            sim.Context.End();
            ((SimEventBuffer)sim.Events).EndTick();

            Assert.That(control.Current, Is.EqualTo(target));
            Assert.That(control.CurrentLink, Is.EqualTo(targetLink));
            Assert.That(sim.Events.Read(1).Length, Is.EqualTo(1));
            StableSimEvent change = sim.ReadStableEvent(1, 0);
            Assert.That(change.Type, Is.EqualTo(switchType));
            Assert.That(change.Source, Is.EqualTo(previousLink));
            Assert.That(change.Target, Is.EqualTo(targetLink));
        }
    }
}
