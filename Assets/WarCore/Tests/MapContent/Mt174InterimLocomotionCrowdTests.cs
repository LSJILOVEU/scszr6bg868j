using NUnit.Framework;
using WarCore.Presentation;

namespace WarCore.Tests
{
    public sealed class Mt174InterimLocomotionCrowdTests
    {
        [Test]
        public void IntermediateWaypoint_AdvancesUntilFinalTargetStopsOrder()
        {
            var world = new M4TestWorld(M3TestWorld.Boxes(8, 4),
                new[] { new UnitSpawn(1, 0, 1, 1500, 1500, 30000) });
            world.Order(0, 3500, 1500);

            world.Sim.Step(default);
            Assert.That(world.Point(0), Is.EqualTo(M3TestWorld.Point(2500, 1500)));
            Assert.That(world.Intent(0).Active, Is.True);
            Assert.That(world.Sim.Entities.Get(world.Waypoints, world.Unit(0)).Point,
                Is.EqualTo(M3TestWorld.Point(3500, 1500)));

            world.Sim.Step(default);
            Assert.That(world.Point(0), Is.EqualTo(M3TestWorld.Point(3500, 1500)));
            Assert.That(world.Intent(0).Active, Is.False);
            Assert.That(world.Sim.Entities.Get(world.Waypoints, world.Unit(0)).Has, Is.False);
        }
    }
}
