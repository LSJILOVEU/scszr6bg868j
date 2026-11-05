using System;
using System.Reflection;
using NUnit.Framework;
using WarCore.Sim;
using Simulation=WarCore.Sim.Sim;

namespace WarCore.Tests
{
    [TestFixture,Category("M0c2S2LA")]
    public sealed class M0c2S2LastApplicationTests
    {
        private static readonly StableEntityKey Target=StableEntityKey.Placed(37,1),Source=StableEntityKey.Placed(37,2);
        private static VectorJson Cases=>VectorJson.Load("m0c2-s2-la-v1.json")["cases"];
        private static VectorJson Find(string name)
        {VectorJson cases=Cases;for(int i=0;i<cases.Count;i++)if(cases[i]["name"].Text==name)return cases[i];throw new Exception(name);}
        private static byte[] Hex(string value)
        {var bytes=new byte[value.Length/2];for(int i=0;i<bytes.Length;i++)bytes[i]=Convert.ToByte(value.Substring(i*2,2),16);return bytes;}
        private static Simulation Create(VectorJson item,bool run=true,long captureOverride=0,IRetainedGameplaySystem script=null)
        {
            int policy=(int)item["policy"].Integer,period=(int)item["period"].Integer;
            var def=new GameplayStatusDef(11,"status",(GameplayStatusDuration)item["duration"].Integer,
                (int)item["duration_ticks"].Integer,period,period,(GameplayStackPolicy)policy,
                maxStacks:policy==1?3:1,maxInstances:policy==2?3:1,dispelTags:1,
                periodicActions:period==0?null:new[]{GameplayPeriodicAction.ChangeResource(1,7,3)});
            var schema=new GameplaySchema(new[]{new GameplayResourceDef(7,"resource",0,1000,100)},
                new GameplayDamageTypeDef[0],new GameplayPipelineDef[0],new[]{new GameplayArchetypeDef(12,new[]{7UL})},
                new GameplayCapacities(2,2,16,32,statuses:4),"s2.la",new[]{def});
            var sim=Simulation.CreateRetainedWorld(91,new PlacedCatalog(new StableContentManifest(0),
                new PlacedCatalogEntry(37,1,1),new PlacedCatalogEntry(37,2,1)),
                new SimConfig(entityCapacity:4,timerCapacity:16),gameplaySchema:schema);
            sim.BindGameplay(sim.Entities.Create(Target),12);sim.BindGameplay(sim.Entities.Create(Source),12);
            sim.RegisterGameplay("s2.la-script",script??new Script(item,captureOverride),SimSystemPhase.Queries);
            if(run)
            {
                long prior=item["first_tick"].Integer-1;sim.Clock.Restore(prior);
                if(prior!=0)
                {
                    // Empty-world boundary seeding avoids an impossible 2^63-tick test loop.
                    typeof(SimScheduler).GetField("advancedThrough",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(sim.Scheduler,prior);
                    typeof(SimEventBuffer).GetField("currentTick",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(sim.Events,prior);
                }
                while(sim.Clock.Tick<item["tick"].Integer)sim.Step(default);
            }
            return sim;
        }
        private sealed class Script:IRetainedGameplaySystem
        {
            private readonly VectorJson item;private readonly long captureOverride;
            internal Script(VectorJson item,long captureOverride){this.item=item;this.captureOverride=captureOverride;}
            public int Order=>1;public string StateTypeId=>"test.s2.la";public int StateVersion=>1;
            public void Step(SimContext c)
            {
                VectorJson history=item["history"];Span<GameplayAction> actions=stackalloc GameplayAction[8];int count=0;
                for(int i=0;i<history.Count;i++)
                {
                    VectorJson entry=history[i];if(entry[0].Integer!=c.Clock.Tick)continue;
                    var key=new GameplayApplyKey(Source.Id,(ulong)c.Clock.Tick,0,Source.Id,(ulong)c.Clock.Tick,(uint)i+1);
                    var snapshot=new GameplaySourceSnapshot(c.Owner.GameplayTarget(Source.Id),captureOverride==0?entry[1].Integer:captureOverride);
                    actions[count++]=GameplayAction.ApplyStatus(c.Owner.GameplayTarget(Target.Id),key,11,entry[2].Integer,snapshot);
                }
                if(count!=0)Assert.That(c.SubmitGameplay(c.Owner.GameplayTarget(Source.Id),ReadOnlySpan<GameplayCost>.Empty,
                    actions.Slice(0,count)),Is.EqualTo(GameplayFailure.None));
            }
            public void SaveState(SimStateWriter w){w.String(item["name"].Text);}
            public void LoadState(SimStateReader r){SimStateReader.Require(r.String()==item["name"].Text,"S2_LA_SCRIPT");}
            public void WriteState(StateHash h){}
        }
        private static void Verify(string name)
        {
            VectorJson item=Find(name);Simulation sim=null;
            Assert.DoesNotThrow(()=>sim=Create(item),name+" legal admission capture may precede application");
            var document=new M0c2S2ProtocolTests.Document(sim);VectorJson rows=item["rows"];
            Assert.That(document.Tables[6].Count,Is.EqualTo(rows.Count),name+" row count");
            for(int i=0;i<rows.Count;i++)
            {
                Assert.That(document.Tables[6][i],Is.EqualTo(Hex(rows[i]["bytes"].Text)),name+" independent bytes");
                GameplayStatusView row=sim.GameplayStatusAt(i);
                if(!rows[i]["last_application"].IsNull)
                    Assert.That(row.ExpiryTick-item["duration_ticks"].Integer,Is.EqualTo(rows[i]["last_application"].Integer),name+" LA");
            }
            ulong expectedLeafSum=0;
            for(int i=0;i<rows.Count;i++)expectedLeafSum=unchecked(expectedLeafSum+Convert.ToUInt64(rows[i]["leaf"].Text,16));
            var hashWriter=new SimStateWriter(sim.Entities,materialize:true);sim.Entities.Gameplay.WriteHash(hashWriter);
            Assert.That(M0c2S2ProtocolTests.Word(hashWriter.ToArray(),200),Is.EqualTo(expectedLeafSum),name+" independent status leaf sum");
            Simulation restored=Create(item,false);
            Assert.DoesNotThrow(()=>restored.RestoreRetainedSnapshot(sim.SaveRetainedSnapshot(),true),name+" restore");
            Assert.That(restored.StateHashRetained(),Is.EqualTo(sim.StateHashRetained()),name+" restored hash");
            if(sim.Clock.Tick<long.MaxValue-20)
                for(int tick=0;tick<12;tick++)
                {sim.Step(default);restored.Step(default);Assert.That(restored.StateHashRetained(),Is.EqualTo(sim.StateHashRetained()),name+" continue "+tick);}
        }
        [TestCase("delayed-new"),TestCase("refresh-latest"),TestCase("stack-latest"),
         TestCase("strongest-ignore"),TestCase("strongest-replace"),TestCase("independent"),
         TestCase("infinite-refresh"),TestCase("infinite-stack"),TestCase("infinite-stack-delayed-same-tick"),
         TestCase("infinite-delayed-0"),TestCase("infinite-delayed-1"),TestCase("infinite-delayed-2"),TestCase("infinite-delayed-3"),
         TestCase("saturated-new"),TestCase("saturated-refresh"),TestCase("saturated-stack"),TestCase("saturated-strongest-ignore")]
        public void IndependentLastApplicationRowsAndContinuation(string name){Verify(name);}
        [Test,Category("S2LAEquality")] public void DelayedCaptureIsAdmittedAndExpiryStartsAtApplication(){Verify("delayed-new");}
        [Test,Category("S2LAStart")] public void RefreshPreservesStartAndPulseWhileLastApplicationAdvances(){Verify("refresh-latest");}
        [Test,Category("S2LAInfinite")] public void InfiniteDelayedAndRefreshedRowsUseTheExplicitBranch(){Verify("infinite-delayed-0");Verify("infinite-refresh");}
        [Test,Category("S2LASaturated")] public void SaturatedExpiryDoesNotInventAnEarlierLastApplication(){Verify("saturated-new");Verify("saturated-refresh");}
        [TestCase(-1),TestCase(5)] public void InvalidAdmissionCaptureIsRejected(long captured)
        {
            var item=Find("delayed-new");Simulation sim=Create(item,false,captured);
            SimBoundaryException failure=Assert.Throws<SimBoundaryException>(()=>{for(int i=0;i<4;i++)sim.Step(default);});
            StringAssert.Contains("M0C_SOURCE_SNAPSHOT",failure.Message);Assert.That(sim.GameplayStatusCount,Is.Zero);
        }
        [TestCase("captured-after-la"),TestCase("captured-future"),TestCase("start-after-la"),TestCase("start-future")]
        public void ForgedTimesRejectAfterLeafRecalculation(string fault)
        {
            VectorJson item=Find("delayed-new");Simulation sim=null;Assert.DoesNotThrow(()=>sim=Create(item));sim.Step(default);
            var document=new M0c2S2ProtocolTests.Document(sim);byte[] row=document.Tables[6][0];
            if(fault=="captured-after-la")M0c2S2ProtocolTests.Put(row,220,5);
            else if(fault=="captured-future")M0c2S2ProtocolTests.Put(row,220,6);
            else M0c2S2ProtocolTests.Put(row,147,fault=="start-after-la"?5UL:6UL);
            Simulation restored=Create(item,false);
            Assert.Throws<SimSnapshotException>(()=>restored.RestoreRetainedSnapshot(document.Encode(),true),fault);
        }
        [TestCase("infinite-refresh",true),TestCase("infinite-refresh",false),
         TestCase("saturated-new",true),TestCase("saturated-new",false)]
        public void InfiniteAndSaturatedRowsRejectFutureCaptureAndStart(string name,bool captured)
        {
            VectorJson item=Find(name);Simulation sim=null;Assert.DoesNotThrow(()=>sim=Create(item));
            var document=new M0c2S2ProtocolTests.Document(sim);byte[] row=document.Tables[6][0];
            M0c2S2ProtocolTests.Put(row,captured?220:147,(ulong)(sim.Clock.Tick+1));
            Simulation restored=Create(item,false);
            Assert.Throws<SimSnapshotException>(()=>restored.RestoreRetainedSnapshot(document.Encode(),true));
        }
        [Test] public void SaturatedRowsRequireCapturedNotBeforeStart()
        {
            VectorJson item=Find("saturated-new");Simulation sim=null;Assert.DoesNotThrow(()=>sim=Create(item));
            var document=new M0c2S2ProtocolTests.Document(sim);
            M0c2S2ProtocolTests.Put(document.Tables[6][0],220,(ulong)(sim.Clock.Tick-1));
            Simulation restored=Create(item,false);
            Assert.Throws<SimSnapshotException>(()=>restored.RestoreRetainedSnapshot(document.Encode(),true));
        }
        [Test,Category("S2LASaturationAdmission")] public void SaturatedDelayedNewApplicationRejectsBeforeItCreatesAnUnreadableRow()
        {
            VectorJson item=Find("saturated-new");Simulation sim=Create(item,false,long.MaxValue-5);
            SeedBoundary(sim,item["first_tick"].Integer-1);
            var failure=Assert.Throws<SimBoundaryException>(()=>sim.Step(default));
            StringAssert.Contains("M0C_STATUS_SATURATED_CAPTURE",failure.Message);
            Assert.That(sim.GameplayStatusCount,Is.Zero);Assert.That(sim.Scheduler.PendingCount,Is.Zero);
        }
        private static void SeedBoundary(Simulation sim,long prior)
        {
            sim.Clock.Restore(prior);
            typeof(SimScheduler).GetField("advancedThrough",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(sim.Scheduler,prior);
            typeof(SimEventBuffer).GetField("currentTick",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(sim.Events,prior);
        }
        private sealed class SaturationDispelScript:IRetainedGameplaySystem
        {
            private readonly long first;internal SaturationDispelScript(long first){this.first=first;}
            public int Order=>1;public string StateTypeId=>"test.s2.la-dispel";public int StateVersion=>1;
            public void Step(SimContext c)
            {
                if(c.Clock.Tick!=first && c.Clock.Tick!=first+2)return;
                Span<GameplayAction> actions=stackalloc GameplayAction[2];int count=0;
                if(c.Clock.Tick!=first)actions[count++]=GameplayAction.Dispel(c.Owner.GameplayTarget(Target.Id),
                    new GameplayApplyKey(Source.Id,(ulong)c.Clock.Tick,0,Source.Id,(ulong)c.Clock.Tick,1),1);
                actions[count++]=GameplayAction.ApplyStatus(c.Owner.GameplayTarget(Target.Id),
                    new GameplayApplyKey(Source.Id,(ulong)c.Clock.Tick,0,Source.Id,(ulong)c.Clock.Tick,2),11,5,
                    new GameplaySourceSnapshot(c.Owner.GameplayTarget(Source.Id),first));
                Assert.That(c.SubmitGameplay(c.Owner.GameplayTarget(Source.Id),ReadOnlySpan<GameplayCost>.Empty,actions.Slice(0,count)),Is.EqualTo(GameplayFailure.None));
            }
            public void SaveState(SimStateWriter w){}public void LoadState(SimStateReader r){}public void WriteState(StateHash h){}
        }
        [Test,Category("S2LASaturationAdmission")] public void SaturatedDispelThenReapplyChecksTheActualNewStartBeforeWriting()
        {
            VectorJson item=Find("saturated-refresh");long first=item["first_tick"].Integer;
            Simulation sim=Create(item,false,script:new SaturationDispelScript(first));SeedBoundary(sim,first-1);
            sim.Step(default);sim.Step(default);
            var failure=Assert.Throws<SimBoundaryException>(()=>sim.Step(default));
            StringAssert.Contains("M0C_STATUS_SATURATED_CAPTURE",failure.Message);Assert.That(sim.GameplayStatusCount,Is.Zero);
        }
        [Test,Category("S2LAFuture")] public void FutureLastApplicationRejectsEvenWithMatchingExpiryTask()
        {
            VectorJson item=Find("refresh-latest");Simulation sim=null;Assert.DoesNotThrow(()=>sim=Create(item));
            var before=new M0c2S2ProtocolTests.Document(sim);byte[] row=before.Tables[6][0];int keyAt=324;
            var old=new TaskKey((long)M0c2S2ProtocolTests.Word(row,keyAt),M0c2S2ProtocolTests.Word(row,keyAt+8),
                new StableId(M0c2S2ProtocolTests.Word(row,keyAt+16)),M0c2S2ProtocolTests.Word(row,keyAt+24),M0c2S2ProtocolTests.Word(row,keyAt+32));
            Assert.That(sim.Scheduler.CancelStable(old),Is.True);
            TaskKey replacement=sim.Scheduler.ScheduleFor(15,old.OriginDomain,old.Origin,old.Kind,
                new StableSimEvent(GameplayEvents.StatusExpiry,target:new EntityLink(Target.Id)));
            var document=new M0c2S2ProtocolTests.Document(sim);row=document.Tables[6][0];
            M0c2S2ProtocolTests.Put(row,163,15);M0c2S2ProtocolTests.Put(row,keyAt,15);M0c2S2ProtocolTests.Put(row,keyAt+24,replacement.Counter);
            Simulation restored=Create(item,false);
            var failure=Assert.Throws<SimSnapshotException>(()=>restored.RestoreRetainedSnapshot(document.Encode(),true));
            StringAssert.Contains("M0C_STATUS_EXPIRY_REACHABILITY",failure.Message);
        }
    }
}
