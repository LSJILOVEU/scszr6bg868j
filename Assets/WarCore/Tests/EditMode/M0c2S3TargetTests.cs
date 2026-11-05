using NUnit.Framework;
using WarCore.Sim;
namespace WarCore.Tests
{
    [TestFixture,Category("M0c2S3"),Category("M0c2S3Targets")]
    public sealed class M0c2S3TargetTests
    {
        private static GameplayAbilityDef Ability(GameplayTargetSpec target,int windup=0)
            =>new GameplayAbilityDef(1,"target",1,1,target,new[]{M0c2S3Fixture.Action(1,GameplayPrimitive.Damage,1,17)},new[]{new GameplayCost(2,3)},1,4,windup);
        private static GameplayTargetSpec Sphere(int k,bool requiresAll=false,uint mask=M0c2S3Fixture.UnitLayer)
            =>new GameplayTargetSpec(GameplayTargetMode.Query,GameplayTargetShape.Sphere,GameplayTargetSelection.AllCapped,
                rangeRaw:12*M0c2S3Fixture.S,layerMask:mask,maximumTargets:k,requiresAll:requiresAll,radiusRaw:5*M0c2S3Fixture.S,useTargetPoint:true,excludeSelf:true,relation:GameplayRelation.OtherFaction);
        private static GameplayInputCommand SphereCommand()=>new GameplayInputCommand(new GameplayStamp(M0c2S3Fixture.Actor.Id),1,GameplayInputKind.Activate,1,GameplayTargetMode.Query,
            aimDirection:SimVec3.Metres(1,0,0),targetPoint:SimVec3.Metres(6,0,0));
        [TestCase(0),TestCase(1),TestCase(2),TestCase(3),TestCase(4)] public void LimitedAreaReportsExactTotalHitsWhileApplyingOnlyNearestK(int k)
        {
            var sim=M0c2S3Fixture.CreateWith(M0c2S3Fixture.Schema(abilities:new[]{Ability(Sphere(k))}));M0c2S3Fixture.Input(sim,SphereCommand());
            var result=sim.GameplayInputResultAt(0);Assert.That(result.Failure,Is.EqualTo(GameplayAbilityFailure.None));
            Assert.That(result.TotalHits,Is.EqualTo(3));Assert.That(result.Written,Is.EqualTo(k<3?k:3));
            Assert.That(sim.GameplayResource(M0c2S3Fixture.Target2.Id,1),Is.EqualTo(k>=1?7983:8000),"center is first");
            bool targetFirst=M0c2S3Fixture.Target.Id.CompareTo(M0c2S3Fixture.Target3.Id)<0;
            Assert.That(sim.GameplayResource(M0c2S3Fixture.Target.Id,1),Is.EqualTo(k>=3 || k==2 && targetFirst?7983:8000),"equal-distance tie uses unsigned stable id");
            Assert.That(sim.GameplayResource(M0c2S3Fixture.Target3.Id,1),Is.EqualTo(k>=3 || k==2 && !targetFirst?7983:8000));
        }
        [Test] public void RequiresAllRejectsTheWholeCastWhenPreciseTotalHitsExceedsK()
        {
            var sim=M0c2S3Fixture.CreateWith(M0c2S3Fixture.Schema(abilities:new[]{Ability(Sphere(1,true))}));M0c2S3Fixture.Input(sim,SphereCommand());
            Assert.That(sim.GameplayInputResultAt(0).Failure,Is.EqualTo(GameplayAbilityFailure.TargetOverflow));
            Assert.That(sim.GameplayResource(M0c2S3Fixture.Actor.Id,2),Is.EqualTo(100000));Assert.That(sim.GameplayResource(M0c2S3Fixture.Target2.Id,1),Is.EqualTo(8000));
            Assert.That(sim.GameplayCooldownReady(M0c2S3Fixture.Actor.Id,1),Is.Zero);
        }
        [Test] public void ZeroLayerMaskHasNoHitsAndHighestLayerBitRemainsUsable()
        {
            var sim=M0c2S3Fixture.CreateWith(M0c2S3Fixture.Schema(abilities:new[]{Ability(Sphere(4,mask:0))}));M0c2S3Fixture.Input(sim,SphereCommand());
            Assert.That(sim.GameplayInputResultAt(0).Failure,Is.EqualTo(GameplayAbilityFailure.NoTarget));
            var high=M0c2S3Fixture.CreateWith(M0c2S3Fixture.Schema(abilities:new[]{Ability(Sphere(4))}));M0c2S3Fixture.Input(high,SphereCommand());
            Assert.That(high.GameplayInputResultAt(0).TotalHits,Is.EqualTo(3));
        }
        [Test] public void NearestSkipsInvalidRelationBeforeSelectingTheFirstValidTarget()
        {
            var ray=new GameplayTargetSpec(GameplayTargetMode.Query,rangeRaw:12*M0c2S3Fixture.S,layerMask:M0c2S3Fixture.UnitLayer,excludeSelf:true,relation:GameplayRelation.OtherFaction);
            var sim=M0c2S3Fixture.CreateWith(M0c2S3Fixture.Schema(abilities:new[]{Ability(ray)}));M0c2S3Fixture.Input(sim,M0c2S3Fixture.Command(1));
            Assert.That(sim.GameplayResource(M0c2S3Fixture.Actor.Id,1),Is.EqualTo(8000));Assert.That(sim.GameplayResource(M0c2S3Fixture.Target.Id,1),Is.EqualTo(7983));
            Assert.That(sim.GameplayResource(M0c2S3Fixture.Target2.Id,1),Is.EqualTo(8000));
        }
        [Test] public void PublicNearestPathSelectsTheGeometricallyClosestIdentity()
        {
            var ray=new GameplayTargetSpec(GameplayTargetMode.Query,rangeRaw:12*M0c2S3Fixture.S,layerMask:M0c2S3Fixture.UnitLayer);
            var sim=M0c2S3Fixture.CreateWith(M0c2S3Fixture.Schema(abilities:new[]{Ability(ray)}));M0c2S3Fixture.Input(sim,M0c2S3Fixture.Command(1));
            Assert.That(sim.GameplayResource(M0c2S3Fixture.Actor.Id,1),Is.EqualTo(7983));Assert.That(sim.GameplayResource(M0c2S3Fixture.Target.Id,1),Is.EqualTo(8000));
        }
        [Test] public void ExplicitTargetChecksLogicalGenerationAndAnyOcclusionWithoutUsingItAsIdentity()
        {
            var explicitTarget=new GameplayTargetSpec(GameplayTargetMode.Explicit,rangeRaw:12*M0c2S3Fixture.S,layerMask:M0c2S3Fixture.UnitLayer,occlusionMask:M0c2S3Fixture.UnitLayer);
            var sim=M0c2S3Fixture.CreateWith(M0c2S3Fixture.Schema(abilities:new[]{Ability(explicitTarget)}));
            M0c2S3Fixture.Input(sim,M0c2S3Fixture.Command(1,mode:GameplayTargetMode.Explicit,target:new GameplayStamp(M0c2S3Fixture.Target.Id,1)));
            Assert.That(sim.GameplayInputResultAt(0).Failure,Is.EqualTo(GameplayAbilityFailure.TargetLifeMismatch));
            M0c2S3Fixture.Input(sim,M0c2S3Fixture.Command(1,mode:GameplayTargetMode.Explicit,target:new GameplayStamp(M0c2S3Fixture.Target.Id)));
            Assert.That(sim.GameplayInputResultAt(0).Failure,Is.EqualTo(GameplayAbilityFailure.Occluded));Assert.That(sim.GameplayResource(M0c2S3Fixture.Target.Id,1),Is.EqualTo(8000));
        }
    }
}
