using NUnit.Framework;
using WarCore.Presentation;

namespace WarCore.Tests
{
    // MAP-BL0 W7: the AI recipe, seed 41, run twice side by side for 900 ticks: the same hash every tick, nothing thrown.
    // No outcome is locked here (this card records no behaviour baseline).
    public sealed class WardRunTests
    {
        [Test]
        public void TheSameWorldTwiceStaysTheSameFor900Ticks()
        {
            Assert.DoesNotThrow(() =>
            {
                M6World a = WardWorld.World(WardDemo.AiRecipe(WardWorld.Baked)), b = WardWorld.World(WardDemo.AiRecipe(WardWorld.Baked));
                Assert.That(a.Hash, Is.EqualTo(b.Hash), "tick 0");
                for (int t = 1; t <= 900; t++)
                {
                    a.Step();
                    b.Step();
                    Assert.That(a.Hash, Is.EqualTo(b.Hash), "tick " + t);
                }
            });
        }
    }
}
