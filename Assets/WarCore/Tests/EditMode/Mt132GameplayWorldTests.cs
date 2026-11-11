using NUnit.Framework;
using WarCore.Sim;
using Simulation=WarCore.Sim.Sim;

namespace WarCore.Tests
{
    [TestFixture,Category("Mt132GameplayWorld")]
    public sealed class Mt132GameplayWorldTests
    {
        [Test]
        public void WorldWithoutGameplayHasZeroStatuses()
        {
            var sim=Simulation.CreateRetainedWorld(41,new PlacedCatalog(new StableContentManifest(0)));
            Assert.That(sim.GameplayStatusCount,Is.EqualTo(0));
        }

        [Test]
        public void BindUnboundEntityAfterStepIsRejected()
        {
            var sim=M0c2S1Fixture.Create();
            var entity=sim.Entities.Create(StableEntityKey.World(99,0,1));
            sim.Step(default);
            Assert.That(Assert.Throws<SimBoundaryException>(()=>sim.BindGameplay(entity,1)).Message,
                Is.EqualTo("M0C_BIND_INITIALIZATION_ONLY"));
        }

        [Test]
        public void ChangeResourceAfterStepIsRejectedOutsideQueries()
        {
            var sim=M0c2S1Fixture.Create();
            sim.Step(default);
            var target=sim.GameplayTarget(M0c2S1Fixture.Target.Id);
            var key=new GameplayApplyKey(M0c2S1Fixture.Low,1,0,M0c2S1Fixture.Low,0,1);
            Assert.That(Assert.Throws<SimBoundaryException>(()=>sim.Context.ChangeResource(target,key,1,1)).Message,
                Is.EqualTo("M0C_GAMEPLAY_QUERY_PHASE"));
        }
    }
}
