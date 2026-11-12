using NUnit.Framework;
using WarCore.Sim;
namespace WarCore.Tests
{
    [TestFixture,Category("M0c2S3"),Category("M0c2S3Targets")]
    public sealed class Mt182GameplayAbilityTargetsTests
    {
        private static GameplayAbilityDef Ability(GameplayTargetSpec target,int windup=0)
            =>new GameplayAbilityDef(1,"target",1,1,target,new[]{M0c2S3Fixture.Action(1,GameplayPrimitive.Damage,1,17)},new[]{new GameplayCost(2,3)},1,4,windup);

        [Test] public void ColocatedExplicitTargetIsNotOccluded()
        {
            var target=new GameplayTargetSpec(GameplayTargetMode.Explicit,rangeRaw:12*M0c2S3Fixture.S,
                layerMask:M0c2S3Fixture.UnitLayer,occlusionMask:M0c2S3Fixture.UnitLayer,excludeSelf:true);
            var sim=M0c2S3Fixture.CreateWith(M0c2S3Fixture.Schema(abilities:new[]{Ability(target)}));
            var entity=(EntityId)sim.Entities.Resolve(new EntityLink(M0c2S3Fixture.Target.Id));
            sim.Entities.Set(sim.Entities.Resolve<Position>(),entity,new Position(SimVec3.Metres(0,0,0)));

            M0c2S3Fixture.Input(sim,M0c2S3Fixture.Command(1,mode:GameplayTargetMode.Explicit));

            Assert.That(sim.GameplayInputResultAt(0).Failure,Is.EqualTo(GameplayAbilityFailure.None));
            Assert.That(sim.GameplayResource(M0c2S3Fixture.Target.Id,1),Is.EqualTo(7983));
        }

        [Test] public void ExplicitTargetOnRejectedLayerDoesNotSpendOrDealDamage()
        {
            var target=new GameplayTargetSpec(GameplayTargetMode.Explicit,rangeRaw:12*M0c2S3Fixture.S,layerMask:1);
            var sim=M0c2S3Fixture.CreateWith(M0c2S3Fixture.Schema(abilities:new[]{Ability(target)}));

            M0c2S3Fixture.Input(sim,M0c2S3Fixture.Command(1,mode:GameplayTargetMode.Explicit));

            Assert.That(sim.GameplayInputResultAt(0).Failure,Is.EqualTo(GameplayAbilityFailure.LayerRejected));
            Assert.That(sim.GameplayResource(M0c2S3Fixture.Actor.Id,2),Is.EqualTo(100000));
            Assert.That(sim.GameplayResource(M0c2S3Fixture.Target.Id,1),Is.EqualTo(8000));
        }

        [Test] public void ExplicitTargetLeavingRangeDuringWindupFailsReleaseWithoutDamage()
        {
            var target=new GameplayTargetSpec(GameplayTargetMode.Explicit,rangeRaw:12*M0c2S3Fixture.S,layerMask:M0c2S3Fixture.UnitLayer);
            var sim=M0c2S3Fixture.CreateWith(M0c2S3Fixture.Schema(abilities:new[]{Ability(target,windup:2)}));
            M0c2S3Fixture.Input(sim,M0c2S3Fixture.Command(1,mode:GameplayTargetMode.Explicit));
            Assert.That(sim.GameplayInputResultAt(0).Failure,Is.EqualTo(GameplayAbilityFailure.None));
            Assert.That(sim.GameplayCastCount,Is.EqualTo(1));

            sim.RequestPosition(new EntityLink(M0c2S3Fixture.Target.Id),SimVec3.Metres(20,0,0),
                new StableRequestKey(1,0,M0c2S3Fixture.Actor.Id,1,0,1));
            sim.Step(default);
            sim.Step(default);

            Assert.That(sim.GameplayAbilityFailureCount(GameplayAbilityFailure.OutOfRange),Is.EqualTo(1));
            Assert.That(sim.GameplayResource(M0c2S3Fixture.Target.Id,1),Is.EqualTo(8000));
            Assert.That(sim.GameplayCastCount,Is.Zero);
        }
    }
}
