using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    [TestFixture, Category("M0c2S1")]
    public sealed class Mt122GameplayResourcesTests
    {
        [Test]
        public void MaximumCostCountIsAcceptedAndDebited()
        {
            var sim=M0c2S1Fixture.Create();
            var target=sim.GameplayTarget(M0c2S1Fixture.Target.Id);
            var costs=new GameplayCost[65535];
            for(int i=0;i<costs.Length;i++)costs[i]=new GameplayCost(2,1);
            GameplayFailure failure=default;
            Assert.DoesNotThrow(()=>failure=sim.Entities.Gameplay.Spend(target,costs,0));
            Assert.That(failure,Is.EqualTo(GameplayFailure.None));
            Assert.That(sim.GameplayResource(target.Id,2),Is.EqualTo(589825));
        }

        [Test]
        public void CombinedCostsCanSpendExactlyToLowerBound()
        {
            var sim=M0c2S1Fixture.Create();
            var target=sim.GameplayTarget(M0c2S1Fixture.Target.Id);
            var costs=new[]{new GameplayCost(2,7*M0c2S1Fixture.S),new GameplayCost(2,13*M0c2S1Fixture.S)};
            Assert.That(sim.Entities.Gameplay.Spend(target,costs,0),Is.EqualTo(GameplayFailure.None));
            Assert.That(sim.GameplayResource(target.Id,2),Is.EqualTo(-10*M0c2S1Fixture.S));
        }
    }
}
