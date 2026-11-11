using NUnit.Framework;
using WarCore.Battle;

namespace WarCore.Tests
{
    public sealed class Mt001ReactionFireStateTests
    {
        [TestCase(1)]
        [TestCase(10)]
        public void SingleShotBursts_UseVersionThree(int sense)
        {
            var world = P3.World(P2.Cast(), P3.Ai(0, sense, 1, 12));
            Assert.That(world.System<ReactionFire>().StateVersion, Is.EqualTo(3));
        }
    }
}
