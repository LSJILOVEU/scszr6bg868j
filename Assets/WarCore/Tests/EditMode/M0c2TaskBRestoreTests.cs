using System;
using NUnit.Framework;
using WarCore.Sim;
using Simulation=WarCore.Sim.Sim;

namespace WarCore.Tests
{
    [TestFixture,Category("M0c2TaskBRestore"),Category("M0c2S4")]
    public sealed class M0c2TaskBRestoreTests
    {
        private static readonly StableEntityKey Actor=StableEntityKey.Placed(103,1);
        private static ulong Word(byte[] row,int at)=>M0c2S2ProtocolTests.Word(row,at);
        private static void Put(byte[] row,int at,ulong value)=>M0c2S2ProtocolTests.Put(row,at,value);
        private static Simulation World(bool allowed=false,int regenPeriod=0,int cooldown=20)
        {
            var ability=new GameplayAbilityDef(1,"restore-history",1,1,new GameplayTargetSpec(GameplayTargetMode.Self),
                new GameplayAbilityAction[0],cooldownKey:1,cooldownTicks:cooldown,windupTicks:10,allowWhileDowned:allowed);
            var schema=new GameplaySchema(new[]{new GameplayResourceDef(1,"health",-100,100,100,regenPeriod>0?1:0,regenPeriod,isLife:true)},
                new GameplayDamageTypeDef[0],new GameplayPipelineDef[0],new[]{new GameplayArchetypeDef(1,new[]{1UL})},
                new GameplayCapacities(1,1,16,16,cooldowns:1,casts:1,inputCommands:2,targetsPerCast:1),"task-b.restore",
                abilities:new[]{ability},inputAuthority:new GameplayInputAuthority(1,new[]{Actor.Id}),
                lifeRules:new[]{new GameplayLifeRuleDef(1,1,true,4)});
            var sim=Simulation.CreateRetainedWorld(103,new PlacedCatalog(new StableContentManifest(0),new PlacedCatalogEntry(103,1,1)),
                new SimConfig(entityCapacity:2,timerCapacity:16),gameplaySchema:schema);
            sim.BindGameplay(sim.Entities.Create(Actor),1);sim.RegisterGameplay("task-b.restore",new LifeScript(),SimSystemPhase.Queries);return sim;
        }
        private static void Step(Simulation sim,bool cast=false,int change=0)
        {
            var commands=cast?new[]{new GameplayInputCommand(new GameplayStamp(Actor.Id),1,GameplayInputKind.Activate,1)}:null;
            sim.Step(new InputFrame(default,default,skillSlot:change<0?1:change>0?2:0,gameplayTick:sim.Clock.Tick+1,skillCommands:commands));
        }
        private sealed class LifeScript:IRetainedGameplaySystem
        {
            public int Order=>1;public string StateTypeId=>"test.task-b.restore";public int StateVersion=>1;
            public void Step(SimContext c)
            {
                if(c.Input.SkillSlot!=0)c.ChangeResource(c.Owner.GameplayTarget(Actor.Id),
                    new GameplayApplyKey(Actor.Id,(ulong)c.Clock.Tick,0,Actor.Id,(ulong)c.Clock.Tick,10),1,c.Input.SkillSlot==1?-100:100);
            }
            public void SaveState(SimStateWriter w){}public void LoadState(SimStateReader r){}public void WriteState(StateHash h){}
        }
        [Test,Category("TaskBSnap2")]
        public void DownedSnapshotRejectsPermittedCastCapturedAtDownedEntry()
        {
            var original=World(allowed:true);Step(original,cast:true);
            Assert.That(original.GameplayCastCount,Is.EqualTo(1));
            TaskKey expiry=original.Scheduler.ScheduleFor(5,0,Actor.Id,0,
                new StableSimEvent(GameplayEvents.DownedExpiry,target:new EntityLink(Actor.Id)));
            var document=new M0c2S2ProtocolTests.Document(original);byte[] owner=document.Tables[2][0];Array.Resize(ref owner,98);
            Put(owner,33,1);Put(owner,41,1);Put(owner,49,5);owner[57]=1;
            Put(owner,58,5);Put(owner,66,0);Put(owner,74,expiry.Origin.Value);Put(owner,82,expiry.Counter);Put(owner,90,0);
            document.Tables[2][0]=owner;Put(document.Tables[0][0],0,1);Put(document.Tables[3][0],25,0);
            var restored=World(allowed:true);
            var failure=Assert.Throws<SimSnapshotException>(()=>restored.RestoreRetainedSnapshot(document.Encode(),true));
            StringAssert.Contains("M0C_DOWNED_CAST_HISTORY",failure.Message);
        }
        [Test,Category("TaskBSnap2")]
        public void RecoveredSnapshotRejectsDisallowedCastCapturedBeforeRecovery()
        {
            var original=World();Step(original,cast:true);Step(original);
            var document=new M0c2S2ProtocolTests.Document(original);Put(document.Tables[2][0],41,2);
            var restored=World();var failure=Assert.Throws<SimSnapshotException>(()=>restored.RestoreRetainedSnapshot(document.Encode(),true));
            StringAssert.Contains("M0C_RECOVERED_CAST_HISTORY",failure.Message);
        }
        [Test,Category("TaskBSnap2")]
        public void PermittedCastAfterDownedEntryRestoresBeforeAndAfterRecovery()
        {
            var original=World(allowed:true);Step(original,change:-100);Step(original,cast:true);
            Assert.That(original.GameplayLife(Actor.Id).State,Is.EqualTo(GameplayLifeState.Downed));
            var restored=World(allowed:true);restored.RestoreRetainedSnapshot(original.SaveRetainedSnapshot(),true);
            Assert.That(restored.StateHashRetained(),Is.EqualTo(original.StateHashRetained()));
            Step(original,change:100);Step(restored,change:100);
            Assert.That(original.GameplayLife(Actor.Id).State,Is.EqualTo(GameplayLifeState.Active));
            Assert.That(restored.StateHashRetained(),Is.EqualTo(original.StateHashRetained()));
            restored=World(allowed:true);restored.RestoreRetainedSnapshot(original.SaveRetainedSnapshot(),true);
            Assert.That(restored.SaveRetainedSnapshot(),Is.EqualTo(original.SaveRetainedSnapshot()));
            for(int i=0;i<10;i++){Step(original);Step(restored);Assert.That(restored.StateHashRetained(),Is.EqualTo(original.StateHashRetained()));}
        }
        [Test,Category("TaskBSnap2"),Category("TaskBSnap2Followup")]
        public void RecoveredCaptureEqualityRejectsDisallowedCastButNextTickRestores()
        {
            var original=World();Step(original,change:-100);Step(original,change:100);Step(original,cast:true);
            Assert.That(original.GameplayLife(Actor.Id).EnteredTick,Is.EqualTo(2));
            // Capture at 3 is one tick after recovery: a normal cast is legal.
            var restored=World();byte[] valid=original.SaveRetainedSnapshot();
            Assert.DoesNotThrow(()=>restored.RestoreRetainedSnapshot(valid,true));
            Assert.That(restored.SaveRetainedSnapshot(),Is.EqualTo(valid));
            Assert.That(restored.StateHashRetained(),Is.EqualTo(original.StateHashRetained()));
            // Only change the entered tick. Equality must require AllowWhileDowned.
            var document=new M0c2S2ProtocolTests.Document(original);Put(document.Tables[2][0],41,3);
            var rejected=World();var failure=Assert.Throws<SimSnapshotException>(()=>rejected.RestoreRetainedSnapshot(document.Encode(),true));
            StringAssert.Contains("M0C_RECOVERED_CAST_HISTORY",failure.Message);
        }
        [Test,Category("TaskBSnap2"),Category("TaskBSnap2Followup")]
        public void AllowedCaptureOneTickAfterDownedAndAtRecoveryRoundTripsExactly()
        {
            var downed=World(allowed:true);Step(downed,change:-100);Step(downed,cast:true);
            Assert.That(downed.GameplayLife(Actor.Id).State,Is.EqualTo(GameplayLifeState.Downed));
            Assert.That(downed.GameplayLife(Actor.Id).EnteredTick,Is.EqualTo(1));
            var downedCopy=World(allowed:true);byte[] downedBytes=downed.SaveRetainedSnapshot();
            Assert.DoesNotThrow(()=>downedCopy.RestoreRetainedSnapshot(downedBytes,true));
            Assert.That(downedCopy.SaveRetainedSnapshot(),Is.EqualTo(downedBytes));
            Assert.That(downedCopy.StateHashRetained(),Is.EqualTo(downed.StateHashRetained()));
            // Q0 admits while Downed; Q3 recovers on the same tick, so capture==entered==2.
            var recovered=World(allowed:true);Step(recovered,change:-100);Step(recovered,cast:true,change:100);
            Assert.That(recovered.GameplayLife(Actor.Id).State,Is.EqualTo(GameplayLifeState.Active));
            Assert.That(recovered.GameplayLife(Actor.Id).EnteredTick,Is.EqualTo(2));
            Assert.That(recovered.GameplayCastCount,Is.EqualTo(1));
            var copy=World(allowed:true);byte[] bytes=recovered.SaveRetainedSnapshot();
            Assert.DoesNotThrow(()=>copy.RestoreRetainedSnapshot(bytes,true));
            Assert.That(copy.SaveRetainedSnapshot(),Is.EqualTo(bytes));
            Assert.That(copy.StateHashRetained(),Is.EqualTo(recovered.StateHashRetained()));
            Step(recovered);Step(copy);Assert.That(copy.SaveRetainedSnapshot(),Is.EqualTo(recovered.SaveRetainedSnapshot()));
            Assert.That(copy.StateHashRetained(),Is.EqualTo(recovered.StateHashRetained()));
        }
        [Test,Category("TaskBSnap2"),Category("TaskBSnap2Followup")]
        public void DownedEntryDeletesSameTickAllowedCastButKeepsCooldown()
        {
            var original=World(allowed:true);Step(original,cast:true,change:-100);
            Assert.That(original.GameplayLife(Actor.Id).State,Is.EqualTo(GameplayLifeState.Downed));
            Assert.That(original.GameplayCastCount,Is.Zero,"Q3 Downed entry must cancel the Q0 cast even when allowed");
            var document=new M0c2S2ProtocolTests.Document(original);
            Assert.That(document.Tables[4].Count,Is.EqualTo(1),"paid cooldown survives Downed entry");
            Assert.That(document.Tables[5].Count,Is.Zero,"no cast row survives the entry tick");
            var restored=World(allowed:true);byte[] bytes=original.SaveRetainedSnapshot();
            Assert.DoesNotThrow(()=>restored.RestoreRetainedSnapshot(bytes,true));
            Assert.That(restored.SaveRetainedSnapshot(),Is.EqualTo(bytes));
            Assert.That(restored.StateHashRetained(),Is.EqualTo(original.StateHashRetained()));
        }
        private static TaskKey ReadTask(byte[] row,int at)
            =>new TaskKey((long)Word(row,at),Word(row,at+8),new StableId(Word(row,at+16)),Word(row,at+24),Word(row,at+32));
        [TestCase("missing"),TestCase("wrong-ready"),Category("TaskBSnap3")]
        public void CastSnapshotRequiresItsExactUnexpiredCooldownWithMatchingScheduler(string fault)
        {
            var original=World();Step(original,cast:true);
            var before=new M0c2S2ProtocolTests.Document(original);TaskKey old=ReadTask(before.Tables[4][0],34);
            Assert.That(original.Scheduler.CancelStable(old),Is.True);
            TaskKey replacement=default;
            if(fault=="wrong-ready")replacement=original.Scheduler.ScheduleFor(20,0,Actor.Id,1,
                new StableSimEvent(GameplayEvents.AbilityCooldown,target:new EntityLink(Actor.Id)));
            var document=new M0c2S2ProtocolTests.Document(original);
            if(fault=="missing")document.Tables[4].Clear();
            else{byte[] row=document.Tables[4][0];Put(row,25,20);Put(row,34,20);Put(row,58,replacement.Counter);}
            var restored=World();var failure=Assert.Throws<SimSnapshotException>(()=>restored.RestoreRetainedSnapshot(document.Encode(),true));
            StringAssert.Contains("M0C_CAST_COOLDOWN",failure.Message);
        }
        [TestCase(0),TestCase(2),Category("TaskBSnap3")]
        public void CastWithZeroOrExpiredCooldownRestoresWithoutCooldownRow(int cooldown)
        {
            var original=World(cooldown:cooldown);Step(original,cast:true);Step(original);Step(original);
            var document=new M0c2S2ProtocolTests.Document(original);Assert.That(document.Tables[4].Count,Is.Zero);
            Assert.That(original.GameplayCastCount,Is.EqualTo(1));
            var restored=World(cooldown:cooldown);restored.RestoreRetainedSnapshot(original.SaveRetainedSnapshot(),true);
            Assert.That(restored.SaveRetainedSnapshot(),Is.EqualTo(original.SaveRetainedSnapshot()));
            for(int i=0;i<10;i++){Step(original);Step(restored);Assert.That(restored.StateHashRetained(),Is.EqualTo(original.StateHashRetained()));}
        }
        [TestCase(5L),TestCase(1000000L),Category("TaskBRegen")]
        public void RegenSnapshotRejectsDueBeyondOnePeriodWithMatchingScheduler(long due)
        {
            var original=World(regenPeriod:3);Step(original);
            var before=new M0c2S2ProtocolTests.Document(original);TaskKey old=ReadTask(before.Tables[3][0],34);
            Assert.That(old.DueTick,Is.EqualTo(4));Assert.That(original.Scheduler.CancelStable(old),Is.True);
            TaskKey replacement=original.Scheduler.ScheduleFor(due,0,Actor.Id,1,
                new StableSimEvent(GameplayEvents.Regen,target:new EntityLink(Actor.Id)));
            var document=new M0c2S2ProtocolTests.Document(original);byte[] row=document.Tables[3][0];
            Put(row,34,(ulong)due);Put(row,58,replacement.Counter);
            var restored=World(regenPeriod:3);var failure=Assert.Throws<SimSnapshotException>(()=>restored.RestoreRetainedSnapshot(document.Encode(),true));
            StringAssert.Contains("M0C_REGEN_RESTORE",failure.Message);
        }
        [Test,Category("TaskBRegen")]
        public void RegenTaskAtMaximumRemainingPeriodRestoresAndContinues()
        {
            var original=World(regenPeriod:3);Step(original);
            for(int checkpoint=1;checkpoint<=7;checkpoint++)
            {
                var restored=World(regenPeriod:3);restored.RestoreRetainedSnapshot(original.SaveRetainedSnapshot(),true);
                Assert.That(restored.SaveRetainedSnapshot(),Is.EqualTo(original.SaveRetainedSnapshot()));
                Step(original);Step(restored);Assert.That(restored.StateHashRetained(),Is.EqualTo(original.StateHashRetained()));
            }
        }
    }
}
