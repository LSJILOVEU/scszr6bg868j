using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class Mt067SimTierVersionTests
    {
        [Test]
        public void ComponentSerializerVersionIsOne()
        {
            Assert.That(new SimTierSerializer().Version, Is.EqualTo(1));
        }

        [Test]
        public void ElapsedWrapperStateVersionIsTwo()
        {
            var scene = new ElapsedMovementRegressionTests.Scene(1, SimTier.Full);
            var movement = new MovementSystem(scene.Sim.Entities, scene.Positions,
                scene.Movements, new NavProviderRegistry(), 1);
            var tiered = new TieredSystem(movement, scene.Tiers, 1);
            Assert.That(tiered.StateVersion, Is.EqualTo(2));
        }
    }
}
