using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class Mt060SimClockTests
    {
        [Test]
        public void LastRepresentableTickAdvancesBeforeExhaustion()
        {
            var clock = new SimClock();
            clock.Restore(long.MaxValue - 1);
            Assert.DoesNotThrow(() => clock.Advance());
            Assert.That(clock.Tick, Is.EqualTo(long.MaxValue));
            Assert.Throws<SimBoundaryException>(() => clock.Advance());
            Assert.That(clock.Tick, Is.EqualTo(long.MaxValue));
        }
    }
}
