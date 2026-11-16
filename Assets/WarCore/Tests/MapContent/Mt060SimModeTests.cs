using NUnit.Framework;
using WarCore.Battle;

namespace WarCore.Tests
{
    public sealed class Mt060SimModeTests
    {
        [Test]
        public void RetainedWorldRunsMatchMode()
        {
            M6World world = null;
            Assert.DoesNotThrow(() => world = new M6World(
                M6World.RecipeOf(new[] { M6World.U(1, 1) })));
            world.Step();
            Assert.That(world.Sim.Clock.Tick, Is.EqualTo(1));
            Assert.That(world.Match.Winner, Is.EqualTo(1));
            Assert.That(world.Match.EndTick, Is.EqualTo(1));
            Assert.That(world.Match.EndReason, Is.EqualTo(MatchEndReason.Exhausted));
            Assert.That(world.Sim.IsFinished, Is.True);
        }
    }
}
