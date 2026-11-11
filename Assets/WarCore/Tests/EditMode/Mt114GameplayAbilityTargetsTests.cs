using NUnit.Framework;
using WarCore.Sim;
namespace WarCore.Tests
{
    [TestFixture,Category("M0c2S3"),Category("M0c2S3Targets")]
    public sealed class Mt114GameplayAbilityTargetsTests
    {
        private static GameplayAbilityDef Ability(GameplayTargetSpec target)
            =>new GameplayAbilityDef(1,"target",1,1,target,new[]{M0c2S3Fixture.Action(1,GameplayPrimitive.Damage,1,17)});
        [Test] public void NegativeXWorldLimitIsValidAimDirection()
        {
            var target=new GameplayTargetSpec(GameplayTargetMode.Query,rangeRaw:12*M0c2S3Fixture.S,layerMask:M0c2S3Fixture.UnitLayer);
            var sim=M0c2S3Fixture.CreateWith(M0c2S3Fixture.Schema(abilities:new[]{Ability(target)}));
            var command=new GameplayInputCommand(new GameplayStamp(M0c2S3Fixture.Actor.Id),1,GameplayInputKind.Activate,1,GameplayTargetMode.Query,
                aimDirection:SimVec3.Metres(-SimVec3.WorldLimitMetres,0,0));
            M0c2S3Fixture.Input(sim,command);
            Assert.That(sim.GameplayInputResultAt(0).Failure,Is.EqualTo(GameplayAbilityFailure.None));
            Assert.That(sim.GameplayResource(M0c2S3Fixture.Actor.Id,1),Is.EqualTo(7983));
        }
        [Test] public void ExplicitTargetAtExactRangeReceivesDamage()
        {
            var target=new GameplayTargetSpec(GameplayTargetMode.Explicit,rangeRaw:4*M0c2S3Fixture.S,layerMask:M0c2S3Fixture.UnitLayer);
            var sim=M0c2S3Fixture.CreateWith(M0c2S3Fixture.Schema(abilities:new[]{Ability(target)}));
            M0c2S3Fixture.Input(sim,M0c2S3Fixture.Command(1,mode:GameplayTargetMode.Explicit,target:new GameplayStamp(M0c2S3Fixture.Target.Id)));
            var result=sim.GameplayInputResultAt(0);Assert.That(result.Failure,Is.EqualTo(GameplayAbilityFailure.None));
            Assert.That(result.TotalHits,Is.EqualTo(1));Assert.That(result.Written,Is.EqualTo(1));
            Assert.That(sim.GameplayResource(M0c2S3Fixture.Target.Id,1),Is.EqualTo(7983));
        }
        [Test] public void RequiresAllAcceptsExactlyMaximumTargets()
        {
            var target=new GameplayTargetSpec(GameplayTargetMode.Query,GameplayTargetShape.Sphere,GameplayTargetSelection.AllCapped,
                rangeRaw:12*M0c2S3Fixture.S,layerMask:M0c2S3Fixture.UnitLayer,maximumTargets:3,requiresAll:true,
                radiusRaw:5*M0c2S3Fixture.S,useTargetPoint:true,excludeSelf:true,relation:GameplayRelation.OtherFaction);
            var sim=M0c2S3Fixture.CreateWith(M0c2S3Fixture.Schema(abilities:new[]{Ability(target)}));
            var command=new GameplayInputCommand(new GameplayStamp(M0c2S3Fixture.Actor.Id),1,GameplayInputKind.Activate,1,GameplayTargetMode.Query,
                aimDirection:SimVec3.Metres(1,0,0),targetPoint:SimVec3.Metres(6,0,0));
            M0c2S3Fixture.Input(sim,command);
            var result=sim.GameplayInputResultAt(0);Assert.That(result.Failure,Is.EqualTo(GameplayAbilityFailure.None));
            Assert.That(result.TotalHits,Is.EqualTo(3));Assert.That(result.Written,Is.EqualTo(3));
            Assert.That(sim.GameplayResource(M0c2S3Fixture.Target.Id,1),Is.EqualTo(7983));
            Assert.That(sim.GameplayResource(M0c2S3Fixture.Target2.Id,1),Is.EqualTo(7983));
            Assert.That(sim.GameplayResource(M0c2S3Fixture.Target3.Id,1),Is.EqualTo(7983));
        }
    }
}
