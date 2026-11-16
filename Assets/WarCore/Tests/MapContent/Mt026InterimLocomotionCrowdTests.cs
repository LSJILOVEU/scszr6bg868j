using NUnit.Framework;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class Mt026InterimLocomotionCrowdTests : ICrowdPass
    {
        private EntityId allowedMover, allowedIdle;

        ulong ICrowdPass.Digest => 1;
        bool ICrowdPass.MayPass(EntityId mover, EntityId idle) =>
            mover.Equals(allowedMover) && idle.Equals(allowedIdle);

        [Test]
        public void ZeroDotSidestepKeepsTheFirstSide()
        {
            var world = new M4TestWorld(M3TestWorld.Boxes(8, 8), new[] {
                new UnitSpawn(1, 0, 1, 4500, 4500, 7500),
                new UnitSpawn(2, 0, 2, 5000, 4500, 7500) });

            world.Order(0, 6500, 4500);
            world.Sim.Step(default);

            Assert.That(world.Point(0), Is.EqualTo(M3TestWorld.Point(4500, 4250)));
        }

        [Test]
        public void PassRuleReceivesMoverBeforeIdleUnit()
        {
            var world = new M4TestWorld(M3TestWorld.Boxes(8, 8), new[] {
                new UnitSpawn(1, 0, 1, 2500, 2500, 7500),
                new UnitSpawn(2, 0, 2, 3000, 2500, 7500) });
            allowedMover = world.Unit(0);
            allowedIdle = world.Unit(1);
            typeof(InterimLocomotion).GetField("pass", M7DifferentialTests.Private).SetValue(world.Locomotion, this);

            world.Order(0, 2750, 2500);
            world.Sim.Step(default);

            Assert.That(world.Point(0), Is.EqualTo(M3TestWorld.Point(2750, 2500)));
            Assert.That(world.Point(1), Is.EqualTo(M3TestWorld.Point(3000, 2500)));
        }

        [Test]
        public void EqualDistanceOverlapRequiresASidestep()
        {
            var world = new M4TestWorld(M3TestWorld.Boxes(8, 8), new[] {
                new UnitSpawn(1, 0, 1, 4250, 4500, 15000),
                new UnitSpawn(2, 0, 2, 4500, 4500, 15000) });

            world.Order(0, 4750, 4500);
            world.Sim.Step(default);

            Assert.That(world.Point(0), Is.EqualTo(M3TestWorld.Point(4250, 4000)));
        }
    }
}
