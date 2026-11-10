using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class Mt028InterimLocomotionTests
    {
        [Test]
        public void OneStepTarget_ClearsActiveOnArrivalTick()
        {
            var world = new M2MapFixture.World(M2MapFixture.GoldenBytes, M2MapFixture.Point(1, 2), speed: SimClock.TickRate);
            SimVec3 target = M2MapFixture.Point(2, 2);

            world.Tick(1, target);

            Assert.That(world.Position, Is.EqualTo(target));
            Assert.That(world.Active, Is.False);
        }
    }
}
