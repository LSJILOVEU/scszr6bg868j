using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using WarCore.Sim;
namespace WarCore.Tests
{
    [TestFixture,Category("M0c2S4")]
    public sealed class M0c2S4BoundaryTests
    {
        [Test] public void ThresholdEqualityNeverRecoversAndM13DoesNotChainTransitions()
        {
            var s=M0c2S4Fixture.Create(schema:M0c2S4Fixture.Schema(duration:1));M0c2S4Fixture.Step(s,1);
            Assert.That(M0c2S4Fixture.Life(s).State,Is.EqualTo(GameplayLifeState.Downed));Assert.That(M0c2S4Fixture.Life(s).DownedDueTick,Is.EqualTo(2));
            M0c2S4Fixture.Step(s,2048);Assert.That(M0c2S4Fixture.Health(s),Is.Zero);Assert.That(M0c2S4Fixture.Life(s).State,Is.EqualTo(GameplayLifeState.Dead));
            int n=0;foreach(var e in s.Events.Read(1))if(e.Type.Equals(GameplayEvents.Life))n++;Assert.That(n,Is.EqualTo(1));
        }
        [Test] public void PeriodicDamageContinuesWhileDownedAndCanPreventExpiryRescue()
        {
            var s=M0c2S4Fixture.Create();var control=M0c2S4Fixture.Create();M0c2S4Fixture.Step(s,1|8);M0c2S4Fixture.Step(control,1);
            for(int i=0;i<3;i++){M0c2S4Fixture.Step(s);M0c2S4Fixture.Step(control);Assert.That(M0c2S4Fixture.Life(s).EnteredTick,Is.EqualTo(1));}
            M0c2S4Fixture.Step(s,1024);M0c2S4Fixture.Step(control,1024);
            Assert.That(M0c2S4Fixture.Health(s),Is.EqualTo(-2));Assert.That(M0c2S4Fixture.Life(s).State,Is.EqualTo(GameplayLifeState.Dead));
            Assert.That(M0c2S4Fixture.Life(control).State,Is.EqualTo(GameplayLifeState.Active));
        }
        [Test] public void AnyTerminationBitWinsForHealthyActiveAndDowned()
        {
            var schema=M0c2S4Fixture.Schema(terminate:24);var a=M0c2S4Fixture.Create(schema:schema);var d=M0c2S4Fixture.Create(schema:schema);
            M0c2S4Fixture.Step(a,64);Assert.That(M0c2S4Fixture.Health(a),Is.EqualTo(100));Assert.That(M0c2S4Fixture.Life(a).State,Is.EqualTo(GameplayLifeState.Dead));
            M0c2S4Fixture.Step(d,1);M0c2S4Fixture.Step(d,64|256);Assert.That(M0c2S4Fixture.Life(d).State,Is.EqualTo(GameplayLifeState.Dead));
        }
        [Test] public void CooldownsAndStatusExpiryContinueDuringLongDownedPeriod()
        {
            var s=M0c2S4Fixture.Create(schema:M0c2S4Fixture.Schema(duration:30));M0c2S4Fixture.Step(s,1|8,M0c2S4Fixture.Cast());
            while(s.Clock.Tick<21)M0c2S4Fixture.Step(s);
            Assert.That(M0c2S4Fixture.Life(s).State,Is.EqualTo(GameplayLifeState.Downed));Assert.That(s.GameplayStatusCount,Is.Zero);
            Assert.That(s.GameplayCooldownReady(M0c2S4Fixture.Actor.Id,1),Is.Zero);Assert.That(s.Scheduler.PendingCount,Is.EqualTo(1));
        }
        [Test] public void SourceDeathPreservesAlreadyAppliedForeignStatusAndItsSnapshot()
        {
            var s=M0c2S4Fixture.Create(true);M0c2S4Fixture.Step(s,1|512);
            Assert.That(M0c2S4Fixture.Life(s).State,Is.EqualTo(GameplayLifeState.Dead));Assert.That(M0c2S4Fixture.Statuses(s,M0c2S4Fixture.Other.Id),Is.EqualTo(1));
            var before=s.GameplayStatusAt(0);var restored=M0c2S4Fixture.Create(true,true);restored.RestoreRetainedSnapshot(s.SaveRetainedSnapshot(),true);
            for(int i=0;i<5;i++){M0c2S4Fixture.Step(s);M0c2S4Fixture.Step(restored);Assert.That(s.StateHashRetained(),Is.EqualTo(restored.StateHashRetained()));}
            Assert.That(s.GameplayResource(M0c2S4Fixture.Other.Id,1),Is.EqualTo(85));var after=s.GameplayStatusAt(0);
            Assert.That(after.SourceSnapshot.Source.Id,Is.EqualTo(M0c2S4Fixture.Actor.Id));Assert.That(after.Key.Token,Is.EqualTo(before.Key.Token));Assert.That(after.SourceSnapshot.CapturedTick,Is.EqualTo(1));
        }
        private static List<TaskKey> Pending(WarCore.Sim.Sim s)
        {
            var tasks=(StableScheduledTask[])typeof(SimScheduler).GetField("stableTasks",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(s.Scheduler);
            var result=new List<TaskKey>();foreach(var task in tasks)if(task.Key.DueTick>0 && s.Scheduler.TryGetPendingStable(task.Key,out _))result.Add(task.Key);return result;
        }
        [Test] public void DeathCancelsFullOwnedKeysAndPreservesOtherOwnersKeys()
        {
            var s=M0c2S4Fixture.Create();M0c2S4Fixture.Step(s,8);M0c2S4Fixture.Step(s,1,M0c2S4Fixture.Cast(),M0c2S4Fixture.Cast(actor:M0c2S4Fixture.Other));
            var keys=Pending(s);int own=0,foreign=0;
            foreach(var key in keys)if(key.Origin.Equals(M0c2S4Fixture.Actor.Id))own++;else foreign++;
            Assert.That(own,Is.EqualTo(4));Assert.That(foreign,Is.EqualTo(3));M0c2S4Fixture.Step(s,64);
            foreach(var key in keys)if(key.DueTick>3)Assert.That(s.Scheduler.TryGetPendingStable(key,out _),Is.EqualTo(!key.Origin.Equals(M0c2S4Fixture.Actor.Id)),"complete key "+key.Kind+"/"+key.Counter);
            Assert.That(s.Scheduler.PendingCount,Is.EqualTo(3));Assert.That(M0c2S4Fixture.Statuses(s,M0c2S4Fixture.Actor.Id),Is.Zero);
        }
        [Test] public void DownedTimeSaturatesBeforeAdditionAndRestoresAtTheLimit()
        {
            var s=M0c2S4Fixture.Create(schema:M0c2S4Fixture.Schema(duration:int.MaxValue));long now=long.MaxValue-3;
            s.Clock.Restore(now);
            typeof(SimScheduler).GetField("advancedThrough",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(s.Scheduler,now);
            typeof(SimEventBuffer).GetField("currentTick",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(s.Events,now);
            M0c2S4Fixture.Step(s,1);Assert.That(M0c2S4Fixture.Life(s).DownedDueTick,Is.EqualTo(long.MaxValue-1));
            var doc=new M0c2S2ProtocolTests.Document(s);Assert.That(M0c2S2ProtocolTests.Word(doc.Tables[8][0],24),Is.EqualTo(1));
            var r=M0c2S4Fixture.Create(schema:M0c2S4Fixture.Schema(duration:int.MaxValue));r.RestoreRetainedSnapshot(s.SaveRetainedSnapshot(),true);
            M0c2S4Fixture.Step(s);M0c2S4Fixture.Step(r);Assert.That(s.StateHashRetained(),Is.EqualTo(r.StateHashRetained()));
            Assert.That(M0c2S4Fixture.Life(s).State,Is.EqualTo(GameplayLifeState.Dead));
        }
        [Test] public void NonDownableRulesRequireZeroDurationAndLifeResourceReference()
        {
            Func<GameplayLifeRuleDef,GameplaySchema> build=r=>new GameplaySchema(new[]{new GameplayResourceDef(1,"life",0,100,100,isLife:true)},
                new GameplayDamageTypeDef[0],new GameplayPipelineDef[0],new[]{new GameplayArchetypeDef(1,new[]{1UL})},new GameplayCapacities(1,1,2,4),"life",
                lifeRules:new[]{r});
            Assert.Throws<SimBoundaryException>(()=>build(new GameplayLifeRuleDef(1,1,false,1)));
            Assert.Throws<SimBoundaryException>(()=>build(new GameplayLifeRuleDef(1,2,true,1)));
            Assert.DoesNotThrow(()=>build(new GameplayLifeRuleDef(1,1,false,0)));
            Assert.DoesNotThrow(()=>build(new GameplayLifeRuleDef(1,1,true,int.MaxValue)));
            Assert.Throws<SimBoundaryException>(()=>new GameplaySchema(new[]{new GameplayResourceDef(1,"life",0,100,100,isLife:true)},
                new GameplayDamageTypeDef[0],new GameplayPipelineDef[0],new[]{new GameplayArchetypeDef(1,new[]{1UL})},
                new GameplayCapacities(1,1,2,4,casts:int.MinValue,inputCommands:int.MinValue),"life",lifeRules:new[]{new GameplayLifeRuleDef(1,1,true,1)}));
        }
        [Test] public void LifeTaskReservationPreventsEarlierAdmissionFromStealingItsSlot()
        {
            var s=M0c2S4Fixture.Create(timers:2);M0c2S4Fixture.Step(s,1,M0c2S4Fixture.Cast());
            Assert.That(s.GameplayInputResultAt(0).Failure,Is.EqualTo(GameplayAbilityFailure.TimerCapacity));
            Assert.That(M0c2S4Fixture.Life(s).State,Is.EqualTo(GameplayLifeState.Downed));Assert.That(s.Scheduler.PendingCount,Is.EqualTo(1));
        }
    }
}
