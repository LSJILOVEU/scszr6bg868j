using NUnit.Framework;
using WarCore.Tests.Oracle;
using WarCore.Sim;
namespace WarCore.Tests
{
    [TestFixture,Category("M0c2S2"),Category("M0c2S2Behavior")]
    public sealed class M0c2S2BehaviorTests
    {
        [Test] public void RefreshUsesLatestExpiryAndThisTicksMinimumFullAttribution()
        {
            var definition=new GameplayStatusDef(1,"refresh",GameplayStatusDuration.Timed,5,2,1,periodicActions:new[]{GameplayPeriodicAction.Damage(1,1,1)});
            var sim=M0c2S2Fixture.CreateWith(out _,new[]{new S2Tick(new[]{S2Op.Apply(1,true)}),
                new S2Tick(new[]{S2Op.Apply(1,true,2),S2Op.Apply(1,false,3)}),new S2Tick(new[]{S2Op.Apply(1,true)})},statuses:new[]{definition});
            M0c2S2Fixture.Step(sim);M0c2S2Fixture.Step(sim);
            Assert.That(sim.GameplayStatusCount,Is.EqualTo(1));
            var row=sim.GameplayStatusAt(0);
            var expected=StatusOracle.Reduce(StatusOracle.Policy.Refresh,new StatusOracle.Row[0],
                new[]{new StatusOracle.Application(OracleKey(M0c2S2Fixture.High,1,1),1,5,0)},1,1,1,1);
            expected=StatusOracle.Reduce(StatusOracle.Policy.Refresh,expected,new[]{
                new StatusOracle.Application(OracleKey(M0c2S2Fixture.High,2,2),2,5,0),
                new StatusOracle.Application(OracleKey(M0c2S2Fixture.Low,2,3),2,5,0)},2,1,1,1);
            Assert.That(row.ExpiryTick,Is.EqualTo(expected[0].Expiry));
            Assert.That(row.Attribution.Source.Value,Is.EqualTo(expected[0].Attribution.Source));
            Assert.That(row.Stacks,Is.EqualTo(1));Assert.That(row.ExpiryTick,Is.EqualTo(7));
            Assert.That(row.Attribution.Source,Is.EqualTo(M0c2S2Fixture.Low));
            Assert.That(row.NextPulseTick,Is.EqualTo(4));Assert.That(row.StartTick,Is.EqualTo(1));
            M0c2S2Fixture.Step(sim);Assert.That(sim.GameplayStatusAt(0).Attribution.Source,Is.EqualTo(M0c2S2Fixture.High));
            Assert.That(sim.GameplayStatusAt(0).ExpiryTick,Is.EqualTo(8));
        }
        [Test] public void StackSaturatesAndUsesCurrentTickSmallestAttributionWithIndependentOracle()
        {
            var definition=new GameplayStatusDef(1,"stack",GameplayStatusDuration.Timed,5,3,3,
                GameplayStackPolicy.Stack,maxStacks:3,periodicActions:new[]{GameplayPeriodicAction.Damage(1,1,1)});
            var sim=M0c2S2Fixture.CreateWith(out _,new[]{new S2Tick(new[]{S2Op.Apply(1,true)}),
                new S2Tick(new[]{S2Op.Apply(1,true,2),S2Op.Apply(1,false,4),S2Op.Apply(1,false,3)}),new S2Tick(new[]{S2Op.Apply(1,true)})},statuses:new[]{definition});
            M0c2S2Fixture.Step(sim);Assert.That(sim.GameplayStatusCount,Is.EqualTo(1));var first=sim.GameplayStatusAt(0);M0c2S2Fixture.Step(sim);var actual=sim.GameplayStatusAt(0);
            var a=new StatusOracle.Application(OracleKey(M0c2S2Fixture.High,1,1),1,5,0);
            var expected=StatusOracle.Reduce(StatusOracle.Policy.Stack,new StatusOracle.Row[0],new[]{a},1,3,1,3);
            expected=StatusOracle.Reduce(StatusOracle.Policy.Stack,expected,new[]{
                new StatusOracle.Application(OracleKey(M0c2S2Fixture.High,2,2),2,5,0),
                new StatusOracle.Application(OracleKey(M0c2S2Fixture.Low,2,4),2,5,0),
                new StatusOracle.Application(OracleKey(M0c2S2Fixture.Low,2,3),2,5,0)},2,3,1,3);
            Assert.That(actual.Stacks,Is.EqualTo(expected[0].Stacks));Assert.That(actual.Stacks,Is.EqualTo(3));
            Assert.That(actual.ExpiryTick,Is.EqualTo(expected[0].Expiry));Assert.That(actual.NextPulseTick,Is.EqualTo(4));
            Assert.That(actual.Attribution.Source.Value,Is.EqualTo(expected[0].Attribution.Source));
            Assert.That(actual.Attribution.ActionOrdinal,Is.EqualTo(expected[0].Attribution.Action));
            Assert.That(actual.Key.Token,Is.EqualTo(first.Key.Token));
            M0c2S2Fixture.Step(sim);Assert.That(sim.GameplayStatusAt(0).Attribution.Source,Is.EqualTo(M0c2S2Fixture.High));
            Assert.That(sim.GameplayStatusAt(0).Stacks,Is.EqualTo(3));
        }
        [Test] public void IndependentInstancesKeepSeparateSourcesAndExpiryAndRejectAtTheLimit()
        {
            var d=new GameplayStatusDef(1,"independent",GameplayStatusDuration.Timed,4,
                stackPolicy:GameplayStackPolicy.Independent,maxInstances:2);
            var sim=M0c2S2Fixture.CreateWith(out S2Script script,new[]{new S2Tick(new[]{S2Op.Apply(1,true)}),
                new S2Tick(new[]{S2Op.Apply(1,false)}),new S2Tick(new[]{S2Op.Apply(1,false)})},statuses:new[]{d});
            M0c2S2Fixture.Step(sim);Assert.That(sim.GameplayStatusCount,Is.EqualTo(1));
            M0c2S2Fixture.Step(sim);Assert.That(sim.GameplayStatusCount,Is.EqualTo(2));
            var first=sim.GameplayStatusAt(0);var second=sim.GameplayStatusAt(1);
            var expected=StatusOracle.Reduce(StatusOracle.Policy.Independent,new StatusOracle.Row[0],
                new[]{new StatusOracle.Application(OracleKey(M0c2S2Fixture.High,1,1),1,4,0)},1,1,2,0);
            expected=StatusOracle.Reduce(StatusOracle.Policy.Independent,expected,
                new[]{new StatusOracle.Application(OracleKey(M0c2S2Fixture.Low,2,1),2,4,0)},2,1,2,0);
            Assert.That(first.ExpiryTick,Is.EqualTo(expected[0].Expiry));Assert.That(second.ExpiryTick,Is.EqualTo(expected[1].Expiry));
            Assert.That(first.Attribution.Source.Value,Is.EqualTo(expected[0].Attribution.Source));
            Assert.That(second.Attribution.Source.Value,Is.EqualTo(expected[1].Attribution.Source));
            M0c2S2Fixture.Step(sim);Assert.That(script.LastFailure,Is.EqualTo(GameplayFailure.StatusCapacity));
            Assert.That(sim.GameplayStatusCount,Is.EqualTo(2));Assert.That(sim.GameplayStatusAt(1).Key.Token,Is.EqualTo(second.Key.Token));
        }
        [Test] public void StrongestKeepsStrengthAttributionAndSnapshotTogetherAndUsesTheFullTieKey()
        {
            var d=new GameplayStatusDef(1,"strongest",GameplayStatusDuration.Timed,5,3,3,
                GameplayStackPolicy.Strongest,periodicActions:new[]{GameplayPeriodicAction.Damage(1,1,1)});
            var plan=new[]{new S2Tick(new[]{S2Op.Apply(1,true,9,4),S2Op.Apply(1,false,7,3),S2Op.Apply(1,true,1,4)}),
                new S2Tick(new[]{S2Op.Apply(1,false,1,2)}),new S2Tick(new[]{S2Op.Apply(1,false,3,4)}),
                new S2Tick(new[]{S2Op.Apply(1,false,2,4)}),new S2Tick(new[]{S2Op.Apply(1,false,1,5)})};
            var sim=M0c2S2Fixture.CreateWith(out _,plan,statuses:new[]{d});var expected=new StatusOracle.Row[0];ulong token=0;
            for(int t=0;t<plan.Length;t++)
            {
                var operations=plan[t].Operations;var applications=new StatusOracle.Application[operations.Length];
                for(int i=0;i<operations.Length;i++)
                {var op=operations[i];applications[i]=new StatusOracle.Application(OracleKey(op.High?M0c2S2Fixture.High:M0c2S2Fixture.Low,(ulong)t+1,op.Ordinal),t+1,5,op.Amount);}
                expected=StatusOracle.Reduce(StatusOracle.Policy.Strongest,expected,applications,t+1,1,1,3);
                M0c2S2Fixture.Step(sim);Assert.That(sim.GameplayStatusCount,Is.EqualTo(1));var row=sim.GameplayStatusAt(0);
                if(t==0)token=row.Key.Token;Assert.That(row.Key.Token,Is.EqualTo(token));
                Assert.That(row.Stacks,Is.EqualTo(1));Assert.That(row.StrengthRaw,Is.EqualTo(expected[0].Strength));
                Assert.That(row.Attribution.Source.Value,Is.EqualTo(expected[0].Attribution.Source));
                Assert.That(row.Attribution.ActionOrdinal,Is.EqualTo(expected[0].Attribution.Action));
                Assert.That(row.Attribution.CastToken,Is.EqualTo(expected[0].Attribution.Cast));
                Assert.That(row.SourceSnapshot.Source.Id.Value,Is.EqualTo(expected[0].Attribution.Source));
                Assert.That(row.SourceSnapshot.CapturedTick,Is.EqualTo(expected[0].Attribution.Cast));
                Assert.That(row.ExpiryTick,Is.EqualTo(expected[0].Expiry));
                if(t==1)Assert.That(row.NextPulseTick,Is.EqualTo(4),"weaker application must not reset the plan");
                if(t==2)Assert.That(row.NextPulseTick,Is.EqualTo(6),"winning replacement rebuilds the plan");
            }
        }
        [TestCase(0,1,1),TestCase(1,1,1),TestCase(2,1,1),TestCase(3,1,1),
         TestCase(3,2,2),TestCase(4,2,2),TestCase(5,2,2),TestCase(6,2,2),
         TestCase(2,3,1),TestCase(3,3,1),TestCase(4,3,1)]
        public void PeriodicRequestsMatchIndependentWideOracleAtEveryExpiryNeighbour(int duration,int delay,int period)
        {
            var d=new GameplayStatusDef(1,"periodic",GameplayStatusDuration.Timed,duration,period,delay,
                periodicActions:new[]{GameplayPeriodicAction.Damage(1,1,1)});
            var sim=M0c2S2Fixture.CreateWith(out _,new[]{new S2Tick(new[]{S2Op.Apply(1)})},statuses:new[]{d});
            long[] pulses=StatusOracle.Pulses(1,duration,delay,period);
            for(int tick=1;tick<=duration+3;tick++)
            {
                M0c2S2Fixture.Step(sim);int count=0;foreach(long due in pulses)if(due<=tick)count++;
                Assert.That(sim.GameplayResource(M0c2S2Fixture.Target.Id,1),Is.EqualTo(8000*M0c2S2Fixture.S-count),"tick "+tick);
                Assert.That(sim.GameplayStatusCount,Is.EqualTo(tick<1+duration?1:0),"active interval tick "+tick);
                bool futurePulse=false;foreach(long due in pulses)if(due>tick)futurePulse=true;
                Assert.That(sim.Scheduler.PendingCount,Is.EqualTo(tick<1+duration?(futurePulse?2:1):0),"no pulse task may be scheduled at or after expiry");
            }
        }
        [Test] public void ImmunityFreezesAfterDispelAndBeforeAllSameTickApplications()
        {
            var defs=new[]{new GameplayStatusDef(1,"immune",GameplayStatusDuration.Timed,10,immunityTags:4,dispelTags:8),
                new GameplayStatusDef(2,"control",GameplayStatusDuration.Timed,10,stackPolicy:GameplayStackPolicy.Independent,maxInstances:4,tags:4)};
            var sim=M0c2S2Fixture.CreateWith(out _,new[]{new S2Tick(new[]{S2Op.Apply(1),S2Op.Apply(2,true)}),
                new S2Tick(new[]{S2Op.Apply(2)}),new S2Tick(new[]{S2Op.Dispel(8),S2Op.Apply(2,false,2)})},statuses:defs);
            M0c2S2Fixture.Step(sim);Assert.That(sim.GameplayStatusCount,Is.EqualTo(2),"new immunity does not reject a parallel application");
            M0c2S2Fixture.Step(sim);Assert.That(sim.GameplayStatusCount,Is.EqualTo(2),"previous immunity rejects the next tick application");
            M0c2S2Fixture.Step(sim);Assert.That(sim.GameplayStatusCount,Is.EqualTo(2),"dispel runs before immunity is frozen");
            for(int i=0;i<sim.GameplayStatusCount;i++)Assert.That(sim.GameplayStatusAt(i).Key.Definition,Is.EqualTo(2));
            Assert.That(sim.GameplayStatusAt(1).Attribution.Source,Is.EqualTo(M0c2S2Fixture.Low));
        }
        [Test] public void DispelChoosesPriorityThenCompleteInstanceKeyAndOnlyTheRequestedCount()
        {
            var defs=new[]{new GameplayStatusDef(1,"late",GameplayStatusDuration.Timed,10,dispelTags:1,priority:9),
                new GameplayStatusDef(2,"first",GameplayStatusDuration.Timed,10,dispelTags:1,priority:1),
                new GameplayStatusDef(3,"second",GameplayStatusDuration.Timed,10,dispelTags:1,priority:1)};
            var sim=M0c2S2Fixture.CreateWith(out _,new[]{new S2Tick(new[]{S2Op.Apply(3,false,1),S2Op.Apply(1,false,2),S2Op.Apply(2,false,3)}),
                new S2Tick(new[]{S2Op.Dispel(1)}),new S2Tick(new[]{S2Op.Dispel(1)})},statuses:defs);
            M0c2S2Fixture.Step(sim);Assert.That(sim.GameplayStatusCount,Is.EqualTo(3));
            M0c2S2Fixture.Step(sim);Assert.That(sim.GameplayStatusCount,Is.EqualTo(2));
            Assert.That(sim.GameplayStatusAt(0).Key.Definition,Is.EqualTo(1));Assert.That(sim.GameplayStatusAt(1).Key.Definition,Is.EqualTo(3));
            M0c2S2Fixture.Step(sim);Assert.That(sim.GameplayStatusCount,Is.EqualTo(1));Assert.That(sim.GameplayStatusAt(0).Key.Definition,Is.EqualTo(1));
        }
        [TestCase(false),TestCase(true)] public void SameTickDefenseModifiersAreFrozenAfterQ1BeforeDamage(bool reverse)
        {
            var d=new GameplayStatusDef(1,"defense",GameplayStatusDuration.Timed,4,
                modifiers:new[]{new GameplayStatusModifierDef(1,1,GameplayModifierStage.PreFlat,4*M0c2S2Fixture.S),
                    new GameplayStatusModifierDef(2,1,GameplayModifierStage.Product,M0c2S2Fixture.S/2)});
            var sim=M0c2S2Fixture.CreateWith(out _,new[]{new S2Tick(new[]{S2Op.Apply(1),S2Op.Damage(false,10*M0c2S2Fixture.S,2)})},
                reverse:reverse,statuses:new[]{d});M0c2S2Fixture.Step(sim);
            Assert.That(sim.GameplayResource(M0c2S2Fixture.Target.Id,1),Is.EqualTo(7993*M0c2S2Fixture.S));
            Assert.That(sim.ReadStableEvent(1,0).Payload.Magnitude.Raw,Is.EqualTo(7*M0c2S2Fixture.S));
        }
        [Test] public void AbsorbConsumesPriorityThenCompleteInstanceKeyAfterNumericStages()
        {
            var defs=new[]{new GameplayStatusDef(1,"middle",GameplayStatusDuration.Timed,4,priority:5,absorbResourceKey:1),
                new GameplayStatusDef(2,"first",GameplayStatusDuration.Timed,4,priority:1,absorbResourceKey:1),
                new GameplayStatusDef(3,"last",GameplayStatusDuration.Timed,4,priority:5,absorbResourceKey:1),
                new GameplayStatusDef(4,"factor",GameplayStatusDuration.Timed,4,
                    modifiers:new[]{new GameplayStatusModifierDef(1,1,GameplayModifierStage.Product,M0c2S2Fixture.S/2)})};
            var sim=M0c2S2Fixture.CreateWith(out _,new[]{new S2Tick(new[]{S2Op.Absorb(3,false,7,1),S2Op.Absorb(1,false,3,2),
                S2Op.Absorb(2,false,2,3),S2Op.Apply(4,false,4),S2Op.Damage(false,12,5)})},statuses:defs);
            M0c2S2Fixture.Step(sim);Assert.That(sim.GameplayStatusCount,Is.EqualTo(4));
            Assert.That(sim.GameplayStatusAt(0).AbsorbRemainingRaw,Is.Zero);Assert.That(sim.GameplayStatusAt(1).AbsorbRemainingRaw,Is.Zero);
            Assert.That(sim.GameplayStatusAt(2).AbsorbRemainingRaw,Is.EqualTo(6));
            Assert.That(sim.GameplayResource(M0c2S2Fixture.Target.Id,1),Is.EqualTo(8000*M0c2S2Fixture.S));
        }
        [Test] public void StatusModifierConstructionIncludesStatusTokenForOtherwiseEqualModifierKeys()
        {
            var d=new GameplayStatusDef(1,"independent-factors",GameplayStatusDuration.Timed,4,
                stackPolicy:GameplayStackPolicy.Independent,maxInstances:2,
                modifiers:new[]{new GameplayStatusModifierDef(1,1,GameplayModifierStage.Product,M0c2S2Fixture.S/2)});
            var sim=M0c2S2Fixture.CreateWith(out _,new[]{new S2Tick(new[]{S2Op.Apply(1,producerToken:20),
                S2Op.Apply(1,producerToken:10),S2Op.Damage(false,11,2)})},statuses:new[]{d});
            Assert.DoesNotThrow(()=>M0c2S2Fixture.Step(sim),"each independent instance requires its complete modifier key including StatusToken");
            Assert.That(sim.GameplayStatusCount,Is.EqualTo(2));
            Assert.That(sim.GameplayStatusAt(0).Key.Token,Is.Not.EqualTo(sim.GameplayStatusAt(1).Key.Token));
            Assert.That(sim.GameplayResource(M0c2S2Fixture.Target.Id,1),Is.EqualTo(8000*M0c2S2Fixture.S-2));
        }
        [TestCase(false),TestCase(true)] public void SameTickMixedActionsHaveIdenticalRowsAndHashesAcrossAllStorageOrders(bool packageB)
        {
            var a=M0c2S2Fixture.Create(packageB);var b=M0c2S2Fixture.Create(packageB,true,1);var c=M0c2S2Fixture.Create(packageB,false,1);
            Assert.That(a.SaveRetainedSnapshot(),Is.EqualTo(b.SaveRetainedSnapshot()));Assert.That(a.SaveRetainedSnapshot(),Is.EqualTo(c.SaveRetainedSnapshot()));
            for(int tick=1;tick<=60;tick++)
            {
                ulong hash=0,reverseHash=0,slotHash=0;
                Assert.DoesNotThrow(()=>hash=M0c2S2Fixture.Step(a));Assert.DoesNotThrow(()=>reverseHash=M0c2S2Fixture.Step(b));
                Assert.DoesNotThrow(()=>slotHash=M0c2S2Fixture.Step(c));Assert.That(reverseHash,Is.EqualTo(hash),"reverse tick "+tick);
                Assert.That(slotHash,Is.EqualTo(hash),"slot-only tick "+tick);
                Assert.That(a.GameplayStatusCount,Is.GreaterThan(0));
                Assert.That(a.SaveRetainedSnapshot(),Is.EqualTo(b.SaveRetainedSnapshot()),"all canonical rows reverse tick "+tick);
                Assert.That(a.SaveRetainedSnapshot(),Is.EqualTo(c.SaveRetainedSnapshot()),"all canonical rows slot-only tick "+tick);
            }
            TestContext.WriteLine("S2 T1 package={0}; variants=3; ticks=60; per-tick hashes and complete canonical snapshot rows match",packageB?"B":"A");
        }
        [TestCase(false),TestCase(true)] public void EveryMidRunCheckpointRestoresWithDifferentSlotsAndContinuesAfterSourceRetirement(bool packageB)
        {
            var definitions=new[]{new GameplayStatusDef(1,packageB?"healing":"burn",GameplayStatusDuration.Timed,40,3,1,
                    periodicActions:packageB?new[]{GameplayPeriodicAction.ChangeResource(1,1,3)}:new[]{GameplayPeriodicAction.Damage(1,1,3)}),
                new GameplayStatusDef(2,packageB?"chakra":"supply",GameplayStatusDuration.Timed,44,5,2,
                    periodicActions:new[]{GameplayPeriodicAction.ChangeResource(1,2,2)}),
                new GameplayStatusDef(3,"absorb",GameplayStatusDuration.Timed,48,absorbResourceKey:1)};
            var plan=new[]{new S2Tick(new[]{S2Op.Apply(1),S2Op.Apply(2,true),S2Op.Absorb(3,false,7,2)})};
            var baseline=M0c2S2Fixture.CreateWith(out _,plan,packageB,statuses:definitions,retireTick:3);
            var snapshots=new byte[301][];var hashes=new ulong[601];
            snapshots[0]=baseline.SaveRetainedSnapshot();hashes[0]=baseline.StateHashRetained();
            for(int tick=1;tick<=600;tick++)
            {hashes[tick]=M0c2S2Fixture.Step(baseline);if(tick<=300)snapshots[tick]=baseline.SaveRetainedSnapshot();}
            for(int checkpoint=0;checkpoint<=300;checkpoint++)
            {
                var restored=M0c2S2Fixture.CreateWith(out _,plan,packageB,true,statuses:definitions,retireTick:3,slotVariant:1);
                restored.RestoreRetainedSnapshot(snapshots[checkpoint],true);
                Assert.That(restored.StateHashRetained(),Is.EqualTo(hashes[checkpoint]),"checkpoint "+checkpoint);
                if(checkpoint>=3 && checkpoint<40)
                {
                    Assert.That(restored.Entities.TryResolve(M0c2S2Fixture.Low,out _),Is.False);
                    Assert.That(restored.GameplayStatusCount,Is.EqualTo(3));
                    Assert.That(restored.GameplayStatusAt(0).SourceSnapshot.Source.Id,Is.EqualTo(M0c2S2Fixture.Low));
                }
                for(int tick=checkpoint+1;tick<=checkpoint+300;tick++)
                    Assert.That(M0c2S2Fixture.Step(restored),Is.EqualTo(hashes[tick]),"checkpoint "+checkpoint+" continued tick "+tick);
            }
            TestContext.WriteLine("S2 checkpoints package={0}; every checkpoint 0..300; continuation=300; comparisons=90300; retired source snapshots preserved",packageB?"B":"A");
        }
        [Test] public void DispelCancelsFutureTasksButPreservesThePulseReleasedInRequests()
        {
            var d=new GameplayStatusDef(1,"pulse",GameplayStatusDuration.Timed,8,1,1,
                periodicActions:new[]{GameplayPeriodicAction.Damage(1,1,7)},dispelTags:1);
            var sim=M0c2S2Fixture.CreateWith(out _,new[]{new S2Tick(new[]{S2Op.Apply(1)}),new S2Tick(new[]{S2Op.Dispel(1)})},statuses:new[]{d});
            M0c2S2Fixture.Step(sim);M0c2S2Fixture.Step(sim);
            Assert.That(sim.GameplayResource(M0c2S2Fixture.Target.Id,1),Is.EqualTo(8000*M0c2S2Fixture.S-7));
            Assert.That(sim.GameplayStatusCount,Is.Zero);Assert.That(sim.Scheduler.PendingCount,Is.Zero);
            M0c2S2Fixture.Step(sim);Assert.That(sim.GameplayResource(M0c2S2Fixture.Target.Id,1),Is.EqualTo(8000*M0c2S2Fixture.S-7));
        }
        [Test] public void BDataProvidesChakraHealingSealAndControlThroughTheSamePrimitives()
        {
            var plan=new[]{new S2Tick(new[]{S2Op.Apply(1,false,1),S2Op.Apply(2,false,2),S2Op.Apply(5,true,1)}),
                new S2Tick(new[]{S2Op.Apply(4)}),new S2Tick(new[]{S2Op.Dispel(8),S2Op.Apply(4,false,2)})};
            var sim=M0c2S2Fixture.CreateWith(out _,plan,packageB:true);
            M0c2S2Fixture.Step(sim);M0c2S2Fixture.Step(sim);Assert.That(sim.GameplayStatusCount,Is.EqualTo(3),"seal immunity blocks control");
            M0c2S2Fixture.Step(sim);Assert.That(sim.GameplayStatusCount,Is.EqualTo(3));
            Assert.That(sim.GameplayStatusAt(2).Key.Definition,Is.EqualTo(4),"dispel removes the seal before control is applied");
            Assert.That(sim.GameplayResource(M0c2S2Fixture.Target.Id,1),Is.EqualTo(8003*M0c2S2Fixture.S));
            Assert.That(sim.GameplayResource(M0c2S2Fixture.Target.Id,2),Is.EqualTo(102*M0c2S2Fixture.S));
        }
        private static StatusOracle.Key OracleKey(StableId source,ulong tick,uint ordinal)
            =>new StatusOracle.Key(source.Value,tick,0,source.Value,tick,ordinal,0,M0c2S2Fixture.Target.Id.Value);
    }
}
