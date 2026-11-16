using System;
using NUnit.Framework;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests.Motion
{
    public sealed class Mt025InertiaMotionTests
    {
        [Test]
        public void NullClock_IsRejected()
        {
            SimSession s = InertiaWorlds.Build(InertiaWorlds.Open(InertiaWorlds.Line(25000, 12000), 9000, 0), InertiaWorlds.FlatMap());
            EntityStore entities = M3TestWorld.Sim(s).Entities;
            var error = Assert.Throws<ArgumentNullException>(() => new InertiaMotion(entities,
                entities.Resolve<Position>(), entities.Resolve<MoveIntent>(), entities.Resolve<MotionLimits>(),
                entities.Resolve<MotionState>(), null));
            Assert.That(error.ParamName, Is.EqualTo("clock"));
        }

        [Test]
        public void ZeroMaxSpeed_ReturnsTheWantedSpeed()
        {
            SimSession s = InertiaWorlds.Build(InertiaWorlds.Open(InertiaWorlds.Line(25000, 12000), 9000, 0), InertiaWorlds.FlatMap());
            WarCore.Sim.Sim sim = M3TestWorld.Sim(s);
            EntityStore entities = sim.Entities;
            EntityId unit = M3TestWorld.Unit(s);
            entities.Set(entities.Resolve<MotionLimits>(), unit, new MotionLimits(default, default, default, default));
            var model = new InertiaMotion(entities, entities.Resolve<Position>(), entities.Resolve<MoveIntent>(),
                entities.Resolve<MotionLimits>(), entities.Resolve<MotionState>(), sim.Clock);

            Assert.That(model.StepSpeed(unit, SimFix.FromRaw(1234)).Raw, Is.EqualTo(1234L));
            Assert.That(InertiaWorlds.State(s), Is.EqualTo(new MotionState(SimFix.FromRaw(1234), 0)));
        }
    }
}
