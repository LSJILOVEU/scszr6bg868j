using System;
using NUnit.Framework;
using WarCore.Sim;
namespace WarCore.Tests
{
    [TestFixture,Category("M0c2S3"),Category("M0c2S3Capacity")]
    public sealed class M0c2S3CapacityTests
    {
        [Test]
        public void FutureStatusPeriodicFanoutRejectsAtAdmissionWithoutPaying()
        {
            var d=new GameplayAbilityDef(1,"capacity",1,1,new GameplayTargetSpec(GameplayTargetMode.Self),
                new[]{M0c2S3Fixture.Action(1,GameplayPrimitive.ApplyStatus,1,-1)},new[]{new GameplayCost(2,3)},windupTicks:1);
            var baseSchema=M0c2S3Fixture.Schema(abilities:new[]{d},commands:4);
            var resources=new GameplayResourceDef[baseSchema.ResourceCount];for(int i=0;i<resources.Length;i++)resources[i]=baseSchema.ResourceAt(i);
            var types=new GameplayDamageTypeDef[baseSchema.DamageTypeCount];for(int i=0;i<types.Length;i++)types[i]=baseSchema.DamageTypeAt(i);
            var pipes=new GameplayPipelineDef[baseSchema.PipelineCount];for(int i=0;i<pipes.Length;i++)pipes[i]=baseSchema.PipelineAt(i);
            var archetypes=new GameplayArchetypeDef[baseSchema.ArchetypeCount];for(int i=0;i<archetypes.Length;i++)archetypes[i]=baseSchema.ArchetypeAt(i);
            var status=new GameplayStatusDef(1,"four-pulses",GameplayStatusDuration.Timed,5,1,1,periodicActions:new[]{
                GameplayPeriodicAction.Damage(1,1,1),GameplayPeriodicAction.Damage(2,1,1),GameplayPeriodicAction.Damage(3,1,1),GameplayPeriodicAction.Damage(4,1,1)});
            var schema=new GameplaySchema(resources,types,pipes,archetypes,baseSchema.Capacities,baseSchema.CriticalStream,new[]{status},new[]{d},baseSchema.InputAuthority);
            var sim=M0c2S3Fixture.CreateWith(schema);
            Assert.DoesNotThrow(()=>M0c2S3Fixture.Input(sim,M0c2S3Fixture.Command(1,mode:GameplayTargetMode.Self)));
            Assert.That(sim.GameplayInputResultAt(0).Failure,Is.EqualTo(GameplayAbilityFailure.CommandCapacity));
            Assert.That(sim.GameplayResource(M0c2S3Fixture.Actor.Id,2),Is.EqualTo(100000));Assert.That(sim.GameplayCastCount,Is.Zero);
        }
        private static GameplayAbilityDef TightTimerDefinition(bool cooldown)
            =>new GameplayAbilityDef(1,"tight-timer",1,1,new GameplayTargetSpec(GameplayTargetMode.Self),new[]{
                M0c2S3Fixture.Action(1,GameplayPrimitive.ChangeResource,1,7),
                M0c2S3Fixture.Action(2,GameplayPrimitive.ChangeResource,1,2,GameplayActionTrigger.ActivePulse)},
                new[]{new GameplayCost(2,3)},cooldownTicks:cooldown?10:0,windupTicks:3,activeTicks:4,recoveryTicks:2,pulsePeriodTicks:1);
        [TestCase(false,1),TestCase(true,1),TestCase(false,4),TestCase(true,4),TestCase(false,8),TestCase(true,8)]
        public void TightTimerBudgetRestoresWindupActiveAndRecoveryWithoutCountingOwnedTasksTwice(bool cooldown,int checkpoint)
        {
            int capacity=cooldown?3:2;var schema=M0c2S3Fixture.Schema(abilities:new[]{TightTimerDefinition(cooldown)});
            var direct=M0c2S3Fixture.CreateWith(schema,timerCapacity:capacity);
            Assert.DoesNotThrow(()=>M0c2S3Fixture.Input(direct,M0c2S3Fixture.Command(1,mode:GameplayTargetMode.Self)));
            Assert.That(direct.GameplayInputResultAt(0).Failure,Is.EqualTo(GameplayAbilityFailure.None));
            while(direct.Clock.Tick<checkpoint)Assert.DoesNotThrow(()=>direct.Step(default));
            Assert.That(direct.Scheduler.PendingCount,Is.EqualTo((checkpoint==4?2:1)+(cooldown?1:0)));
            var resumed=M0c2S3Fixture.CreateWith(schema,true,1,timerCapacity:capacity);
            Assert.DoesNotThrow(()=>resumed.RestoreRetainedSnapshot(direct.SaveRetainedSnapshot(),true),"owned phase/pulse tasks are already charged to PendingCount");
            Assert.That(resumed.StateHashRetained(),Is.EqualTo(direct.StateHashRetained()));
            for(int tick=checkpoint+1;tick<=12;tick++)
            {
                Assert.DoesNotThrow(()=>direct.Step(default));Assert.DoesNotThrow(()=>resumed.Step(default));
                Assert.That(resumed.StateHashRetained(),Is.EqualTo(direct.StateHashRetained()),"tick "+tick);
            }
            Assert.That(direct.GameplayResource(M0c2S3Fixture.Actor.Id,1),Is.EqualTo(8013));
            Assert.That(direct.GameplayResource(M0c2S3Fixture.Actor.Id,2),Is.EqualTo(99997));
        }
        [TestCase(false),TestCase(true)]
        public void TimerBudgetBelowItsDeclaredWorstCaseRejectsTheWholeAdmission(bool cooldown)
        {
            var schema=M0c2S3Fixture.Schema(abilities:new[]{TightTimerDefinition(cooldown)});
            var sim=M0c2S3Fixture.CreateWith(schema,timerCapacity:cooldown?2:1);
            Assert.DoesNotThrow(()=>M0c2S3Fixture.Input(sim,M0c2S3Fixture.Command(1,mode:GameplayTargetMode.Self)));
            Assert.That(sim.GameplayInputResultAt(0).Failure,Is.EqualTo(GameplayAbilityFailure.TimerCapacity));
            Assert.That(sim.GameplayResource(M0c2S3Fixture.Actor.Id,2),Is.EqualTo(100000));
            Assert.That(sim.GameplayResource(M0c2S3Fixture.Actor.Id,1),Is.EqualTo(8000));
            Assert.That(sim.GameplayCastCount,Is.Zero);Assert.That(sim.Scheduler.PendingCount,Is.Zero);
            Assert.That(sim.GameplayCooldownReady(M0c2S3Fixture.Actor.Id,0),Is.Zero);
        }
        private sealed class ExternalSubmit : IRetainedGameplaySystem
        {
            public int Order=>1;public string StateTypeId=>"s3.external-capacity";public int StateVersion=>1;
            public GameplayFailure Failure;
            public void Step(SimContext c)
            {
                if(c.Clock.Tick!=2)return;
                var owner=c.Owner.GameplayTarget(M0c2S3Fixture.OtherActor.Id);var key=new GameplayApplyKey(owner.Id,99,0,owner.Id,99,1);
                Failure=c.SubmitGameplay(owner,new[]{new GameplayCost(2,3)},new[]{GameplayAction.ChangeResource(owner,key,1,9)},1);
            }
            public void SaveState(SimStateWriter w){}public void LoadState(SimStateReader r){}public void WriteState(StateHash h){}
        }
        [Test]
        public void ExistingSubmitCannotSpendTheCapacityReservedByAnEarlierWindup()
        {
            var d=new GameplayAbilityDef(1,"external-reserved",1,1,new GameplayTargetSpec(GameplayTargetMode.Self),
                new[]{M0c2S3Fixture.Action(1,GameplayPrimitive.ChangeResource,1,7)},new[]{new GameplayCost(2,3)},windupTicks:3);
            var sim=M0c2S3Fixture.CreateWith(M0c2S3Fixture.Schema(abilities:new[]{d},commands:1));var script=new ExternalSubmit();
            sim.RegisterGameplay("s3.external-capacity",script,SimSystemPhase.Queries);
            M0c2S3Fixture.Input(sim,M0c2S3Fixture.Command(1,mode:GameplayTargetMode.Self));Assert.DoesNotThrow(()=>sim.Step(default));
            Assert.That(script.Failure,Is.EqualTo(GameplayFailure.CommandCapacity));Assert.That(sim.GameplayResource(M0c2S3Fixture.OtherActor.Id,2),Is.EqualTo(100000));
            sim.Step(default);Assert.DoesNotThrow(()=>sim.Step(default));Assert.That(sim.GameplayResource(M0c2S3Fixture.Actor.Id,1),Is.EqualTo(8007));
        }
        [Test]
        public void WindupReservesNextTickCapacityAndRestoreRebuildsTheSameBudget()
        {
            var d=new GameplayAbilityDef(1,"reserved",1,1,new GameplayTargetSpec(GameplayTargetMode.Self),
                new[]{M0c2S3Fixture.Action(1,GameplayPrimitive.ChangeResource,1,7)},new[]{new GameplayCost(2,3)},windupTicks:3);
            var schema=M0c2S3Fixture.Schema(abilities:new[]{d},commands:1);
            var direct=M0c2S3Fixture.CreateWith(schema);M0c2S3Fixture.Input(direct,M0c2S3Fixture.Command(1,mode:GameplayTargetMode.Self));
            var resumed=M0c2S3Fixture.CreateWith(schema,true,1);resumed.RestoreRetainedSnapshot(direct.SaveRetainedSnapshot(),true);
            foreach(var sim in new[]{direct,resumed})
            {
                Assert.DoesNotThrow(()=>M0c2S3Fixture.Input(sim,M0c2S3Fixture.Command(1,M0c2S3Fixture.OtherActor,mode:GameplayTargetMode.Self)));
                Assert.That(sim.GameplayInputResultAt(0).Failure,Is.EqualTo(GameplayAbilityFailure.CommandCapacity));
                Assert.That(sim.GameplayResource(M0c2S3Fixture.OtherActor.Id,2),Is.EqualTo(100000));
                sim.Step(default);sim.Step(default);Assert.That(sim.GameplayResource(M0c2S3Fixture.Actor.Id,1),Is.EqualTo(8007));
            }
            Assert.That(resumed.StateHashRetained(),Is.EqualTo(direct.StateHashRetained()));
        }
    }
}
