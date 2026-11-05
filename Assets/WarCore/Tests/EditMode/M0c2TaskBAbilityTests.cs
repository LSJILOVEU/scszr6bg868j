using NUnit.Framework;
using WarCore.Sim;
using Simulation=WarCore.Sim.Sim;
namespace WarCore.Tests
{
    [TestFixture,Category("M0c2TaskBAbility")]
    public sealed class M0c2TaskBAbilityTests
    {
        private static GameplaySchema Schema(GameplayAbilityDef ability,GameplayStatusDef status=null)
            =>new GameplaySchema(new[]{new GameplayResourceDef(1,"health",-100,10000,8000,isLife:true),
                    new GameplayResourceDef(2,"energy",0,1000000,100000)},
                new[]{new GameplayDamageTypeDef(1,"direct",1,1)},new[]{new GameplayPipelineDef(1,new GameplayDamageSpec(0,GameplayDamageStages.None))},
                new[]{new GameplayArchetypeDef(1,new[]{1UL,2UL}),new GameplayArchetypeDef(2,new[]{1UL,2UL})},
                new GameplayCapacities(8,16,128,256,status==null?0:32,16,8,16,8),"task-b.ability",
                status==null?new GameplayStatusDef[0]:new[]{status},new[]{ability},
                new GameplayInputAuthority(1,new[]{M0c2S3Fixture.Actor.Id,M0c2S3Fixture.OtherActor.Id}),
                new[]{new GameplayLifeRuleDef(1,1,false,0),new GameplayLifeRuleDef(2,1,false,0)});
        private static GameplayTargetSpec Sphere(GameplayTargetTiming timing,GameplayTargetSelection selection,int maximum=2)
            =>new GameplayTargetSpec(GameplayTargetMode.Query,GameplayTargetShape.Sphere,selection,
                rangeRaw:12*M0c2S3Fixture.S,layerMask:M0c2S3Fixture.UnitLayer,maximumTargets:selection==GameplayTargetSelection.Nearest?1:maximum,
                radiusRaw:3*M0c2S3Fixture.S,useTargetPoint:true,timing:timing);
        private static GameplayInputCommand SphereCommand(StableEntityKey? owner=null)
            =>new GameplayInputCommand(new GameplayStamp((owner??M0c2S3Fixture.Actor).Id),1,GameplayInputKind.Activate,1,
                GameplayTargetMode.Query,targetPoint:SimVec3.Metres(6,0,0));
        [TestCase(GameplayTargetTiming.LockAtAdmission,GameplayTargetSelection.Nearest)]
        [TestCase(GameplayTargetTiming.LockAtAdmission,GameplayTargetSelection.AllCapped)]
        [TestCase(GameplayTargetTiming.RequeryAtRelease,GameplayTargetSelection.Nearest)]
        [TestCase(GameplayTargetTiming.RequeryAtRelease,GameplayTargetSelection.AllCapped)]
        [Category("TaskBDet1")]
        public void CorpseInsideShapeNeverDisplacesLivingAbilityTargets(GameplayTargetTiming timing,GameplayTargetSelection selection)
        {
            var ability=new GameplayAbilityDef(1,"corpse-filter",1,1,Sphere(timing,selection),
                new[]{M0c2S3Fixture.Action(1,GameplayPrimitive.Damage,1,17)});
            var sim=M0c2S3Fixture.CreateWith(Schema(ability));
            sim.RegisterGameplay("task-b.kill",new KillMiddleTarget(),SimSystemPhase.Queries);sim.Step(default);
            Assert.That(sim.GameplayLife(M0c2S3Fixture.Target2.Id).State,Is.EqualTo(GameplayLifeState.Dead));
            Assert.DoesNotThrow(()=>M0c2S3Fixture.Input(sim,SphereCommand()));
            var result=sim.GameplayInputResultAt(0);Assert.That(result.Failure,Is.EqualTo(GameplayAbilityFailure.None));
            Assert.That(result.TotalHits,Is.EqualTo(2));Assert.That(result.Written,Is.EqualTo(selection==GameplayTargetSelection.Nearest?1:2));
            Assert.That(sim.GameplayAbilityFailureCount(GameplayAbilityFailure.TargetGone),Is.Zero);
            long total=sim.GameplayResource(M0c2S3Fixture.Target.Id,1)+sim.GameplayResource(M0c2S3Fixture.Target3.Id,1);
            Assert.That(total,Is.EqualTo(selection==GameplayTargetSelection.Nearest?15983:15966));
            Assert.That(sim.GameplayResource(M0c2S3Fixture.Target2.Id,1),Is.EqualTo(-100));Assert.That(sim.GameplayCastCount,Is.Zero);
        }
        private static GameplayStatusDef Independent(GameplayStackScope scope=GameplayStackScope.Target)
            =>new GameplayStatusDef(1,"independent",GameplayStatusDuration.Timed,20,stackPolicy:GameplayStackPolicy.Independent,
                stackScope:scope,maxInstances:1);
        private static GameplayTargetSpec StatusTarget(GameplayTargetMode mode,GameplayTargetTiming timing)
            =>mode==GameplayTargetMode.Query?new GameplayTargetSpec(mode,GameplayTargetShape.Sphere,GameplayTargetSelection.Nearest,
                rangeRaw:12*M0c2S3Fixture.S,layerMask:M0c2S3Fixture.UnitLayer,radiusRaw:M0c2S3Fixture.S,useTargetPoint:true,timing:timing)
                :new GameplayTargetSpec(mode,rangeRaw:12*M0c2S3Fixture.S,layerMask:M0c2S3Fixture.UnitLayer,timing:timing);
        private static GameplayAbilityDef StatusAbility(GameplayTargetMode mode,GameplayTargetTiming timing,int windup=0,int cooldown=0)
            =>new GameplayAbilityDef(1,"instance-cap",1,1,StatusTarget(mode,timing),new[]{
                M0c2S3Fixture.Action(1,GameplayPrimitive.Damage,1,17),M0c2S3Fixture.Action(2,GameplayPrimitive.ApplyStatus,1,-1)},
                new[]{new GameplayCost(2,3)},1,cooldown,windup);
        private static GameplayInputCommand StatusCommand(GameplayTargetMode mode,StableEntityKey? owner=null)
            =>mode==GameplayTargetMode.Query?SphereCommand(owner):new GameplayInputCommand(new GameplayStamp((owner??M0c2S3Fixture.Actor).Id),
                1,GameplayInputKind.Activate,1,mode,mode==GameplayTargetMode.Explicit?(GameplayStamp?)new GameplayStamp(M0c2S3Fixture.Target2.Id):null);
        [TestCase(GameplayTargetMode.Self),TestCase(GameplayTargetMode.Explicit),TestCase(GameplayTargetMode.Query)]
        [Category("TaskBLc2Admission")]
        public void KnownTargetRejectsIndependentLimitBeforeSecondTickFee(GameplayTargetMode mode)
        {
            var sim=M0c2S3Fixture.CreateWith(Schema(StatusAbility(mode,GameplayTargetTiming.LockAtAdmission),Independent()));
            M0c2S3Fixture.Input(sim,StatusCommand(mode));Assert.That(sim.GameplayStatusCount,Is.EqualTo(1));
            Assert.DoesNotThrow(()=>M0c2S3Fixture.Input(sim,StatusCommand(mode)));
            Assert.That(sim.GameplayInputResultAt(0).Failure,Is.EqualTo(GameplayAbilityFailure.StatusCapacity));
            Assert.That(sim.GameplayResource(M0c2S3Fixture.Actor.Id,2),Is.EqualTo(99997));Assert.That(sim.GameplayStatusCount,Is.EqualTo(1));
            Assert.That(sim.GameplayResource(mode==GameplayTargetMode.Self?M0c2S3Fixture.Actor.Id:M0c2S3Fixture.Target2.Id,1),Is.EqualTo(7983));
            Assert.That(sim.GameplayCastCount,Is.Zero);
        }
        [Test,Category("TaskBLc2Admission")]
        public void TwoCastersReserveKnownIndependentGroupBeforeEitherRelease()
        {
            var sim=M0c2S3Fixture.CreateWith(Schema(StatusAbility(GameplayTargetMode.Explicit,GameplayTargetTiming.LockAtAdmission,windup:1,cooldown:10),Independent()));
            Assert.DoesNotThrow(()=>M0c2S3Fixture.Input(sim,StatusCommand(GameplayTargetMode.Explicit),StatusCommand(GameplayTargetMode.Explicit,M0c2S3Fixture.OtherActor)));
            Assert.That(sim.GameplayInputResultAt(0).Failure,Is.EqualTo(GameplayAbilityFailure.None));
            Assert.That(sim.GameplayInputResultAt(1).Failure,Is.EqualTo(GameplayAbilityFailure.StatusCapacity));
            Assert.That(sim.GameplayCastCount,Is.EqualTo(1));Assert.That(sim.GameplayResource(sim.GameplayInputResultAt(1).Actor.Id,2),Is.EqualTo(100000));
            Assert.That(sim.GameplayCooldownReady(sim.GameplayInputResultAt(1).Actor.Id,1),Is.Zero);
            Assert.DoesNotThrow(()=>sim.Step(default));Assert.That(sim.GameplayStatusCount,Is.EqualTo(1));
        }
        [Test,Category("TaskBLc2Release")]
        public void RequerySecondTickFailureKeepsFeeAndRollsBackWholeRelease()
        {
            var sim=M0c2S3Fixture.CreateWith(Schema(StatusAbility(GameplayTargetMode.Query,GameplayTargetTiming.RequeryAtRelease,cooldown:1),Independent()));
            M0c2S3Fixture.Input(sim,StatusCommand(GameplayTargetMode.Query));
            Assert.DoesNotThrow(()=>M0c2S3Fixture.Input(sim,StatusCommand(GameplayTargetMode.Query)));
            Assert.That(sim.GameplayInputResultAt(0).Failure,Is.EqualTo(GameplayAbilityFailure.None));
            Assert.That(sim.GameplayAbilityFailureCount(GameplayAbilityFailure.StatusCapacity),Is.EqualTo(1));
            Assert.That(sim.GameplayResource(M0c2S3Fixture.Actor.Id,2),Is.EqualTo(99994));Assert.That(sim.GameplayCooldownReady(M0c2S3Fixture.Actor.Id,1),Is.EqualTo(3));
            Assert.That(sim.GameplayResource(M0c2S3Fixture.Target2.Id,1),Is.EqualTo(7983));Assert.That(sim.GameplayStatusCount,Is.EqualTo(1));Assert.That(sim.GameplayCastCount,Is.Zero);
            AssertSingleCapacityFailureNotice(sim);
        }
        [Test,Category("TaskBLc2Release")]
        public void TwoCastersRequerySameTickRejectsOneWholeReleaseAndPreservesCooldown()
        {
            var sim=M0c2S3Fixture.CreateWith(Schema(StatusAbility(GameplayTargetMode.Query,GameplayTargetTiming.RequeryAtRelease,windup:1,cooldown:10),Independent()));
            M0c2S3Fixture.Input(sim,StatusCommand(GameplayTargetMode.Query),StatusCommand(GameplayTargetMode.Query,M0c2S3Fixture.OtherActor));
            Assert.That(sim.GameplayCastCount,Is.EqualTo(2));Assert.DoesNotThrow(()=>sim.Step(default));
            Assert.That(sim.GameplayAbilityFailureCount(GameplayAbilityFailure.StatusCapacity),Is.EqualTo(1));Assert.That(sim.GameplayStatusCount,Is.EqualTo(1));
            Assert.That(sim.GameplayResource(M0c2S3Fixture.Target2.Id,1),Is.EqualTo(7983));Assert.That(sim.GameplayCastCount,Is.Zero);
            Assert.That(sim.GameplayResource(M0c2S3Fixture.Actor.Id,2),Is.EqualTo(99997));Assert.That(sim.GameplayResource(M0c2S3Fixture.OtherActor.Id,2),Is.EqualTo(99997));
            Assert.That(sim.GameplayCooldownReady(M0c2S3Fixture.Actor.Id,1),Is.EqualTo(11));Assert.That(sim.GameplayCooldownReady(M0c2S3Fixture.OtherActor.Id,1),Is.EqualTo(11));
            AssertSingleCapacityFailureNotice(sim);
        }
        private static void AssertSingleCapacityFailureNotice(Simulation sim)
        {
            int count=0;
            foreach(var item in sim.Events.Read(sim.Clock.Tick))if(item.Type.Equals(GameplayEvents.AbilityFailure))
            {count++;Assert.That(item.Tag,Is.EqualTo((int)GameplayAbilityFailure.StatusCapacity));}
            Assert.That(count,Is.EqualTo(1));
        }
        private sealed class KillMiddleTarget : IRetainedGameplaySystem
        {
            public int Order=>10;public string StateTypeId=>"test.task-b.kill";public int StateVersion=>1;
            public void Step(SimContext context)
            {if(context.Clock.Tick==1)context.Damage(context.Owner.GameplayTarget(M0c2S3Fixture.Target2.Id),
                new GameplayApplyKey(M0c2S3Fixture.Actor.Id,1,0,M0c2S3Fixture.Actor.Id,1,1),1,8100);}
            public void SaveState(SimStateWriter writer){}public void LoadState(SimStateReader reader){}public void WriteState(StateHash hash){}
        }
    }
}
