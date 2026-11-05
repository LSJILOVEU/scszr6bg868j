using NUnit.Framework;
using WarCore.Sim;
using WarCore.Tests.Oracle;
namespace WarCore.Tests
{
    [TestFixture,Category("M0c2S3"),Category("M0c2S3Behavior")]
    public sealed class M0c2S3BehaviorTests
    {
        private static GameplayAbilityDef Timeline(int windup=2,int active=5,int recovery=2,int period=2,int cooldown=10,GameplayCost[] costs=null)
            =>new GameplayAbilityDef(1,"timeline",1,1,new GameplayTargetSpec(GameplayTargetMode.Self),new[]{
                M0c2S3Fixture.Action(1,GameplayPrimitive.ChangeResource,1,11),
                M0c2S3Fixture.Action(2,GameplayPrimitive.ChangeResource,1,3,GameplayActionTrigger.ActivePulse),
                M0c2S3Fixture.Action(3,GameplayPrimitive.ChangeResource,1,5,GameplayActionTrigger.OnEnd)},
                costs??new[]{new GameplayCost(2,3)},1,cooldown,windup,active,recovery,period,4);
        [TestCase(0,0,0,1),TestCase(0,1,0,1),TestCase(1,0,0,1),TestCase(1,3,1,1),
         TestCase(2,5,2,2),TestCase(0,4,1,2),TestCase(3,2,0,3)]
        public void ZeroAndNonzeroStageDurationsMatchIndependentHalfOpenTimeline(int windup,int active,int recovery,int period)
        {
            var sim=M0c2S3Fixture.CreateWith(M0c2S3Fixture.Schema(abilities:new[]{Timeline(windup,active,recovery,period)}));
            var oracle=new AbilityOracle.Model(new AbilityOracle.Definition(windup,active,recovery,period,10,3),100000);
            oracle.Admit(1);M0c2S3Fixture.Input(sim,M0c2S3Fixture.Command(1,mode:GameplayTargetMode.Self));
            for(int tick=1;tick<=windup+active+recovery+3;tick++)
            {
                if(tick>1)sim.Step(default);
                Assert.That(sim.GameplayResource(M0c2S3Fixture.Actor.Id,1),Is.EqualTo(8000+11*oracle.ReleaseCount(tick)+3*oracle.PulseCount(tick)+5*oracle.EndCount(tick)),"tick "+tick);
                Assert.That((int)sim.GameplayCastPhase(M0c2S3Fixture.Actor.Id),Is.EqualTo((int)oracle.At(tick)),"phase tick "+tick);
            }
        }
        [Test] public void CooldownBeginsAtAdmissionAndEndsExactlyAtReadyTick()
        {
            var sim=M0c2S3Fixture.CreateWith(M0c2S3Fixture.Schema(abilities:new[]{Timeline(0,0,0,1,3)}));
            var oracle=new AbilityOracle.Model(new AbilityOracle.Definition(0,0,0,1,3,3),100000);
            for(int tick=1;tick<=4;tick++)
            {
                var expected=oracle.Admit(tick);M0c2S3Fixture.Input(sim,M0c2S3Fixture.Command(1,mode:GameplayTargetMode.Self));
                Assert.That(sim.GameplayInputResultAt(0).Failure,Is.EqualTo(expected==AbilityOracle.Failure.None?GameplayAbilityFailure.None:GameplayAbilityFailure.Cooldown),"tick "+tick);
                Assert.That(sim.GameplayResource(M0c2S3Fixture.Actor.Id,2),Is.EqualTo(oracle.Resources[0]));
                Assert.That(sim.GameplayResource(M0c2S3Fixture.Actor.Id,1),Is.EqualTo(8000+16*oracle.ReleaseCount(tick)));
            }
        }
        [Test] public void AllCostsAreRejectedTogetherWithoutCastTokenOrCooldownConsumption()
        {
            var ability=Timeline(costs:new[]{new GameplayCost(2,3),new GameplayCost(3,3)});
            var sim=M0c2S3Fixture.CreateWith(M0c2S3Fixture.Schema(abilities:new[]{ability}));
            Assert.DoesNotThrow(()=>M0c2S3Fixture.Input(sim,M0c2S3Fixture.Command(1,mode:GameplayTargetMode.Self)));
            Assert.That(sim.GameplayInputResultAt(0).Failure,Is.EqualTo(GameplayAbilityFailure.InsufficientResource));
            Assert.That(sim.GameplayResource(M0c2S3Fixture.Actor.Id,2),Is.EqualTo(100000));
            Assert.That(sim.GameplayResource(M0c2S3Fixture.Actor.Id,3),Is.EqualTo(2));
            Assert.That(sim.GameplayCastCount,Is.Zero);Assert.That(sim.GameplayCooldownReady(M0c2S3Fixture.Actor.Id,1),Is.Zero);
            Assert.That(sim.GameplayAbilityFailureCount(GameplayAbilityFailure.InsufficientResource),Is.EqualTo(1));
        }
        [TestCase(false),TestCase(true)] public void BothPackagesExecuteDataOnlyRangedAreaArmourHealingControlPeriodicAndSectorSkills(bool packageB)
        {
            var sim=M0c2S3Fixture.Create(packageB);Assert.That(M0c2S3Fixture.Definitions(packageB).Length,Is.GreaterThanOrEqualTo(5));
            long start=sim.GameplayResource(M0c2S3Fixture.Target.Id,1);int successful=0;bool sawCast=false,sawStatus=false;
            for(int tick=1;tick<=37;tick++)
            {
                M0c2S3Fixture.Step(sim);sawCast|=sim.GameplayCastCount!=0;sawStatus|=sim.GameplayStatusCount!=0;
                for(int i=0;i<sim.GameplayInputResultCount;i++)
                {Assert.That(sim.GameplayInputResultAt(i).Failure,Is.EqualTo(GameplayAbilityFailure.None),"tick "+tick);successful++;}
            }
            Assert.That(successful,Is.EqualTo(8));Assert.That(sawCast && sawStatus,Is.True);
            Assert.That(sim.GameplayResource(M0c2S3Fixture.Target.Id,1),Is.LessThan(start));
            Assert.That(sim.GameplayResource(M0c2S3Fixture.Actor.Id,1),Is.EqualTo(8017));
            Assert.That(sim.GameplayResource(M0c2S3Fixture.Actor.Id,2),Is.EqualTo(99989));
        }
        [Test] public void PackageBControlInterruptsUnreleasedWindupAndPreservesPaidCostAndCooldown()
        {
            var sim=M0c2S3Fixture.Create(true);
            M0c2S3Fixture.Input(sim,M0c2S3Fixture.Command(1),M0c2S3Fixture.Command(5,M0c2S3Fixture.OtherActor,
                target:new GameplayStamp(M0c2S3Fixture.Actor.Id)));
            Assert.That(sim.GameplayCastPhase(M0c2S3Fixture.Actor.Id),Is.EqualTo(GameplayCastPhase.Windup));
            sim.Step(default);Assert.That(sim.GameplayCastPhase(M0c2S3Fixture.Actor.Id),Is.EqualTo(GameplayCastPhase.Idle));
            M0c2S3Fixture.Input(sim,M0c2S3Fixture.Command(1));
            Assert.That(sim.GameplayInputResultAt(0).Failure,Is.EqualTo(GameplayAbilityFailure.BlockedState));
            Assert.That(sim.GameplayResource(M0c2S3Fixture.Actor.Id,2),Is.EqualTo(99997));
            Assert.That(sim.GameplayResource(M0c2S3Fixture.Target.Id,1),Is.EqualTo(8000));
            while(sim.Clock.Tick<5)sim.Step(default);
            M0c2S3Fixture.Input(sim,M0c2S3Fixture.Command(1));Assert.That(sim.GameplayInputResultAt(0).Failure,Is.EqualTo(GameplayAbilityFailure.Cooldown));
            while(sim.Clock.Tick<11)sim.Step(default);
            M0c2S3Fixture.Input(sim,M0c2S3Fixture.Command(1));Assert.That(sim.GameplayInputResultAt(0).Failure,Is.EqualTo(GameplayAbilityFailure.None));
        }
        [TestCase(9),TestCase(10)] public void OptionalFutureCapabilitiesRejectWithoutResourceOrCooldownMutation(int slot)
        {
            var sim=M0c2S3Fixture.Create(true);M0c2S3Fixture.Input(sim,M0c2S3Fixture.Command((ulong)slot));
            Assert.That(sim.GameplayInputResultAt(0).Failure,Is.EqualTo(GameplayAbilityFailure.UnsupportedAction));
            Assert.That(sim.GameplayResource(M0c2S3Fixture.Actor.Id,2),Is.EqualTo(100000));Assert.That(sim.GameplayCastCount,Is.Zero);
        }
        [TestCase(1),TestCase(3),TestCase(8)] public void CancelStopsFutureEventsAndNeverRefundsPaidCost(int cancelTick)
        {
            var sim=M0c2S3Fixture.CreateWith(M0c2S3Fixture.Schema(abilities:new[]{Timeline()}));
            var oracle=new AbilityOracle.Model(new AbilityOracle.Definition(2,5,2,2,10,3),100000);oracle.Admit(1);
            M0c2S3Fixture.Input(sim,M0c2S3Fixture.Command(1,mode:GameplayTargetMode.Self));
            int actualCancel=cancelTick==1?2:cancelTick;
            for(int tick=2;tick<=12;tick++)
            {
                if(tick==actualCancel){oracle.Cancel(tick);M0c2S3Fixture.Input(sim,M0c2S3Fixture.Command(1,mode:GameplayTargetMode.Self,kind:GameplayInputKind.Cancel));}
                else sim.Step(default);
                Assert.That(sim.GameplayResource(M0c2S3Fixture.Actor.Id,1),Is.EqualTo(8000+11*oracle.ReleaseCount(tick)+3*oracle.PulseCount(tick)+5*oracle.EndCount(tick)),"tick "+tick);
            }
            Assert.That(sim.GameplayResource(M0c2S3Fixture.Actor.Id,2),Is.EqualTo(99997));
        }
        [Test,Category("M0c2S3Limit")]
        public void ZeroActiveDurationAtMaximumTickNeverSchedulesItsUnusedPulse()
        {
            var definition=new GameplayAbilityDef(1,"limit-zero-active",1,1,new GameplayTargetSpec(GameplayTargetMode.Self),new[]{
                M0c2S3Fixture.Action(1,GameplayPrimitive.ChangeResource,1,7),
                M0c2S3Fixture.Action(2,GameplayPrimitive.ChangeResource,1,99,GameplayActionTrigger.ActivePulse)},pulsePeriodTicks:1);
            var sim=M0c2S3Fixture.CreateWith(M0c2S3Fixture.Schema(abilities:new[]{definition}));sim.Clock.Restore(long.MaxValue-2);
            typeof(SimScheduler).GetField("advancedThrough",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).SetValue(sim.Scheduler,long.MaxValue-2);
            typeof(SimEventBuffer).GetField("currentTick",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).SetValue(sim.Events,long.MaxValue-2);
            Assert.DoesNotThrow(()=>M0c2S3Fixture.Input(sim,M0c2S3Fixture.Command(1,mode:GameplayTargetMode.Self)));
            Assert.That(sim.GameplayResource(M0c2S3Fixture.Actor.Id,1),Is.EqualTo(8007));Assert.That(sim.GameplayCastCount,Is.Zero);
        }
        [Test] public void InputAdmissionAndEveryCastBoundaryMatchIndependentTimeline()
        {
            var sim=M0c2S3Fixture.CreateWith(M0c2S3Fixture.Schema(abilities:new[]{Timeline()}));
            var oracle=new AbilityOracle.Model(new AbilityOracle.Definition(2,5,2,2,10,3),100000);
            Assert.That(oracle.Admit(1),Is.EqualTo(AbilityOracle.Failure.None));
            M0c2S3Fixture.Input(sim,M0c2S3Fixture.Command(1,mode:GameplayTargetMode.Self));
            Assert.That(sim.GameplayResource(M0c2S3Fixture.Actor.Id,2),Is.EqualTo(99997),"successful InputFrame admission pays the whole fee once");
            for(int tick=1;tick<=12;tick++)
            {
                if(tick>1)sim.Step(default);
                long expected=8000+11*oracle.ReleaseCount(tick)+3*oracle.PulseCount(tick)+5*oracle.EndCount(tick);
                Assert.That(sim.GameplayResource(M0c2S3Fixture.Actor.Id,1),Is.EqualTo(expected),"timeline tick "+tick);
                Assert.That((int)sim.GameplayCastPhase(M0c2S3Fixture.Actor.Id),Is.EqualTo((int)oracle.At(tick)),"phase tick "+tick);
            }
        }
    }
}
