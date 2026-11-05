using System;
using System.IO;
using System.Numerics;
using NUnit.Framework;
using WarCore.Sim;
using WarCore.Tests.Oracle;
using Simulation=WarCore.Sim.Sim;
namespace WarCore.Tests
{
    [TestFixture,Category("M0c2S2"),Category("M0c2S2Numeric")]
    public sealed class M0c2S2NumericTests
    {
        private const long M=GameplayMath.MaxMagnitude;
        private static Simulation Create(GameplayStatusDef definition,S2Tick[] plan,GameplayDamageStages stages=GameplayDamageStages.PreFlat,long initial=M,long minimum=0,long maximum=M)
        {
            var schema=new GameplaySchema(new[]{new GameplayResourceDef(1,"value",0,M,initial)},
                new[]{new GameplayDamageTypeDef(1,"direct",1,1)},
                new[]{new GameplayPipelineDef(1,new GameplayDamageSpec(0,stages,minimumRaw:minimum,maximumRaw:maximum))},
                new[]{new GameplayArchetypeDef(1,new[]{1UL})},new GameplayCapacities(3,3,16,32,statuses:1),"s2.numeric",new[]{definition});
            var catalog=new PlacedCatalog(new StableContentManifest(0),new PlacedCatalogEntry(29,1,1),new PlacedCatalogEntry(29,2,1),new PlacedCatalogEntry(29,3,1));
            var sim=Simulation.CreateRetainedWorld(41,catalog,new SimConfig(entityCapacity:8,timerCapacity:16),gameplaySchema:schema);
            foreach(var key in new[]{M0c2S2Fixture.Target,M0c2S2Fixture.SourceA,M0c2S2Fixture.SourceB})sim.BindGameplay(sim.Entities.Create(key),1);
            sim.RegisterGameplay("s2.numeric",new S2Script(plan,false,false,0),SimSystemPhase.Queries);return sim;
        }
        private static GameplayStatusDef Def(GameplayStackPolicy policy=GameplayStackPolicy.Refresh,int maxStacks=1,int maxInstances=1,
            long strength=0,GameplayStatusDuration kind=GameplayStatusDuration.Infinite,int duration=0,int period=0,long magnitude=0,bool modifier=false,GameplayModifierStage stage=GameplayModifierStage.PreFlat)
            =>new GameplayStatusDef(1,"numeric",kind,duration,period,period==0?0:1,policy,maxStacks:maxStacks,maxInstances:maxInstances,
                strengthRaw:strength,modifiers:modifier?new[]{new GameplayStatusModifierDef(1,1,stage,magnitude)}:null,
                periodicActions:period==0?null:new[]{GameplayPeriodicAction.Damage(1,1,1)},absorbResourceKey:1);
        private static long Saturations(Simulation sim)
            =>(long)M0c2S2ProtocolTests.Word(new M0c2S2ProtocolTests.Document(sim).Tables[8][0],0);
        private static Simulation RestoreStacks(GameplayStatusDef d,S2Tick[] plan,int old,GameplayDamageStages stages=GameplayDamageStages.PreFlat,long initial=M)
        {
            var seed=Create(d,plan,stages,initial);M0c2S2Fixture.Step(seed);var document=new M0c2S2ProtocolTests.Document(seed);
            if(old==0)document.Tables[6].Clear();else M0c2S2ProtocolTests.Put(document.Tables[6][0],131,(ulong)old);
            var restored=Create(d,plan,stages,initial);restored.RestoreRetainedSnapshot(document.Encode(),true);return restored;
        }
        [Test] public void ConfiguredDamageLimitsApplyBeforeAbsorbConsumption()
        {
            long[][] cases={new[]{1L,10L,M,7L,3L},new[]{20L,0L,4L,3L,1L}};
            foreach(long[] item in cases)
            {
                var sim=Create(Def(),new[]{new S2Tick(new[]{S2Op.Absorb(1,false,item[3]),S2Op.Damage(false,item[0],2)})},
                    GameplayDamageStages.Limits,minimum:item[1],maximum:item[2]);
                Assert.DoesNotThrow(()=>M0c2S2Fixture.Step(sim));
                Assert.That(M-sim.GameplayResource(M0c2S2Fixture.Target.Id,1),Is.EqualTo(item[4]),
                    "configured min/max must clamp first, then the shield can reduce the resulting amount below the configured minimum");
                Assert.That(sim.GameplayStatusAt(0).AbsorbRemainingRaw,Is.Zero,"the final clamped amount consumes the shield");
            }
            TestContext.WriteLine("S2 configured limits before absorb: min(1 -> 10)-7=3; max(20 -> 4)-3=1");
        }
        [Test] public void PeriodicStackedAmountsMatchIndependentWideOracleForDamageAndSignedResourceChanges()
        {
            long[] amounts={-M,-M+1,-M/2-1,-1,0,1,M/2,M/2+1,M-1,M};
            int rows=0;
            foreach(bool damage in new[]{true,false})foreach(long amount in amounts)
            {
                if(damage && amount<0)continue;
                foreach(int stacks in new[]{2,3,65535})foreach(long initial in new[]{0L,M})
                {
                    var action=damage?GameplayPeriodicAction.Damage(1,1,amount):GameplayPeriodicAction.ChangeResource(1,1,amount);
                    var definition=new GameplayStatusDef(1,"stacked-periodic",GameplayStatusDuration.Infinite,
                        periodTicks:1,firstPulseDelayTicks:1,stackPolicy:GameplayStackPolicy.Stack,maxStacks:65535,
                        periodicActions:new[]{action});
                    var plan=new[]{new S2Tick(new[]{S2Op.Apply(1)})};
                    var sim=RestoreStacks(definition,plan,stacks,GameplayDamageStages.PreFlat,initial);
                    long expected=StatusOracle.StackedPeriodicResource(initial,amount,stacks,damage,out long count);
                    Assert.DoesNotThrow(()=>M0c2S2Fixture.Step(sim));
                    string label=$"damage={damage}, amount={amount}, stacks={stacks}, initial={initial}";
                    Assert.That(sim.GameplayResource(M0c2S2Fixture.Target.Id,1),Is.EqualTo(expected),label);
                    Assert.That(Saturations(sim),Is.EqualTo(count),label+" diagnostics");
                    Assert.That(sim.GameplayStatusAt(0).PulseOrdinal,Is.EqualTo(1),label+" exactly one pulse");
                    rows++;
                }
            }
            Assert.That(rows,Is.EqualTo(96));
            TestContext.WriteLine("S2 periodic stack amounts: 96 public Damage/signed ChangeResource boundary cases match independent wide oracle");
        }
        [Test] public void DisabledStatusModifierStagesDoNotRunArithmeticOrCountSaturation()
        {
            var definition=Def(GameplayStackPolicy.Stack,maxStacks:2,magnitude:M,modifier:true);
            var disabled=Create(definition,new[]{new S2Tick(new[]{S2Op.Apply(1,false,1),S2Op.Apply(1,false,2),S2Op.Damage(false,1,3)})},GameplayDamageStages.None);
            M0c2S2Fixture.Step(disabled);
            Assert.That(disabled.GameplayResource(M0c2S2Fixture.Target.Id,1),Is.EqualTo(M-1));
            Assert.That(Saturations(disabled),Is.Zero,"disabled flat stage must not perform stack multiplication");
            var enabled=Create(definition,new[]{new S2Tick(new[]{S2Op.Apply(1,false,1),S2Op.Apply(1,false,2),S2Op.Damage(false,0,3)})});
            M0c2S2Fixture.Step(enabled);Assert.That(enabled.GameplayResource(M0c2S2Fixture.Target.Id,1),Is.Zero);
            Assert.That(Saturations(enabled),Is.EqualTo(1),"enabled flat scaling saturates once before adding the zero base");
        }
        [Test] public void FrozenExpiryVectorsSeparateZeroDurationFoldFromRequiredFutureScheduling()
        {
            int rows=0;
            foreach(string line in File.ReadAllLines("Assets/WarCore/Tests/EditMode/Vectors/m0c2-s2-numeric-v1.csv"))
            {
                if(!line.StartsWith("expiry,"))continue;string[] p=line.Split(',');long now=long.Parse(p[1]);int duration=int.Parse(p[2]);long saturations=0;
                if(p[4]=="reject")Assert.Throws<SimBoundaryException>(()=>GameplayMath.FutureTick(now,duration==0?1:duration,ref saturations),line);
                else
                {
                    long due=long.Parse(p[4]);Assert.That(StatusOracle.Future(now,duration),Is.EqualTo(due),line+" independent wide oracle");
                    if(duration==0)
                    {
                        var sim=Create(Def(kind:GameplayStatusDuration.Timed,duration:0),new[]{new S2Tick(new[]{S2Op.Apply(1)})});
                        M0c2S2Fixture.Step(sim);Assert.That(sim.GameplayStatusCount,Is.Zero);Assert.That(sim.Scheduler.PendingCount,Is.Zero);
                    }
                    else if(now==long.MaxValue-1)
                        Assert.Throws<SimBoundaryException>(()=>GameplayMath.FutureTick(now,duration,ref saturations),line+" due==now cannot be scheduled in the future");
                    else
                    {
                        Assert.That(GameplayMath.FutureTick(now,duration,ref saturations),Is.EqualTo(due),line);
                        Assert.That(saturations,Is.EqualTo(long.Parse(p[5])),line+" diagnostics");
                    }
                }
                rows++;
            }
            Assert.That(rows,Is.GreaterThan(25));TestContext.WriteLine("S2 expiry numeric vector rows={0}; zero durations fold; no-future scheduling explicitly rejects",rows);
        }
        [Test] public void FrozenPulseCursorVectorsUseNormalReplayAndExplicitExhaustionFaultInjection()
        {
            int rows=0;
            foreach(string line in File.ReadAllLines("Assets/WarCore/Tests/EditMode/Vectors/m0c2-s2-numeric-v1.csv"))
            {
                if(!line.StartsWith("pulse-ordinal,"))continue;string[] p=line.Split(',');long value=long.Parse(p[1]);
                if(p[4]=="reject")
                    Assert.Throws<SimBoundaryException>(()=>M0c2S2BoundaryTests.ProbePulseOrdinal(uint.MaxValue),line);
                else if(value<=1)
                {
                    var sim=Create(Def(period:1),new[]{new S2Tick(new[]{S2Op.Apply(1)})});
                    for(int tick=0;tick<=value;tick++)M0c2S2Fixture.Step(sim);
                    Assert.That(sim.GameplayStatusAt(0).PulseOrdinal,Is.EqualTo((uint)value),line+" legal replay");
                    byte[] saved=sim.SaveRetainedSnapshot();var resumed=Create(Def(period:1),new[]{new S2Tick(new[]{S2Op.Apply(1)})});
                    resumed.RestoreRetainedSnapshot(saved,true);M0c2S2Fixture.Step(resumed);
                    Assert.That(resumed.GameplayStatusAt(0).PulseOrdinal,Is.EqualTo((uint)value+1),line);
                }
                else
                    Assert.That(M0c2S2BoundaryTests.ProbePulseOrdinal((uint)value),Is.EqualTo((uint)value+1),line+" explicit counter fault injection, not a valid historical save");
                rows++;
            }
            Assert.That(rows,Is.EqualTo(6));TestContext.WriteLine("S2 pulse cursor vector rows=6; 0/1 legal replay and restore; maximum neighbours use explicit counter fault injection");
        }
        [TestCase(false),TestCase(true)] public void StackedSignedFlatBoundsAndNeighboursMatchIndependentWideOracle(bool postFlat)
            =>CheckStackedModifierVectors("stack-flat",postFlat?GameplayModifierStage.PostFlat:GameplayModifierStage.PreFlat);
        [Test] public void StackedRateBoundsAndNeighboursUseTheFactorDomainAndIndependentWideOracle()
            =>CheckStackedModifierVectors("stack-rate",GameplayModifierStage.Rate);
        [Test] public void StackedProductUsesOneScaledFactorPerInstanceAndTheFactorDomain()
        {
            CheckStackedModifierVectors("stack-product",GameplayModifierStage.Product);
            var definition=Def(GameplayStackPolicy.Stack,maxStacks:2,magnitude:GameplayMath.Scale/2,modifier:true,stage:GameplayModifierStage.Product);
            var plan=new[]{new S2Tick(new[]{S2Op.Apply(1)}),new S2Tick(new[]{S2Op.Damage(false,11)})};
            var sim=RestoreStacks(definition,plan,2,GameplayDamageStages.Product);M0c2S2Fixture.Step(sim);
            Assert.That(M-sim.GameplayResource(M0c2S2Fixture.Target.Id,1),Is.EqualTo(11),"two half-factor stacks form one unit factor; they are not two successive half products");
        }
        private static void CheckStackedModifierVectors(string operation,GameplayModifierStage stage)
        {
            GameplayDamageStages enabled=stage==GameplayModifierStage.PreFlat?GameplayDamageStages.PreFlat:
                stage==GameplayModifierStage.PostFlat?GameplayDamageStages.PostFlat:
                stage==GameplayModifierStage.Rate?GameplayDamageStages.Rate:GameplayDamageStages.Product;
            int rows=0;
            foreach(string line in File.ReadAllLines("Assets/WarCore/Tests/EditMode/Vectors/m0c2-s2-numeric-v1.csv"))
            {
                if(!line.StartsWith(operation+","))continue;string[] p=line.Split(',');long raw=long.Parse(p[1]),basis=long.Parse(p[3]);int stacks=int.Parse(p[2]);
                var definition=Def(GameplayStackPolicy.Stack,maxStacks:65535,magnitude:raw,modifier:true,stage:stage);
                var plan=new[]{new S2Tick(new[]{S2Op.Apply(1)}),new S2Tick(new[]{S2Op.Damage(false,basis)})};
                if(p[4]=="reject")Assert.Throws<SimBoundaryException>(()=>Create(definition,plan,enabled),line);
                else
                {
                    long expected=long.Parse(p[4]),count=long.Parse(p[5]);
                    Assert.That(StatusOracle.StackedModifierDamage(basis,raw,stacks,(int)stage,out long oracleCount),Is.EqualTo(expected),line+" independent wide formula");
                    Assert.That(oracleCount,Is.EqualTo(count),line+" independent diagnostic count");
                    var sim=RestoreStacks(definition,plan,stacks,enabled);Assert.DoesNotThrow(()=>M0c2S2Fixture.Step(sim),line);
                    Assert.That(M-sim.GameplayResource(M0c2S2Fixture.Target.Id,1),Is.EqualTo(expected),line);
                    Assert.That(Saturations(sim),Is.EqualTo(count),line+" diagnostics");
                }
                rows++;
            }
            Assert.That(rows,Is.GreaterThan(30));TestContext.WriteLine("S2 stacked modifier {0}/{1}: {2} frozen boundary rows match public settlement and independent wide oracle",operation,stage,rows);
        }
        [Test] public void FirstPulseDelayZeroNormalizesToNextTickAndUpperNeighboursRemainExplicit()
        {
            int rows=0;
            foreach(string line in File.ReadAllLines("Assets/WarCore/Tests/EditMode/Vectors/m0c2-s2-numeric-v1.csv"))
            {
                if(!line.StartsWith("first-delay,"))continue;string[] p=line.Split(',');long delay=long.Parse(p[1]);
                var definition=new GameplayStatusDef(1,"delay",GameplayStatusDuration.Infinite,periodTicks:1,firstPulseDelayTicks:unchecked((int)delay),
                    periodicActions:new[]{GameplayPeriodicAction.Damage(1,1,1)});
                var plan=new[]{new S2Tick(new[]{S2Op.Apply(1)})};
                if(p[4]=="reject")Assert.Throws<SimBoundaryException>(()=>Create(definition,plan),line);
                else
                {
                    var sim=Create(definition,plan);Assert.DoesNotThrow(()=>M0c2S2Fixture.Step(sim),line);
                    Assert.That(sim.GameplayStatusAt(0).NextPulseTick,Is.EqualTo(1+long.Parse(p[4])),line);
                    Assert.That(sim.GameplayResource(M0c2S2Fixture.Target.Id,1),Is.EqualTo(M),"new status cannot pulse in its application tick");
                    M0c2S2Fixture.Step(sim);Assert.That(sim.GameplayResource(M0c2S2Fixture.Target.Id,1),Is.EqualTo(delay<=1?M-1:M),line);
                }
                rows++;
            }
            Assert.That(rows,Is.EqualTo(6));TestContext.WriteLine("S2 first pulse delay: 6 boundary rows; delay0 and delay1 both first pulse on the next tick");
        }
        [Test] public void FrozenNumericVectorsUsePublicDefinitionAdmissionRestoreAndSettlementBoundaries()
        {
            string[] lines=File.ReadAllLines("Assets/WarCore/Tests/EditMode/Vectors/m0c2-s2-numeric-v1.csv");int rows=0;
            foreach(string line in lines)
            {
                if(line.StartsWith("operation,"))continue;string[] p=line.Split(',');string op=p[0];
                if(op=="expiry" || op=="pulse-ordinal" || op=="first-delay" || op=="stack-flat" || op=="stack-rate" || op=="stack-product")continue;
                BigInteger wide=BigInteger.Parse(p[1]);long a=wide<=long.MaxValue?(long)wide:0,b=long.Parse(p[2]),c=long.Parse(p[3]);
                bool valid=p[4]!="reject";long expected=valid && BigInteger.Parse(p[4])<=long.MaxValue?long.Parse(p[4]):0;
                long expectedSaturations=long.Parse(p[5]);
                TestDelegate run=()=>
                {
                    GameplayStatusDef d=null;
                    switch(op)
                    {
                        case "max-stacks":d=Def(GameplayStackPolicy.Stack,maxStacks:unchecked((int)a));break;
                        case "max-instances":d=Def(GameplayStackPolicy.Independent,maxInstances:unchecked((int)a));break;
                        case "duration":d=Def(kind:GameplayStatusDuration.Timed,duration:unchecked((int)a));break;
                        case "period":
                            d=new GameplayStatusDef(1,"period",GameplayStatusDuration.Infinite,periodTicks:unchecked((int)a),firstPulseDelayTicks:1,
                                periodicActions:new[]{GameplayPeriodicAction.Damage(1,1,1)});break;
                        case "strength":d=Def(strength:a);break;
                        case "absorb":
                        {
                            var sim=Create(Def(),new[]{new S2Tick(new[]{S2Op.Absorb(1,false,a)})});M0c2S2Fixture.Step(sim);
                            Assert.That(sim.GameplayStatusAt(0).AbsorbRemainingRaw,Is.EqualTo(expected),line);return;
                        }
                        case "effect-token":
                        {
                            var plan=new[]{new S2Tick(new[]{S2Op.Apply(1)})};var seed=Create(Def(),plan);
                            var document=new M0c2S2ProtocolTests.Document(seed);ulong cursor=wide.Sign<0?ulong.MaxValue:(ulong)wide;
                            M0c2S2ProtocolTests.Put(document.Tables[0][0],0,cursor);var restored=Create(Def(),plan);
                            restored.RestoreRetainedSnapshot(document.Encode(),true);M0c2S2Fixture.Step(restored);
                            Assert.That(restored.GameplayStatusAt(0).Key.Token,Is.EqualTo(cursor),line);return;
                        }
                        case "stack-merge":
                        {
                            var ops=new S2Op[(int)b];for(int i=0;i<ops.Length;i++)ops[i]=S2Op.Apply(1,false,(uint)i+1);
                            var plan=new[]{new S2Tick(new[]{S2Op.Apply(1)}),new S2Tick(ops)};
                            var sim=RestoreStacks(Def(GameplayStackPolicy.Stack,maxStacks:(int)c),plan,(int)a);M0c2S2Fixture.Step(sim);
                            Assert.That(sim.GameplayStatusAt(0).Stacks,Is.EqualTo(expected),line);
                            Assert.That(sim.GameplayStatusAt(0).Stacks,Is.EqualTo((long)BigInteger.Min(new BigInteger(a)+b,c)),line+" wide oracle");
                            Assert.That(Saturations(sim),Is.EqualTo(expectedSaturations),line+" diagnostics");return;
                        }
                        case "stack-magnitude":
                        {
                            var plan=new[]{new S2Tick(new[]{S2Op.Apply(1)}),new S2Tick(new[]{S2Op.Damage(false,0)})};
                            var sim=RestoreStacks(Def(GameplayStackPolicy.Stack,maxStacks:65535,magnitude:a,modifier:true),plan,(int)b);
                            M0c2S2Fixture.Step(sim);Assert.That(M-sim.GameplayResource(M0c2S2Fixture.Target.Id,1),Is.EqualTo(expected),line);
                            Assert.That(expected,Is.EqualTo(StatusOracle.ScaleStacks(a,(int)b,M)),line+" independent wide oracle");
                            Assert.That(Saturations(sim),Is.EqualTo(expectedSaturations),line+" diagnostics");return;
                        }
                        case "pulse-count":
                        {
                            d=new GameplayStatusDef(1,"period",GameplayStatusDuration.Timed,(int)a,(int)c,(int)b,
                                periodicActions:new[]{GameplayPeriodicAction.Damage(1,1,1)});
                            var sim=Create(d,new[]{new S2Tick(new[]{S2Op.Apply(1)})});for(int tick=0;tick<=a+1;tick++)M0c2S2Fixture.Step(sim);
                            Assert.That(M-sim.GameplayResource(M0c2S2Fixture.Target.Id,1),Is.EqualTo(expected),line);
                            Assert.That(StatusOracle.Pulses(1,a,(int)b,(int)c).Length,Is.EqualTo(expected),line+" independent enumeration");return;
                        }
                        default:throw new InvalidOperationException(op);
                    }
                    Create(d,new S2Tick[0]);
                };
                if(valid)Assert.DoesNotThrow(run,line);else Assert.Throws<SimBoundaryException>(run,line);
                rows++;
            }
            TestContext.WriteLine("S2 public numeric vector rows={0}; expiry/pulse cursor lanes are exercised separately; mismatches=0",rows);
            Assert.That(rows,Is.GreaterThan(100));
        }
    }
}
