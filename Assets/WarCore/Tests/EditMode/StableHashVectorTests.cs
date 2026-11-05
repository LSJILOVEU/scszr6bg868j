using NUnit.Framework;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    [TestFixture, Category("StableIdentity")]
    public sealed class StableHashVectorTests
    {
        [Test]
        public void StableV3MatchesIndependentCanonicalByteOracle()
        {
            var sim = new Simulation(123,identityMode:IdentityMode.StableWorld);
            sim.Entities.Create(StableEntityKey.Placed(0,0));
            Assert.That(sim.StateHashStable(),Is.EqualTo(0xD55AF3A9B2827FD4UL));
            var scheduler = new SimScheduler(4,IdentityMode.StableWorld);
            scheduler.ScheduleStable(new TaskKey(5,1,new StableId(9),0,7),new StableSimEvent(new EventTypeId(100),tag:3));
            Assert.That(scheduler.StateHashStable(),Is.EqualTo(0x1E79EB5A9C9A126AUL));
            Assert.That(StableEntityKey.Spawned(new StableId(1UL<<63),ulong.MaxValue,7).Id.Value,Is.EqualTo(0xF2E5F858A0D8F621UL));
            Assert.That(StableEntityKey.World(9,300,4).Id.Value,Is.EqualTo(0x382A95B626962FBBUL));
            TestContext.WriteLine("Stable independent vectors: state-v3=0x{0:X16}; scheduler-v3=0x{1:X16}; H1=4 cases.",sim.StateHashStable(),scheduler.StateHashStable());
        }
    }
}
