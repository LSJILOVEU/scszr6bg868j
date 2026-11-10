using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    [TestFixture, Category("StableIdentity")]
    public sealed class Mt098StableSchedulerStateTests
    {
        [Test]
        public void DueTaskMatchesCanonicalStableHash()
        {
            var scheduler = new SimScheduler(4, IdentityMode.StableWorld);
            scheduler.ScheduleStable(new TaskKey(1, 1, new StableId(9), 0, 7),
                new StableSimEvent(new EventTypeId(100), tag:3));
            scheduler.Advance(1);

            Assert.That(scheduler.PendingCount, Is.EqualTo(0));
            Assert.That(scheduler.DueCount, Is.EqualTo(1));
            Assert.That(scheduler.StateHashStable(), Is.EqualTo(0x406DB163F4322661UL));
        }
    }
}
