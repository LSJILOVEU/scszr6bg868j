using System;
using NUnit.Framework;
using WarCore.Sim;
using Simulation=WarCore.Sim.Sim;
namespace WarCore.Tests
{
    // Public InputFrame -> admission result -> settled state. These 24 cases pin
    // the LC-2 review probes without observing private reservation counters.
    [TestFixture,Category("M0c2TaskB"),Category("TaskBLc2Probes")]
    public sealed class M0c2TaskBLc2ProbeTests
    {
        private static StableEntityKey First=>M0c2S3Fixture.Actor.Id.CompareTo(M0c2S3Fixture.OtherActor.Id)<0?M0c2S3Fixture.Actor:M0c2S3Fixture.OtherActor;
        private static StableEntityKey Second=>First.Id.Equals(M0c2S3Fixture.Actor.Id)?M0c2S3Fixture.OtherActor:M0c2S3Fixture.Actor;
        private static GameplayStatusDef Status(int maximum=1,GameplayStackScope scope=GameplayStackScope.Target,
            GameplayStackPolicy policy=GameplayStackPolicy.Independent,GameplayStatusDuration duration=GameplayStatusDuration.Timed,
            int ticks=1000,ulong key=1,bool absorb=false)
            =>new GameplayStatusDef(key,"lc2-status-"+key,duration,ticks,stackPolicy:policy,stackScope:scope,
                maxStacks:policy==GameplayStackPolicy.Stack?4:1,maxInstances:maximum,dispelTags:1,absorbResourceKey:absorb?(ulong?)1:null);
        private static GameplayAbilityAction Apply(uint ordinal=1,ulong status=1,GameplayActionTrigger trigger=GameplayActionTrigger.OnRelease,
            GameplayPrimitive kind=GameplayPrimitive.ApplyStatus)
            =>M0c2S3Fixture.Action(ordinal,kind,status,kind==GameplayPrimitive.GrantAbsorb?10:-1,trigger);
        private static GameplayAbilityDef Ability(ulong key=1,GameplayTargetMode mode=GameplayTargetMode.Explicit,
            GameplayTargetTiming timing=GameplayTargetTiming.LockAtAdmission,int windup=0,int active=0,int recovery=0,int period=0,
            GameplayAbilityAction[] actions=null)
        {
            var target=new GameplayTargetSpec(mode,GameplayTargetShape.Sphere,GameplayTargetSelection.Nearest,timing,
                rangeRaw:12*M0c2S3Fixture.S,layerMask:M0c2S3Fixture.UnitLayer,radiusRaw:M0c2S3Fixture.S,useTargetPoint:true);
            return new GameplayAbilityDef(key,"lc2-ability-"+key,key,1,target,actions??new[]{Apply()},
                new[]{new GameplayCost(2,3)},windupTicks:windup,activeTicks:active,recoveryTicks:recovery,pulsePeriodTicks:period);
        }
        private static Simulation World(GameplayStatusDef status,params GameplayAbilityDef[] abilities)
            =>World(new[]{status},abilities);
        private static Simulation World(GameplayStatusDef[] statuses,params GameplayAbilityDef[] abilities)
        {
            var schema=new GameplaySchema(new[]{new GameplayResourceDef(1,"health",-100,10000,8000),new GameplayResourceDef(2,"energy",0,1000000,100000)},
                new[]{new GameplayDamageTypeDef(1,"direct",1,1)},new[]{new GameplayPipelineDef(1,new GameplayDamageSpec(0,GameplayDamageStages.None))},
                new[]{new GameplayArchetypeDef(1,new[]{1UL,2UL}),new GameplayArchetypeDef(2,new[]{1UL,2UL})},
                new GameplayCapacities(8,16,128,256,64,16,8,16,8),"task-b.lc2-probes",statuses,abilities,
                new GameplayInputAuthority(1,new[]{M0c2S3Fixture.Actor.Id,M0c2S3Fixture.OtherActor.Id}));
            return M0c2S3Fixture.CreateWith(schema);
        }
        private static GameplayInputCommand Command(StableEntityKey? owner=null,ulong ability=1,uint ordinal=1,
            GameplayTargetMode mode=GameplayTargetMode.Explicit,StableEntityKey? target=null)
            =>new GameplayInputCommand(new GameplayStamp((owner??First).Id),ordinal,GameplayInputKind.Activate,ability,mode,
                mode==GameplayTargetMode.Explicit?(GameplayStamp?)new GameplayStamp((target??M0c2S3Fixture.Target2).Id):null,
                targetPoint:SimVec3.Metres(6,0,0));
        private static void Result(Simulation sim,int index,GameplayAbilityFailure expected)
            =>Assert.That(sim.GameplayInputResultAt(index).Failure,Is.EqualTo(expected),"public admission result "+index);
        private static void Accepted(Simulation sim,int count=1)
        {Assert.That(sim.GameplayInputResultCount,Is.EqualTo(count));for(int i=0;i<count;i++)Result(sim,i,GameplayAbilityFailure.None);}

#if !UNITY_EDITOR && !UNITY_5_3_OR_NEWER
        // Direct byte counters are a strict managed gate; Unity's meter is unavailable.
        [TestCase(GameplayTargetMode.Self),TestCase(GameplayTargetMode.Explicit),TestCase(GameplayTargetMode.Query)]
        public void KnownTargetMaxInstancesPlusOneRejectsBeforeFeeAndStaysAllocationFree(GameplayTargetMode mode)
        {
            var sim=World(Status(3),Ability(mode:mode));var commands=new[]{Command(mode:mode)};
            for(int i=0;i<3;i++){M0c2S3Fixture.Input(sim,commands);Accepted(sim);Assert.That(sim.GameplayStatusCount,Is.EqualTo(i+1));}
            M0c2S3Fixture.Input(sim,commands);Result(sim,0,GameplayAbilityFailure.StatusCapacity);
            Assert.That(sim.GameplayResource(First.Id,2),Is.EqualTo(99991));Assert.That(sim.GameplayStatusCount,Is.EqualTo(3));
            for(int i=0;i<20;i++)sim.Step(new InputFrame(default,default,gameplayTick:sim.Clock.Tick+1,skillCommands:commands));
            long before=GC.GetAllocatedBytesForCurrentThread();
            for(int i=0;i<200;i++)sim.Step(new InputFrame(default,default,gameplayTick:sim.Clock.Tick+1,skillCommands:commands));
            long allocated=GC.GetAllocatedBytesForCurrentThread()-before;
            Assert.That(allocated,Is.Zero,"200 rejected admissions allocate 0 B for "+mode);
            before=GC.GetAllocatedBytesForCurrentThread();var control=new byte[32768];long measured=GC.GetAllocatedBytesForCurrentThread()-before;GC.KeepAlive(control);
            Assert.That(measured,Is.GreaterThanOrEqualTo(32768),"allocation meter positive control");
            TestContext.WriteLine("LC2 allocation "+mode+": ticks=200; bytes="+allocated+"; positive-control="+measured);
            Result(sim,0,GameplayAbilityFailure.StatusCapacity);Assert.That(sim.GameplayResource(First.Id,2),Is.EqualTo(99991));
        }
#endif
        [TestCase(GameplayPrimitive.ApplyStatus),TestCase(GameplayPrimitive.GrantAbsorb)]
        public void SameTickZeroWindupCountsPendingStatusCommands(GameplayPrimitive kind)
        {
            var sim=World(Status(absorb:kind==GameplayPrimitive.GrantAbsorb),Ability(actions:new[]{Apply(kind:kind)}));
            M0c2S3Fixture.Input(sim,Command(First),Command(Second));Result(sim,0,GameplayAbilityFailure.None);Result(sim,1,GameplayAbilityFailure.StatusCapacity);
            Assert.That(sim.GameplayStatusCount,Is.EqualTo(1));Assert.That(sim.GameplayResource(Second.Id,2),Is.EqualTo(100000));
        }
        [TestCase(GameplayTargetMode.Explicit),TestCase(GameplayTargetMode.Query)]
        public void SameTickWindupCountsOtherKnownCastBeforeRelease(GameplayTargetMode mode)
        {
            var sim=World(Status(),Ability(mode:mode,windup:2));M0c2S3Fixture.Input(sim,Command(First,mode:mode),Command(Second,mode:mode));
            Result(sim,0,GameplayAbilityFailure.None);Result(sim,1,GameplayAbilityFailure.StatusCapacity);
            Assert.That(sim.GameplayCastCount,Is.EqualTo(1));Assert.That(sim.GameplayStatusCount,Is.Zero);Assert.That(sim.GameplayResource(Second.Id,2),Is.EqualTo(100000));
        }
        [Test] public void TargetAndSourceDoesNotCombineDifferentOwnersWindupDemand()
        {
            var sim=World(Status(scope:GameplayStackScope.TargetAndSource),Ability(windup:2));M0c2S3Fixture.Input(sim,Command(First),Command(Second));
            Accepted(sim,2);Assert.That(sim.GameplayCastCount,Is.EqualTo(2));sim.Step(default);sim.Step(default);Assert.That(sim.GameplayStatusCount,Is.EqualTo(2));
        }
        [Test] public void TargetAndSourceDoesNotCombineDifferentOwnersExistingRows()
        {
            var sim=World(Status(scope:GameplayStackScope.TargetAndSource),Ability());M0c2S3Fixture.Input(sim,Command(First));Accepted(sim);
            M0c2S3Fixture.Input(sim,Command(Second));Accepted(sim);Assert.That(sim.GameplayStatusCount,Is.EqualTo(2));
            M0c2S3Fixture.Input(sim,Command(Second));Result(sim,0,GameplayAbilityFailure.StatusCapacity);Assert.That(sim.GameplayResource(Second.Id,2),Is.EqualTo(99997));
        }
        [Test] public void DifferentTargetsDoNotShareWindupDemand()
        {
            var sim=World(Status(),Ability(windup:2));M0c2S3Fixture.Input(sim,Command(First),Command(Second,target:M0c2S3Fixture.Target3));Accepted(sim,2);
            sim.Step(default);sim.Step(default);Assert.That(sim.GameplayStatusCount,Is.EqualTo(2));
        }
        [Test] public void DifferentDefinitionsDoNotShareWindupDemand()
        {
            var sim=World(new[]{Status(),Status(key:2)},Ability(windup:2),Ability(2,windup:2,actions:new[]{Apply(status:2)}));
            M0c2S3Fixture.Input(sim,Command(First),Command(Second,2));Accepted(sim,2);sim.Step(default);sim.Step(default);Assert.That(sim.GameplayStatusCount,Is.EqualTo(2));
        }
        [TestCase(GameplayStackPolicy.Refresh),TestCase(GameplayStackPolicy.Stack),TestCase(GameplayStackPolicy.Strongest)]
        public void NonIndependentPoliciesDoNotUseMaxInstancesAsApplicationLimit(GameplayStackPolicy policy)
        {
            var sim=World(Status(policy:policy),Ability(windup:1));M0c2S3Fixture.Input(sim,Command(First),Command(Second));Accepted(sim,2);
            sim.Step(default);Assert.That(sim.GameplayStatusCount,Is.EqualTo(1));Assert.That(sim.GameplayAbilityFailureCount(GameplayAbilityFailure.StatusCapacity),Is.Zero);
        }
        [TestCase(GameplayStatusDuration.Instant),TestCase(GameplayStatusDuration.Timed)]
        public void NonPersistentStatusesDoNotConsumeIndependentInstances(GameplayStatusDuration duration)
        {
            var sim=World(Status(duration:duration,ticks:0),Ability(actions:new[]{Apply(1),Apply(2)}));
            M0c2S3Fixture.Input(sim,Command(First),Command(Second));Accepted(sim,2);Assert.That(sim.GameplayStatusCount,Is.Zero);
        }
        [Test] public void WindupCountsReleaseEveryPulseAndEndBeforeAdmissionFee()
        {
            // Release 1 + pulses at 3,5 (2) + end 1 = 4, larger than maximum 3.
            var sim=World(Status(3),Ability(windup:1,active:5,period:2,actions:new[]{Apply(1),Apply(2,trigger:GameplayActionTrigger.ActivePulse),Apply(3,trigger:GameplayActionTrigger.OnEnd)}));
            M0c2S3Fixture.Input(sim,Command());Result(sim,0,GameplayAbilityFailure.StatusCapacity);
            Assert.That(sim.GameplayResource(First.Id,2),Is.EqualTo(100000));Assert.That(sim.GameplayCastCount,Is.Zero);
        }
        [Test] public void ActiveCastDoesNotCountItsAlreadyReleasedActionAgain()
        {
            var sim=World(Status(2),Ability(active:10),Ability(2));M0c2S3Fixture.Input(sim,Command(First));Accepted(sim);
            Assert.That(sim.GameplayCastPhase(First.Id),Is.EqualTo(GameplayCastPhase.Active));M0c2S3Fixture.Input(sim,Command(Second,2));Accepted(sim);
            Assert.That(sim.GameplayStatusCount,Is.EqualTo(2));
        }
        [Test] public void ActiveCastCountsOnlyPulsesThatHaveNotAlreadyFired()
        {
            // First cast: pulses 2,3,4,5 and end 6. At tick 4: 2 rows + 3 future + incoming 1 = exactly 6.
            var sim=World(Status(6),Ability(active:5,period:1,actions:new[]{Apply(1,trigger:GameplayActionTrigger.ActivePulse),Apply(2,trigger:GameplayActionTrigger.OnEnd)}),Ability(2));
            M0c2S3Fixture.Input(sim,Command(First));sim.Step(default);sim.Step(default);Assert.That(sim.GameplayStatusCount,Is.EqualTo(2));
            Assert.That(sim.GameplayCastAt(0).NextPulseOrdinal,Is.EqualTo(3));M0c2S3Fixture.Input(sim,Command(Second,2));Accepted(sim);
            sim.Step(default);sim.Step(default);Assert.That(sim.GameplayStatusCount,Is.EqualTo(6));
        }
        [Test] public void RecoveryCastHasNoRemainingEndOrPulseDemand()
        {
            var sim=World(Status(2),Ability(recovery:10,actions:new[]{Apply(trigger:GameplayActionTrigger.OnEnd)}),Ability(2));
            M0c2S3Fixture.Input(sim,Command(First));Assert.That(sim.GameplayCastPhase(First.Id),Is.EqualTo(GameplayCastPhase.Recovery));
            Assert.That(sim.GameplayStatusCount,Is.EqualTo(1));M0c2S3Fixture.Input(sim,Command(Second,2));Accepted(sim);Assert.That(sim.GameplayStatusCount,Is.EqualTo(2));
        }
        [Test] public void RowExpiringAtAdmissionTickDoesNotConsumeCapacity()
        {
            var sim=World(Status(ticks:1),Ability());M0c2S3Fixture.Input(sim,Command());Accepted(sim);Assert.That(sim.GameplayStatusAt(0).ExpiryTick,Is.EqualTo(2));
            M0c2S3Fixture.Input(sim,Command());Accepted(sim);Assert.That(sim.GameplayStatusCount,Is.EqualTo(1));Assert.That(sim.GameplayStatusAt(0).StartTick,Is.EqualTo(2));
        }
        [Test] public void PendingDispelDoesNotLendCapacityBeforeSettlement()
        {
            var sim=World(Status(),Ability(),Ability(2,actions:new[]{M0c2S3Fixture.Action(1,GameplayPrimitive.Dispel,1,maximum:8)}));
            M0c2S3Fixture.Input(sim,Command(Second));Accepted(sim);
            M0c2S3Fixture.Input(sim,Command(First,2),Command(Second));Result(sim,0,GameplayAbilityFailure.None);Result(sim,1,GameplayAbilityFailure.StatusCapacity);
            Assert.That(sim.GameplayStatusCount,Is.Zero);Assert.That(sim.GameplayResource(Second.Id,2),Is.EqualTo(99997));
        }
        [Test] public void ExistingRowsPendingCommandsAndOtherCastsAccumulateInOneGroup()
        {
            // At second-owner admission: 1 settled + 1 queued + 1 first-owner windup + 1 incoming = 4 > 3.
            var sim=World(Status(3),Ability(),Ability(2,windup:5));M0c2S3Fixture.Input(sim,Command(Second));Accepted(sim);
            M0c2S3Fixture.Input(sim,Command(First),Command(First,2,2),Command(Second));
            Result(sim,0,GameplayAbilityFailure.None);Result(sim,1,GameplayAbilityFailure.None);Result(sim,2,GameplayAbilityFailure.StatusCapacity);
            Assert.That(sim.GameplayStatusCount,Is.EqualTo(2));Assert.That(sim.GameplayCastCount,Is.EqualTo(1));Assert.That(sim.GameplayResource(Second.Id,2),Is.EqualTo(99997));
        }
        [Test] public void RequeryUnknownGroupUsesWholeReleasePreflightAfterAdmissionFee()
        {
            var sim=World(Status(),Ability(mode:GameplayTargetMode.Query,timing:GameplayTargetTiming.RequeryAtRelease,
                actions:new[]{M0c2S3Fixture.Action(1,GameplayPrimitive.Damage,1,17),Apply(2)}));
            M0c2S3Fixture.Input(sim,Command(First,mode:GameplayTargetMode.Query),Command(Second,mode:GameplayTargetMode.Query));Accepted(sim,2);
            Assert.That(sim.GameplayAbilityFailureCount(GameplayAbilityFailure.StatusCapacity),Is.EqualTo(1));
            Assert.That(sim.GameplayResource(M0c2S3Fixture.Target2.Id,1),Is.EqualTo(7983),"failed second release must not leak its earlier damage action");
            Assert.That(sim.GameplayResource(First.Id,2),Is.EqualTo(99997));Assert.That(sim.GameplayResource(Second.Id,2),Is.EqualTo(99997));
            Assert.That(sim.GameplayStatusCount,Is.EqualTo(1));Assert.That(sim.GameplayCastCount,Is.Zero);
        }
    }
}
