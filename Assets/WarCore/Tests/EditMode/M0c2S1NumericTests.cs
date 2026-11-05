using System;
using System.IO;
using System.Numerics;
using NUnit.Framework;
using WarCore.Sim;
using WarCore.Tests.Oracle;

namespace WarCore.Tests
{
    [TestFixture, Category("M0c2S1")]
    public sealed class M0c2S1NumericTests
    {
        private const long M=GameplayMath.MaxMagnitude, S=GameplayMath.Scale, F=GameplayMath.MaxFactor;
        private static readonly GameplayModifier[] Empty=Array.Empty<GameplayModifier>();
        private static GameplayModifier Mod(GameplayModifierStage stage,long value,ulong key)
            => new GameplayModifier(stage,value,new GameplayModifierKey(new StableId(1),1,1,key));

        [Test]
        public void IndependentFrozenNumericVectorsCoverBothSidesOfEveryS1Domain()
        {
            string path=Path.Combine("Assets","WarCore","Tests","EditMode","Vectors","m0c2-s1-numeric-v1.csv");
            string[] lines=File.ReadAllLines(path);
            int rows=0;
            for (int i=1;i<lines.Length;i++)
            {
                string[] row=lines[i].Split(','); string op=row[0];
                long a=long.Parse(row[1]),b=long.Parse(row[2]),low=long.Parse(row[3]),high=long.Parse(row[4]);
                bool valid=row[5]!="reject"; long count=0;
                Action action=() =>
                {
                    long actual=0;
                    switch(op)
                    {
                        case "magnitude": GameplayMath.RequireMagnitude(a); break;
                        case "signed": GameplayMath.RequireSignedMagnitude(a); break;
                        case "factor": GameplayMath.RequireFactor(a); break;
                        case "rate": GameplayMath.RequireRate(a); break;
                        case "ratio": GameplayMath.RequireRatio(a); break;
                        case "product": actual=GameplayMath.MultiplyFactor(a,b,ref count); break;
                        case "resource": actual=GameplayMath.ChangeResource(a,b,low,high,ref count); break;
                        case "time": actual=GameplayMath.FutureTick(a,(int)b,ref count); break;
                        case "spend": actual=GameplayMath.Spend(a,low,b,out long after)?after:a; count=GameplayMath.CanSpend(a,low,b)?1:0; break;
                        case "diagnostic": count=a; GameplayMath.CountSaturation(ref count); actual=count; break;
                        case "modifiers":
                            var modifiers=new GameplayModifier[a<0?0:(int)a];
                            for(int j=0;j<modifiers.Length;j++) modifiers[j]=Mod(GameplayModifierStage.PreFlat,0,(ulong)j);
                            GameplayDamage.Validate(new GameplayDamageSpec(0),modifiers,(int)a);
                            break;
                        default: throw new InvalidOperationException("Unknown numeric vector.");
                    }
                    if (valid)
                    {
                        Assert.That(actual,Is.EqualTo(long.Parse(row[5])),lines[i]);
                        Assert.That(count,Is.EqualTo(long.Parse(row[6])),lines[i]);
                    }
                };
                if (valid) action(); else Assert.Throws<SimBoundaryException>(() => action(),lines[i]);
                rows++;
            }
            TestContext.WriteLine("S1 independent frozen numeric vectors: rows={0}, mismatches=0",rows);
            Assert.That(rows,Is.GreaterThan(100));
        }

        private static void Check(GameplayDamageSpec spec,GameplayModifier[] mods,long roll)
        {
            var stages=new int[mods.Length]; var values=new long[mods.Length];
            for (int i=0;i<mods.Length;i++) { stages[i]=(int)mods[i].Stage; values[i]=mods[i].MagnitudeRaw; }
            long expectedCount=0,actualCount=0;
            long expected=GameplayOracle.Damage(spec.BaseRaw,(int)spec.EnabledStages,spec.ResistanceRaw,spec.PenetrationRaw,
                spec.CriticalChanceRaw,spec.CriticalFactorRaw,spec.MinimumRaw,spec.MaximumRaw,roll,stages,values,ref expectedCount,out bool critical);
            GameplayDamageResult actual=GameplayDamage.Evaluate(spec,mods,mods.Length,roll,ref actualCount);
            Assert.That(actual.AmountRaw,Is.EqualTo(expected));
            Assert.That(actual.WasCritical,Is.EqualTo(critical));
            Assert.That(actualCount,Is.EqualTo(expectedCount));
        }

        [Test]
        public void AllFixedStagesAndEveryEnableMaskMatchIndependentWideOracle()
        {
            var mods=new[]{Mod(GameplayModifierStage.PreFlat,10*S+1,1),Mod(GameplayModifierStage.PreFlat,-S,2),
                Mod(GameplayModifierStage.Rate,S/2+1,3),Mod(GameplayModifierStage.Product,S/3,4),
                Mod(GameplayModifierStage.Product,2*S,5),Mod(GameplayModifierStage.PostFlat,3*S+7,6)};
            for (int mask=0;mask<=127;mask++)
                Check(new GameplayDamageSpec(19*S+1,(GameplayDamageStages)mask,S/2,S/4,S/2,3*S,0,21*S),mods,S/2-1);
            TestContext.WriteLine("S1 fixed phase oracle: 128 stage enable masks, mismatches=0");
        }

        [Test]
        public void FlatStageSumsBeforeClampingButProductClampsAfterEachModifier()
        {
            Check(new GameplayDamageSpec(M),new[]{Mod(GameplayModifierStage.PreFlat,M,1),Mod(GameplayModifierStage.PreFlat,-M,2)},0);
            Check(new GameplayDamageSpec(M),new[]{Mod(GameplayModifierStage.Product,2*S,1),Mod(GameplayModifierStage.Product,S/2,2)},0);
            Check(new GameplayDamageSpec(1),new[]{Mod(GameplayModifierStage.Product,S/2,1),Mod(GameplayModifierStage.Product,2*S,2)},0);
            Check(new GameplayDamageSpec(1),new[]{Mod(GameplayModifierStage.PreFlat,-2,1),Mod(GameplayModifierStage.PostFlat,2,2)},0);
            Check(new GameplayDamageSpec(11),new[]{Mod(GameplayModifierStage.Rate,-F,1),Mod(GameplayModifierStage.Rate,F,2)},0);
            Check(new GameplayDamageSpec(M),new[]{Mod(GameplayModifierStage.Rate,F,1)},0);
        }

        [Test]
        public void BoundedResistanceAndCriticalThresholdUseExactIntegerEdges()
        {
            foreach(long resistance in new[]{0L,1,S-1,S})
            foreach(long penetration in new[]{0L,1,S-1,S})
            foreach(long chance in new[]{0L,1,S-1,S})
            foreach(long roll in new[]{0L,1,S-1})
                Check(new GameplayDamageSpec(3*S+1,GameplayDamageStages.All,resistance,penetration,chance,2*S+1),Empty,roll);
            long diagnostics=0;
            Assert.Throws<SimBoundaryException>(() => GameplayDamage.Evaluate(new GameplayDamageSpec(1),Empty,0,S,ref diagnostics));
            Assert.Throws<SimBoundaryException>(() => GameplayDamage.Evaluate(new GameplayDamageSpec(1),Empty,0,-1,ref diagnostics));
            Assert.That(diagnostics,Is.Zero);
        }

        [Test]
        public void ModifierOrderAndFullIdentityAreValidatedBeforeAnyStage()
        {
            var high=new GameplayModifierKey(new StableId(ulong.MaxValue),0,0,1);
            var low=new GameplayModifierKey(new StableId(1),0,0,1);
            Assert.That(low.CompareTo(high),Is.LessThan(0));
            var sorted=new[]{new GameplayModifier(GameplayModifierStage.Product,S/2,low),new GameplayModifier(GameplayModifierStage.Product,2*S,high)};
            Check(new GameplayDamageSpec(1),sorted,0);
            var reversed=new[]{sorted[1],sorted[0]}; long diagnostics=0;
            Assert.Throws<SimBoundaryException>(() => GameplayDamage.Evaluate(new GameplayDamageSpec(1),reversed,2,0,ref diagnostics));
            Assert.Throws<SimBoundaryException>(() => GameplayDamage.Evaluate(new GameplayDamageSpec(1),new[]{sorted[0],sorted[0]},2,0,ref diagnostics));
            Assert.That(diagnostics,Is.Zero);
            for(int field=0;field<10;field++)
            {
                var a=new GameplayModifierKey(new StableId(1),1,1,1,new StableId(1),1,field!=5,new StableId(1),1,1);
                var b=new GameplayModifierKey(new StableId(field==0?2UL:1UL),field==1?2UL:1UL,field==2?2U:1U,
                    field==9?2UL:1UL,new StableId(field==3?2UL:1UL),field==4?2UL:1UL,true,
                    new StableId(field==6?2UL:1UL),field==7?(ushort)2:(ushort)1,field==8?2UL:1UL);
                Assert.That(a.CompareTo(b),Is.LessThan(0),"key field "+field);
            }
        }

        private static ulong Next(ref ulong state)
        {
            state^=state<<13; state^=state>>7; state^=state<<17; return state;
        }
        [Test]
        public void SeededMixedStageExtremesMatchIndependentWideOracle()
        {
            ulong state=0xC2005100EUL;
            long[] amounts={0,1,S-1,S,S+1,M/2,M-1,M};
            long[] flats={-M,-M+1,-S,-1,0,1,S,M-1,M};
            long[] rates={-F,-F+1,-S,-1,0,1,S,F-1,F};
            long[] factors={0,1,S-1,S,S+1,F-1,F};
            long[] ratios={0,1,S/2,S-1,S};
            for(int sample=0;sample<4096;sample++)
            {
                var mods=new[]{Mod(GameplayModifierStage.PreFlat,flats[Next(ref state)%(ulong)flats.Length],1),
                    Mod(GameplayModifierStage.PreFlat,flats[Next(ref state)%(ulong)flats.Length],2),
                    Mod(GameplayModifierStage.Rate,rates[Next(ref state)%(ulong)rates.Length],3),
                    Mod(GameplayModifierStage.Rate,rates[Next(ref state)%(ulong)rates.Length],4),
                    Mod(GameplayModifierStage.Product,factors[Next(ref state)%(ulong)factors.Length],5),
                    Mod(GameplayModifierStage.Product,factors[Next(ref state)%(ulong)factors.Length],6),
                    Mod(GameplayModifierStage.PostFlat,flats[Next(ref state)%(ulong)flats.Length],7)};
                long lower=amounts[Next(ref state)%(ulong)amounts.Length];
                long upper=amounts[Next(ref state)%(ulong)amounts.Length];
                if(lower>upper) { long swap=lower; lower=upper; upper=swap; }
                Check(new GameplayDamageSpec(amounts[Next(ref state)%(ulong)amounts.Length],(GameplayDamageStages)(Next(ref state)%128),
                    ratios[Next(ref state)%(ulong)ratios.Length],ratios[Next(ref state)%(ulong)ratios.Length],
                    ratios[Next(ref state)%(ulong)ratios.Length],factors[Next(ref state)%(ulong)factors.Length],lower,upper),mods,(long)(Next(ref state)%(ulong)S));
            }
            TestContext.WriteLine("S1 BigInteger pipeline oracle: seed=0xC2005100E, mixed stage cases=4096, mismatches=0");
        }

        [Test]
        public void ModifierCapacityAndInvalidRangesFailBeforeArithmetic()
        {
            var full=new GameplayModifier[65536];
            for(int i=0;i<full.Length;i++) full[i]=Mod(GameplayModifierStage.PreFlat,M,(ulong)i);
            Check(new GameplayDamageSpec(1),new[]{Mod(GameplayModifierStage.PreFlat,-M,1)},0);
            long diagnostics=0;
            Assert.That(GameplayDamage.Evaluate(new GameplayDamageSpec(1),full,65535,0,ref diagnostics).AmountRaw,Is.EqualTo(M));
            Assert.Throws<SimBoundaryException>(() => GameplayDamage.Evaluate(new GameplayDamageSpec(1),full,65536,0,ref diagnostics));
            Assert.Throws<SimBoundaryException>(() => GameplayDamage.Validate(new GameplayDamageSpec(1),Empty,-1));
            Assert.Throws<SimBoundaryException>(() => GameplayDamage.Validate(new GameplayDamageSpec(1),Empty,1));
            foreach(var spec in new[]{new GameplayDamageSpec(-1),new GameplayDamageSpec(M+1),new GameplayDamageSpec(1,(GameplayDamageStages)128),
                new GameplayDamageSpec(1,resistanceRaw:S+1),new GameplayDamageSpec(1,penetrationRaw:-1),new GameplayDamageSpec(1,criticalChanceRaw:S+1),
                new GameplayDamageSpec(1,criticalFactorRaw:F+1),new GameplayDamageSpec(1,minimumRaw:2,maximumRaw:1)})
                Assert.Throws<SimBoundaryException>(() => GameplayDamage.Validate(spec,Empty,0));
        }

        [Test]
        public void ResourceCostRejectsWithoutClampingOrMutation()
        {
            foreach(long lower in new[]{-M,-1L,0,1,M})
            foreach(long cost in new[]{0L,1,M})
            {
                long value=lower==M?M:lower+1;
                bool enough=new BigInteger(value)-lower>=cost;
                Assert.That(GameplayMath.CanSpend(value,lower,cost),Is.EqualTo(enough));
                Assert.That(GameplayMath.Spend(value,lower,cost,out long result),Is.EqualTo(enough));
                Assert.That(result,Is.EqualTo(enough?value-cost:value));
            }
            Assert.Throws<SimBoundaryException>(() => GameplayMath.CanSpend(0,0,-1));
        }

        [Test,Category("G25")]
        public void S1DangerousOperationProbesUseExplicitWideAndBoundaryContracts()
        {
            Assert.That(FixedInt128.Abs(long.MinValue),Is.EqualTo(1UL<<63));
            Assert.Throws<SimOverflowException>(() => SimInteger.Divide(long.MinValue,-1));
            Assert.Throws<SimDivideByZeroException>(() => SimInteger.Divide(1,0));
            Assert.Throws<SimOverflowException>(() => SimInteger.Abs(long.MinValue));
            FixedUInt128 product=FixedUInt128.Multiply(ulong.MaxValue,ulong.MaxValue);
            Assert.That(product.Low,Is.EqualTo(1)); Assert.That(product.High,Is.EqualTo(ulong.MaxValue-1));
            foreach(int shift in new[]{0,1,63,64,65,127,128,129,int.MaxValue})
            {
                FixedUInt128 shifted=product.ShiftRight(shift);
                BigInteger expected=shift>=128?BigInteger.Zero:((BigInteger.One<<64)-1)*((BigInteger.One<<64)-1)>>shift;
                Assert.That(shifted.Low,Is.EqualTo((ulong)(expected&ulong.MaxValue)));
                Assert.That(shifted.High,Is.EqualTo((ulong)(expected>>64)));
            }
            Assert.Throws<SimBoundaryException>(() => product.ShiftRight(-1));
            long count=long.MaxValue;
            Assert.Throws<SimBoundaryException>(() => GameplayMath.CountSaturation(ref count));
            Assert.That(count,Is.EqualTo(long.MaxValue));
            long invalid=-1;
            Assert.Throws<SimBoundaryException>(() => GameplayMath.CountSaturation(ref invalid));
            TestContext.WriteLine("S1 G25 probes: abs-min, min/-1, zero divisor, full-width product, shift 0/1/63/64/65/127/128/129/max/-1, diagnostic max/negative PASS");
        }

        [Test,Category("G25")]
        public void FutureTicksMatchIndependentWideOracleAndRejectExhaustionBeforeMutation()
        {
            const long limit=long.MaxValue-1;
            long[] clocks={-1,0,1,limit-int.MaxValue-1,limit-int.MaxValue,limit-int.MaxValue+1,limit-2,limit-1,limit,limit+1};
            int[] periods={int.MinValue,-1,0,1,2,int.MaxValue-1,int.MaxValue};
            int cases=0;
            foreach(long now in clocks)
            foreach(int period in periods)
            {
                long expectedCount=0,actualCount=0,expected=0;
                bool valid=true;
                try { expected=GameplayOracle.FutureTick(now,period,ref expectedCount); }
                catch(ArgumentOutOfRangeException) { valid=false; }
                if(valid)
                {
                    Assert.That(GameplayMath.FutureTick(now,period,ref actualCount),Is.EqualTo(expected),"now="+now+" period="+period);
                    Assert.That(actualCount,Is.EqualTo(expectedCount));
                }
                else
                {
                    Assert.Throws<SimBoundaryException>(() => GameplayMath.FutureTick(now,period,ref actualCount));
                    Assert.That(actualCount,Is.Zero);
                }
                cases++;
            }
            long maxCounter=long.MaxValue;
            Assert.Throws<SimBoundaryException>(() => GameplayMath.FutureTick(limit-1,2,ref maxCounter));
            Assert.That(maxCounter,Is.EqualTo(long.MaxValue));
            long negativeCounter=-1;
            Assert.Throws<SimBoundaryException>(() => GameplayMath.FutureTick(0,1,ref negativeCounter));
            Assert.That(negativeCounter,Is.EqualTo(-1));
            TestContext.WriteLine("S1 future tick BigInteger oracle: cases={0}; counter max/negative guarded; mismatches=0",cases);
        }
    }
}
