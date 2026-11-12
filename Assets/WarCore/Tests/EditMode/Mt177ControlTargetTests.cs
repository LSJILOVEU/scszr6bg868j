using System;
using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class Mt177ControlTargetTests
    {
        [Test]
        public void ConstructorRejectsDefaultEventType()
        {
            var sim = Fixtures.Empty(1);
            EntityId initial = sim.Entities.Create();

            Assert.Throws<ArgumentException>(() => new ControlTargetSystem(sim.Entities, initial,
                new StableSystemTests.Handler(), default, 20));
        }

        [Test]
        public void SwitchRejectsForeignSimulationEvenForCurrentTarget()
        {
            var owner = Fixtures.Empty(1);
            var other = Fixtures.Empty(2);
            EntityId initial = owner.Entities.Create();
            var control = new ControlTargetSystem(owner.Entities, initial,
                new StableSystemTests.Handler(), Fixtures.Type("control-switched"), 20);
            other.Register("commands", new SwitchCommand(control, initial));

            Assert.Throws<ArgumentException>(() => other.Step(new InputFrame(default, default, skillSlot: 2)));
        }

        [Test]
        public void SwitchRejectsDeadCurrentTarget()
        {
            var sim = Fixtures.Empty(1);
            EntityId initial = sim.Entities.Create();
            var control = new ControlTargetSystem(sim.Entities, initial,
                new StableSystemTests.Handler(), Fixtures.Type("control-switched"), 20);
            sim.Entities.Destroy(initial);
            sim.Register("commands", new SwitchCommand(control, initial));

            Assert.Throws<ArgumentException>(() => sim.Step(new InputFrame(default, default, skillSlot: 2)));
        }

        [Test]
        public void StepRejectsForeignSimulation()
        {
            var owner = Fixtures.Empty(1);
            var other = Fixtures.Empty(2);
            EntityId initial = owner.Entities.Create();
            var control = new ControlTargetSystem(owner.Entities, initial,
                new StableSystemTests.Handler(), Fixtures.Type("control-switched"), 20);
            other.Register("control", control);

            Assert.Throws<InvalidOperationException>(() => other.Step(default));
        }
    }
}
