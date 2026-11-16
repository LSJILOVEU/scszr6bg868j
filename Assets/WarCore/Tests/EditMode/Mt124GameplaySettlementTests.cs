using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    [TestFixture,Category("M0c2S1")]
    public sealed class Mt124GameplaySettlementTests
    {
        [Test]
        public void SubmitAcceptsFirstResourceRow()
        {
            var sim=M0c2S1Fixture.Create();
            var first=M0c2S1Fixture.Target.Id.CompareTo(M0c2S1Fixture.Low)<0
                ?M0c2S1Fixture.Target.Id:M0c2S1Fixture.Low;
            var owner=sim.GameplayTarget(first);
            var key=new GameplayApplyKey(first,1,0,first,1,1);
            var costs=new[]{new GameplayCost(2,M0c2S1Fixture.S)};
            var actions=new[]{GameplayAction.ChangeResource(owner,key,1,7*M0c2S1Fixture.S)};
            GameplayFailure result=GameplayFailure.CommandCapacity;
            Assert.DoesNotThrow(()=>result=sim.Entities.Gameplay.Submit(owner,costs,actions,0));
            Assert.That(result,Is.EqualTo(GameplayFailure.None));
            Assert.That(sim.GameplayResource(first,2),Is.EqualTo(9*M0c2S1Fixture.S));
        }

        [Test]
        public void SubmitAtCommandCapacityPaysAndSettlesWholeBatch()
        {
            var sim=M0c2S1Fixture.CreateWith(out S1Script script,commands:2);
            sim.Step(new InputFrame(default,default,primaryAttack:true,skillSlot:5));
            Assert.That(script.LastFailure,Is.EqualTo(GameplayFailure.None));
            Assert.That(sim.GameplayResource(M0c2S1Fixture.Target.Id,2),Is.EqualTo(9*M0c2S1Fixture.S));
            Assert.That(sim.GameplayResource(M0c2S1Fixture.Target.Id,1),Is.EqualTo(80*M0c2S1Fixture.S-2));
        }
    }
}
