using System;
using System.Reflection;
using NUnit.Framework;
using WarCore.Sim;
using Simulation=WarCore.Sim.Sim;
namespace WarCore.Tests
{
    [TestFixture,Category("M0c2S2"),Category("M0c2S2Boundary")]
    public sealed class M0c2S2BoundaryTests
    {
        private static readonly StableEntityKey A=StableEntityKey.Placed(81,1),B=StableEntityKey.Placed(81,2);
        private static StableId Low=>A.Id.CompareTo(B.Id)<0?A.Id:B.Id;
        private static StableId High=>A.Id.CompareTo(B.Id)>0?A.Id:B.Id;
        private static GameplayApplyKey Key(StableId source,ulong token,uint ordinal=0)=>new GameplayApplyKey(source,token,0,source,token,ordinal);
        private static Simulation Create(GameplayStatusDef definition,int statuses=4,int timers=16,IRetainedGameplaySystem script=null)
        {
            var schema=new GameplaySchema(new[]{new GameplayResourceDef(1,"resource",0,1000,500)},new GameplayDamageTypeDef[0],new GameplayPipelineDef[0],
                new[]{new GameplayArchetypeDef(1,new[]{1UL})},new GameplayCapacities(2,2,32,64,statuses),"s2.boundary",new[]{definition});
            var sim=Simulation.CreateRetainedWorld(42,new PlacedCatalog(new StableContentManifest(0),new PlacedCatalogEntry(81,1,1),new PlacedCatalogEntry(81,2,1)),
                new SimConfig(entityCapacity:4,timerCapacity:timers,eventRetainedTicks:1),gameplaySchema:schema);
            sim.BindGameplay(sim.Entities.Create(A),1);sim.BindGameplay(sim.Entities.Create(B),1);
            if(script!=null)sim.RegisterGameplay("s2.boundary-script",script,SimSystemPhase.Queries);
            return sim;
        }
        private static T Field<T>(GameplayState state,string name)=>(T)typeof(GameplayState).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(state);
        private static void SetField<T>(GameplayState state,string name,T value)=>typeof(GameplayState).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(state,value);
        [Test] public void WeakerStrongestRequestDoesNotEvaluateOrSaturateItsUnusedExpiry()
        {
            var sim=Create(new GameplayStatusDef(1,"strong",GameplayStatusDuration.Timed,10,stackPolicy:GameplayStackPolicy.Strongest));sim.Step(default);
            GameplayState state=sim.Entities.Gameplay;long limit=long.MaxValue-1;sim.Clock.Restore(limit-2);
            Assert.That(state.AdmitStatus(GameplayAction.ApplyStatus(sim.GameplayTarget(Low),Key(Low,1),1,10).Command,0),Is.EqualTo(GameplayFailure.None));state.Settle();
            long before=Field<long>(state,"timeSaturations");sim.Clock.Restore(limit-1);
            Assert.That(state.AdmitStatus(GameplayAction.ApplyStatus(sim.GameplayTarget(Low),Key(Low,2),1,1).Command,1),Is.EqualTo(GameplayFailure.None));state.Settle();
            Assert.That(Field<long>(state,"timeSaturations"),Is.EqualTo(before),"A losing strongest request has no expiry work.");
            Assert.That(sim.GameplayStatusAt(0).StrengthRaw,Is.EqualTo(10));
        }

        private abstract class Script : IRetainedGameplaySystem
        {
            public int Order=>1;public string StateTypeId=>"test.s2.boundary";public int StateVersion=>1;
            public abstract void Step(SimContext c);
            public void SaveState(SimStateWriter w){} public void LoadState(SimStateReader r){} public void WriteState(StateHash h){}
        }
        private sealed class GuardScript : Script
        {
            private readonly int kind;private readonly bool duplicate,submit;
            internal GuardScript(int kind,bool duplicate,bool submit){this.kind=kind;this.duplicate=duplicate;this.submit=submit;}
            public override void Step(SimContext c)
            {
                if(c.Clock.Tick!=1)return;
                Span<GameplayAction> actions=stackalloc GameplayAction[1];
                for(int n=0;n<2;n++)
                {
                    StableId source=n==0 && !duplicate?High:Low;var target=c.Owner.GameplayTarget(Low);var key=Key(source,1,(uint)n);
                    if(submit)
                    {
                        actions[0]=kind==0?GameplayAction.ApplyStatus(target,key,1):
                            kind==1?GameplayAction.Dispel(target,key,1):GameplayAction.GrantAbsorb(target,key,1,5);
                        c.SubmitGameplay(c.Owner.GameplayTarget(source),ReadOnlySpan<GameplayCost>.Empty,actions,0);
                    }
                    else if(kind==0)c.ApplyStatus(target,key,1,commandOrdinal:0);
                    else if(kind==1)c.Dispel(target,key,1,commandOrdinal:0);
                    else c.GrantAbsorb(target,key,1,5,commandOrdinal:0);
                }
            }
        }
        [TestCase(0,false,false),TestCase(1,false,false),TestCase(2,false,false),TestCase(0,true,false),TestCase(1,true,false),TestCase(2,true,false),
         TestCase(0,false,true),TestCase(1,false,true),TestCase(2,false,true),TestCase(0,true,true),TestCase(1,true,true),TestCase(2,true,true)]
        public void EveryStatusAdmissionPathGuardsSourceAndCommandOrdinal(int kind,bool duplicate,bool submit)
        {
            var sim=Create(new GameplayStatusDef(1,"guard",GameplayStatusDuration.Infinite,absorbResourceKey:1),script:new GuardScript(kind,duplicate,submit));
            Assert.That(Assert.Throws<SimBoundaryException>(()=>sim.Step(default)).Message,Does.Contain("ADMISSION_ORDER"));
            Assert.That(sim.GameplayStatusCount,Is.Zero);Assert.That(sim.GameplayResource(Low,1),Is.EqualTo(500));
        }
        private sealed class AtomicScript : Script
        {
            internal GameplayFailure Result;private readonly int count;
            internal AtomicScript(int count){this.count=count;}
            public override void Step(SimContext c)
            {
                if(c.Clock.Tick!=1)return;
                Span<GameplayAction> actions=stackalloc GameplayAction[2];
                for(int i=0;i<count;i++)actions[i]=GameplayAction.ApplyStatus(c.Owner.GameplayTarget(Low),Key(Low,1,(uint)i),1);
                Span<GameplayCost> costs=stackalloc GameplayCost[1];costs[0]=new GameplayCost(1,7);
                Result=c.SubmitGameplay(c.Owner.GameplayTarget(Low),costs,actions.Slice(0,count));
            }
        }
        [TestCase(false),TestCase(true)] public void FullStatusOrTimerCapacityRejectsAllFeesTokensRowsAndTasks(bool timer)
        {
            var def=timer?new GameplayStatusDef(1,"timer",GameplayStatusDuration.Timed,5,2,1,
                periodicActions:new[]{GameplayPeriodicAction.ChangeResource(0,1,1)}):
                new GameplayStatusDef(1,"independent",GameplayStatusDuration.Infinite,stackPolicy:GameplayStackPolicy.Independent,maxInstances:2);
            var script=new AtomicScript(timer?1:2);var sim=Create(def,statuses:1,timers:timer?1:16,script:script);sim.Step(default);
            Assert.That(script.Result,Is.EqualTo(GameplayFailure.StatusCapacity));Assert.That(sim.GameplayResource(Low,1),Is.EqualTo(500));
            Assert.That(sim.GameplayStatusCount,Is.Zero);Assert.That(sim.Scheduler.PendingCount,Is.Zero);
            Assert.That(Field<ulong>(sim.Entities.Gameplay,"effectNext"),Is.Zero);
        }
        private sealed class LedgerScript : Script
        {
            internal GameplayFailure First,Rejected,Refresh;
            public override void Step(SimContext c)
            {
                if(c.Clock.Tick>3)return;
                StableId target=c.Clock.Tick==2?High:Low;
                Span<GameplayAction> actions=stackalloc GameplayAction[1];actions[0]=GameplayAction.ApplyStatus(c.Owner.GameplayTarget(target),Key(Low,(ulong)c.Clock.Tick),1);
                Span<GameplayCost> cost=stackalloc GameplayCost[1];cost[0]=new GameplayCost(1,7);
                GameplayFailure f=c.SubmitGameplay(c.Owner.GameplayTarget(Low),cost,actions);
                if(c.Clock.Tick==1)First=f;else if(c.Clock.Tick==2)Rejected=f;else Refresh=f;
            }
        }
        [Test] public void FuturePulseReservationSurvivesItsAbsentTaskAndBlocksCompetingAdmission()
        {
            var script=new LedgerScript();var sim=Create(new GameplayStatusDef(1,"ledger",GameplayStatusDuration.Timed,3,4,4,
                periodicActions:new[]{GameplayPeriodicAction.ChangeResource(0,1,1)}),statuses:4,timers:2,script:script);
            sim.Step(default);Assert.That(script.First,Is.EqualTo(GameplayFailure.None));Assert.That(sim.Scheduler.PendingCount,Is.EqualTo(1));
            sim.Step(default);Assert.That(script.Rejected,Is.EqualTo(GameplayFailure.StatusCapacity));Assert.That(sim.GameplayResource(Low,1),Is.EqualTo(493));
            sim.Step(default);Assert.That(script.Refresh,Is.EqualTo(GameplayFailure.None));Assert.That(sim.GameplayResource(Low,1),Is.EqualTo(486));
            Assert.That(sim.GameplayStatusCount,Is.EqualTo(1));Assert.That(sim.Scheduler.PendingCount,Is.EqualTo(2));
            Assert.That(sim.GameplayStatusAt(0).NextPulseTick,Is.EqualTo(5));
            var resumed=Create(new GameplayStatusDef(1,"ledger",GameplayStatusDuration.Timed,3,4,4,
                periodicActions:new[]{GameplayPeriodicAction.ChangeResource(0,1,1)}),statuses:4,timers:2,script:new LedgerScript());
            resumed.RestoreRetainedSnapshot(sim.SaveRetainedSnapshot(),true);Assert.That(resumed.StateHashRetained(),Is.EqualTo(sim.StateHashRetained()));
        }
        [Test] public void EffectTokenExhaustionRejectsBeforeAnyFeeIsPaid()
        {
            var script=new AtomicScript(1);var sim=Create(new GameplayStatusDef(1,"token",GameplayStatusDuration.Infinite),script:script);
            SetField(sim.Entities.Gameplay,"effectNext",ulong.MaxValue);
            Assert.That(Assert.Throws<SimBoundaryException>(()=>sim.Step(default)).Message,Does.Contain("TOKEN_EXHAUSTED"));
            Assert.That(sim.GameplayResource(Low,1),Is.EqualTo(500));Assert.That(sim.GameplayStatusCount,Is.Zero);
        }
        // Counter fault injection only: no claim that this artificially old pulse history is a valid snapshot.
        // All behavior under test after the injection goes through the public Step entry point.
        internal static uint ProbePulseOrdinal(uint ordinal)
        {
            var script=new AtomicScript(1);var sim=Create(new GameplayStatusDef(1,"pulse",GameplayStatusDuration.Infinite,periodTicks:1,firstPulseDelayTicks:1,
                periodicActions:new[]{GameplayPeriodicAction.ChangeResource(0,1,1)}),script:script);sim.Step(default);
            var rows=Field<GameplayStatusRow[]>(sim.Entities.Gameplay,"statuses");rows[0].PulseOrdinal=ordinal;
            sim.Step(default);return sim.GameplayStatusAt(0).PulseOrdinal;
        }
        [Test] public void PulseOrdinalExhaustionIsExplicitBeforePeriodicResourceChanges()
        {
            var script=new AtomicScript(1);var sim=Create(new GameplayStatusDef(1,"pulse",GameplayStatusDuration.Infinite,periodTicks:1,firstPulseDelayTicks:1,
                periodicActions:new[]{GameplayPeriodicAction.ChangeResource(0,1,1)}),script:script);sim.Step(default);
            var rows=Field<GameplayStatusRow[]>(sim.Entities.Gameplay,"statuses");rows[0].PulseOrdinal=uint.MaxValue;
            Assert.That(Assert.Throws<SimBoundaryException>(()=>sim.Step(default)).Message,Does.Contain("PULSE_EXHAUSTED"));
            Assert.That(sim.GameplayResource(Low,1),Is.EqualTo(493));
        }
        [Test] public void StatusSchedulingAtTheTimeLimitHasAnExplicitBoundary()
        {
            var sim=Create(new GameplayStatusDef(1,"time",GameplayStatusDuration.Timed,1));sim.Step(default);sim.Clock.Restore(long.MaxValue-1);
            GameplayState state=sim.Entities.Gameplay;
            Assert.That(state.AdmitStatus(GameplayAction.ApplyStatus(sim.GameplayTarget(Low),Key(Low,1),1).Command,0),Is.EqualTo(GameplayFailure.None));
            Assert.That(Assert.Throws<SimBoundaryException>(()=>state.Settle()).Message,Does.Contain("TIME_EXHAUSTED"));
            Assert.That(sim.GameplayStatusCount,Is.Zero);Assert.That(sim.Scheduler.PendingCount,Is.Zero);
        }
        [TestCase(-1),TestCase(3)] public void UnsupportedDurationEnumIsABoundaryFailure(int value)
        {Assert.Throws<SimBoundaryException>(()=>Create(new GameplayStatusDef(1,"enum",(GameplayStatusDuration)value)));}

        private sealed class PressureScript : Script
        {
            private readonly StableId[] sources;internal int Successes,Rejected;
            internal PressureScript(StableId[] sources){this.sources=sources;}
            public override void Step(SimContext c)
            {
                Span<GameplayAction> action=stackalloc GameplayAction[1];
                for(int i=0;i<512;i++)
                {
                    StableId id=sources[i];var target=c.Owner.GameplayTarget(id);
                    action[0]=GameplayAction.ApplyStatus(target,Key(id,(ulong)c.Clock.Tick),1);
                    GameplayFailure f=c.SubmitGameplay(target,ReadOnlySpan<GameplayCost>.Empty,action);
                    if(f==GameplayFailure.None)Successes++;else Rejected++;
                }
            }
        }
        [Test,Category("M0c2S2Pressure")]
        public void TwoThousandOwnersSustainFiveHundredTwelvePeriodicUpdatesForThreeHundredTicks()
        {
            const int owners=2000;var entries=new PlacedCatalogEntry[owners];var ids=new StableId[owners];
            for(int i=0;i<owners;i++){entries[i]=new PlacedCatalogEntry(82,(ulong)i+1,1);ids[i]=StableEntityKey.Placed(82,(ulong)i+1).Id;}
            var schema=new GameplaySchema(new[]{new GameplayResourceDef(1,"resource",0,1000000,1000)},new GameplayDamageTypeDef[0],new GameplayPipelineDef[0],
                new[]{new GameplayArchetypeDef(1,new[]{1UL})},new GameplayCapacities(owners,owners,2048,4096,1024),"s2.pressure",
                new[]{new GameplayStatusDef(1,"periodic",GameplayStatusDuration.Infinite,periodTicks:1,firstPulseDelayTicks:1,
                    periodicActions:new[]{GameplayPeriodicAction.ChangeResource(0,1,1)})});
            var sim=Simulation.CreateRetainedWorld(42,new PlacedCatalog(new StableContentManifest(0),entries),
                new SimConfig(entityCapacity:2048,timerCapacity:2048,eventRetainedTicks:1,eventInitialCapacity:4096,eventHardLimit:4096),gameplaySchema:schema);
            for(int i=owners-1;i>=0;i--)sim.BindGameplay(sim.Entities.Create(StableEntityKey.Placed(82,(ulong)i+1)),1);
            Array.Sort(ids);var script=new PressureScript(ids);sim.RegisterGameplay("s2.pressure",script,SimSystemPhase.Queries);
            sim.Step(default);
            for(int tick=0;tick<300;tick++)
            {
                sim.Step(default);Assert.That(sim.Scheduler.DueCount,Is.Zero);Assert.That(sim.Events.EventsDroppedThisTick,Is.Zero);
                Assert.That(sim.Scheduler.PendingCount,Is.EqualTo(512));Assert.That(sim.GameplayStatusCount,Is.EqualTo(512));sim.StateHashRetained();
            }
            Assert.That(script.Rejected,Is.Zero);Assert.That(script.Successes,Is.EqualTo(301*512));
            Assert.That(Field<long>(sim.Entities.Gameplay,"statusCapacityRejections"),Is.Zero);
            for(int i=0;i<512;i++)Assert.That(sim.GameplayResource(ids[i],1),Is.EqualTo(1300));
            TestContext.WriteLine("S2 pressure: owners=2000; steadyTicks=300; statusAdmissions=153600; periodicUpdates=153600; statusRows=512; pendingTasks=512; rejects=0; dueBacklog=0; eventsDropped=0; perTickFullHash=true; no G24 timing claim");
        }

        private sealed class ZeroResourceScript : Script
        {
            public override void Step(SimContext c)
            {
                if(c.Clock.Tick!=1)return;var target=c.Owner.GameplayTarget(Low);
                c.GrantAbsorb(target,Key(Low,1,0),1,7);
                c.Damage(target,Key(Low,1,1),1,10);
            }
        }
        private static Simulation ZeroResourceWorld(GameplayStatusDef definition,IRetainedGameplaySystem script)
        {
            var schema=new GameplaySchema(new[]{new GameplayResourceDef(0,"zero-key-resource",0,1000,1000)},
                new[]{new GameplayDamageTypeDef(1,"direct",0,1)},new[]{new GameplayPipelineDef(1,new GameplayDamageSpec(0,GameplayDamageStages.None))},
                new[]{new GameplayArchetypeDef(1,new[]{0UL})},new GameplayCapacities(2,2,16,32,4),"s2.zero-resource",new[]{definition});
            var sim=Simulation.CreateRetainedWorld(42,new PlacedCatalog(new StableContentManifest(0),new PlacedCatalogEntry(81,1,1),new PlacedCatalogEntry(81,2,1)),
                new SimConfig(entityCapacity:4,timerCapacity:16,eventRetainedTicks:1),gameplaySchema:schema);
            sim.BindGameplay(sim.Entities.Create(A),1);sim.BindGameplay(sim.Entities.Create(B),1);
            if(script!=null)sim.RegisterGameplay("s2.zero-resource",script,SimSystemPhase.Queries);
            return sim;
        }
        [Test,Category("M0c2S2KeyZero")] public void ExplicitResourceKeyZeroCanOwnAnAbsorbPool()
        {
            var sim=ZeroResourceWorld(new GameplayStatusDef(1,"zero-key-pool",GameplayStatusDuration.Infinite,absorbResourceKey:0),new ZeroResourceScript());
            Assert.DoesNotThrow(()=>sim.Step(default),"Resource key zero is a valid frozen content key, not an absent shield.");
            Assert.That(sim.GameplayResource(Low,0),Is.EqualTo(997));Assert.That(sim.GameplayStatusAt(0).AbsorbRemainingRaw,Is.Zero);
        }

        [Test,Category("M0c2S2KeyZero")] public void OmittedAbsorbReferenceAndExplicitZeroHaveDifferentRulesAndRestoreContracts()
        {
            var explicitZero=new GameplayStatusDef(1,"zero-key-pool",GameplayStatusDuration.Infinite,absorbResourceKey:0);
            var absent=new GameplayStatusDef(1,"zero-key-pool",GameplayStatusDuration.Infinite);
            Assert.That(explicitZero.HasAbsorbResource,Is.True);Assert.That(explicitZero.AbsorbResourceKey,Is.Zero);
            Assert.That(absent.HasAbsorbResource,Is.False);Assert.That(absent.AbsorbResourceKey,Is.Zero);
            var active=ZeroResourceWorld(explicitZero,new ZeroResourceScript());var noPool=ZeroResourceWorld(absent,new ZeroResourceScript());
            Assert.That(active.StateHashRetained(),Is.Not.EqualTo(noPool.StateHashRetained()));active.Step(default);
            var restored=ZeroResourceWorld(explicitZero,new ZeroResourceScript());restored.RestoreRetainedSnapshot(active.SaveRetainedSnapshot(),true);
            Assert.That(restored.StateHashRetained(),Is.EqualTo(active.StateHashRetained()));
            Assert.Throws<SimSnapshotException>(()=>noPool.RestoreRetainedSnapshot(active.SaveRetainedSnapshot(),true));
            var unsupported=ZeroResourceWorld(absent,new ZeroResourceScript());
            Assert.That(Assert.Throws<SimBoundaryException>(()=>unsupported.Step(default)).Message,Does.Contain("ABSORB_DEFINITION"));
        }
        [TestCase(0UL),TestCase(2UL),Category("M0c2S2KeyZero")]
        public void AnExplicitAbsorbResourceMustExistEvenWhenItsKeyIsZero(ulong key)
        {Assert.Throws<SimBoundaryException>(()=>Create(new GameplayStatusDef(1,"unknown-resource",GameplayStatusDuration.Infinite,absorbResourceKey:key)));}
        private sealed class TagScript : Script
        {
            internal ulong BeforeAdmission,AfterAdmission;
            public override void Step(SimContext c)
            {
                BeforeAdmission=c.Owner.GameplayStatusTags(Low);
                if(c.Clock.Tick==1)c.ApplyStatus(c.Owner.GameplayTarget(Low),Key(Low,1),1);
                if(c.Clock.Tick==2)c.Dispel(c.Owner.GameplayTarget(Low),Key(Low,2),1);
                AfterAdmission=c.Owner.GameplayStatusTags(Low);
            }
        }
        [Test,Category("M0c2S2KeyZero")] public void StatusTagsAreReadOnlyFactsThatChangeAtQ1()
        {
            var script=new TagScript();var sim=Create(new GameplayStatusDef(1,"slow-control-data",GameplayStatusDuration.Infinite,
                strengthRaw:GameplayMath.Scale/2,tags:4,dispelTags:1),script:script);
            Assert.That(sim.GameplayStatusTags(Low),Is.Zero);sim.Step(default);
            Assert.That(script.BeforeAdmission,Is.Zero);Assert.That(script.AfterAdmission,Is.Zero);
            Assert.That(sim.GameplayStatusTags(Low),Is.EqualTo(4));Assert.That(sim.GameplayStatusTags(High),Is.Zero);
            Assert.That(sim.GameplayStatusAt(0).StrengthRaw,Is.EqualTo(GameplayMath.Scale/2));
            ulong hash=sim.StateHashRetained();sim.GameplayStatusTags(Low);Assert.That(sim.StateHashRetained(),Is.EqualTo(hash));
            sim.Step(default);Assert.That(script.BeforeAdmission,Is.EqualTo(4));Assert.That(script.AfterAdmission,Is.EqualTo(4));
            Assert.That(sim.GameplayStatusTags(Low),Is.Zero);
        }

        [Test,Category("M0c2S2KeyZero")] public void StatusTagUnionKeepsTheOtherControlAfterDispel()
        {
            var definitions=new[]{new GameplayStatusDef(1,"control-a",GameplayStatusDuration.Infinite,tags:1,dispelTags:1),
                new GameplayStatusDef(2,"control-b",GameplayStatusDuration.Infinite,tags:4,dispelTags:2)};
            var plan=new[]{new S2Tick(new[]{S2Op.Apply(1,false,1),S2Op.Apply(2,false,2)}),new S2Tick(new[]{S2Op.Dispel(1)})};
            var sim=M0c2S2Fixture.CreateWith(out _,plan,statuses:definitions);M0c2S2Fixture.Step(sim);
            Assert.That(sim.GameplayStatusTags(M0c2S2Fixture.Target.Id),Is.EqualTo(5));M0c2S2Fixture.Step(sim);
            Assert.That(sim.GameplayStatusTags(M0c2S2Fixture.Target.Id),Is.EqualTo(4));
        }
    }
}
