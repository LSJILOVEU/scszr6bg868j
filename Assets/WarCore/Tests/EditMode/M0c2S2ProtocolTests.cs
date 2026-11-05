using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Sim;
using Simulation=WarCore.Sim.Sim;

namespace WarCore.Tests
{
    [TestFixture,Category("M0c2S2"),Category("M0c2S2Protocol")]
    public sealed class M0c2S2ProtocolTests
    {
        private static readonly StableEntityKey Target=StableEntityKey.Placed(37,1);
        private static readonly StableEntityKey Source=StableEntityKey.Placed(37,2);
        private static VectorJson Cases=>VectorJson.Load("m0c2-s2-status-v6-v8.json")["cases"];
        private static VectorJson FindCase(string name)
        {VectorJson cases=Cases;for(int i=0;i<cases.Count;i++)if(cases[i]["name"].Text==name)return cases[i];throw new Exception(name);}
        private static byte[] Hex(string text)
        {var bytes=new byte[text.Length/2];for(int i=0;i<bytes.Length;i++)bytes[i]=Convert.ToByte(text.Substring(i*2,2),16);return bytes;}
        private static Simulation Create(VectorJson item,bool reverse=false,bool run=true)
        {
            int policy=(int)item["policy"].Integer,period=(int)item["period"].Integer;
            var definition=new GameplayStatusDef(11,"status",(GameplayStatusDuration)item["duration"].Integer,
                (int)item["duration_ticks"].Integer,period,(int)item["first"].Integer,(GameplayStackPolicy)policy,
                (GameplayStackScope)item["scope"].Integer,policy==1?3:1,policy==2?3:1,
                periodicActions:period==0?null:new[]{GameplayPeriodicAction.ChangeResource(1,7,3)},
                dispelTags:1,immunityTags:(ulong)item["immunity"].Integer,priority:2,absorbResourceKey:item["absorb"].Boolean?(ulong?)7UL:null);
            var schema=new GameplaySchema(new[]{new GameplayResourceDef(7,"resource",0,1000,100)},
                new[]{new GameplayDamageTypeDef(9,"damage",7,10)},new[]{new GameplayPipelineDef(10,new GameplayDamageSpec(0))},
                new[]{new GameplayArchetypeDef(12,new[]{7UL})},new GameplayCapacities(2,2,16,32,statuses:4),
                "s2.vector.critical",new[]{definition});
            Assert.That(schema.RulesDigest.ToString("X16"),Is.EqualTo(item["rules"].Text),item["name"].Text+" rules");
            Assert.That(schema.CapacityDigest.ToString("X16"),Is.EqualTo(item["capacity"].Text),item["name"].Text+" capacities");
            var catalog=new PlacedCatalog(new StableContentManifest(0),new PlacedCatalogEntry(37,1,1),new PlacedCatalogEntry(37,2,1));
            var sim=Simulation.CreateRetainedWorld(91,catalog,new SimConfig(entityCapacity:4,timerCapacity:16),gameplaySchema:schema);
            sim.BindGameplay(sim.Entities.Create(reverse?Source:Target),12);sim.BindGameplay(sim.Entities.Create(reverse?Target:Source),12);
            sim.RegisterGameplay("s2.protocol-script",new ProtocolScript(item),SimSystemPhase.Queries);
            if(run)for(int i=0;i<item["tick"].Integer;i++)sim.Step(default);
            return sim;
        }
        private sealed class ProtocolScript:IRetainedGameplaySystem
        {
            private readonly long[] applications;private readonly bool absorb,retire;
            internal ProtocolScript(VectorJson item){applications=item["applications"].Integers();absorb=item["absorb"].Boolean;retire=item["retire"].Boolean;}
            public int Order=>1;public string StateTypeId=>"test.s2.protocol";public int StateVersion=>1;
            public void Step(SimContext c)
            {
                if(c.Clock.Tick==1)
                {
                    Span<GameplayAction> actions=stackalloc GameplayAction[8];int count=0;
                    for(int i=0;i<applications.Length;i++)
                    {
                        var key=new GameplayApplyKey(Source.Id,1,0,Source.Id,1,(uint)i+1);
                        actions[count++]=absorb?GameplayAction.GrantAbsorb(c.Owner.GameplayTarget(Target.Id),key,11,12)
                            :GameplayAction.ApplyStatus(c.Owner.GameplayTarget(Target.Id),key,11,applications[i]);
                    }
                    if(absorb)actions[count++]=GameplayAction.Damage(c.Owner.GameplayTarget(Target.Id),
                        new GameplayApplyKey(Source.Id,1,0,Source.Id,1,7),9,5);
                    Assert.That(c.SubmitGameplay(c.Owner.GameplayTarget(Source.Id),ReadOnlySpan<GameplayCost>.Empty,actions.Slice(0,count)),Is.EqualTo(GameplayFailure.None));
                }
                if(retire && c.Clock.Tick==2)c.Entities.Destroy(c.Entities.Resolve(new EntityLink(Source.Id)));
            }
            public void SaveState(SimStateWriter w){w.Int32(applications.Length);foreach(long value in applications)w.Int64(value);w.Bool(absorb);w.Bool(retire);}
            public void LoadState(SimStateReader r)
            {SimStateReader.Require(r.Count(8)==applications.Length,"S2_VECTOR_SCRIPT");foreach(long value in applications)SimStateReader.Require(r.Int64()==value,"S2_VECTOR_SCRIPT");SimStateReader.Require(r.Bool()==absorb && r.Bool()==retire,"S2_VECTOR_SCRIPT");}
            public void WriteState(StateHash h){}
        }
        [Test]
        public void IndependentS2CanonicalSectionsCoverPoliciesWeakSourcePulseAbsorbAndImmunity()
        {
            VectorJson cases=Cases;
            for(int i=0;i<cases.Count;i++)
            {
                VectorJson item=cases[i];Simulation sim=Create(item);string name=item["name"].Text;
                byte[] expected=Hex(item["gameplay_snapshot"].Text),snapshot=sim.SaveRetainedSnapshot();
                Assert.That(Slice(snapshot,snapshot.Length-expected.Length,expected.Length),Is.EqualTo(expected),name+" snapshot bytes");
                var writer=new SimStateWriter(sim.Entities,materialize:true);sim.Entities.Gameplay.WriteHash(writer);
                Assert.That(writer.ToArray(),Is.EqualTo(Hex(item["gameplay_hash"].Text)),name+" table leaves");
                Assert.That(sim.GameplayResource(Target.Id,7),Is.EqualTo(item["value"].Integer),name+" resource");
                if(name=="weak-source-reclaimed")
                    Assert.That(sim.Entities.RegisteredIdentityCount,Is.EqualTo(1),"only target remains after the source retention window");
                Simulation restored=Create(item,true,false);restored.RestoreRetainedSnapshot(snapshot,true);
                Assert.That(restored.StateHashRetained(),Is.EqualTo(sim.StateHashRetained()),name+" restore");
                for(int step=0;step<8;step++)
                {sim.Step(default);restored.Step(default);Assert.That(restored.StateHashRetained(),Is.EqualTo(sim.StateHashRetained()),name+" continuation "+step);}
            }
            TestContext.WriteLine("S2 independent canonical vectors: 14 snapshot/hash sections; mismatches=0");
        }
        private static byte[] Slice(byte[] source,int offset,int length)
        {var result=new byte[length];Array.Copy(source,offset,result,0,length);return result;}
        internal static ulong Word(byte[] bytes,int at)
        {ulong result=0;for(int i=0;i<8;i++)result|=(ulong)bytes[at+i]<<(8*i);return result;}
        internal static void Put(byte[] bytes,int at,ulong value)
        {for(int i=0;i<8;i++)bytes[at+i]=unchecked((byte)(value>>(8*i)));}
        private static void Append(List<byte> bytes,ulong value)
        {for(int i=0;i<8;i++)bytes.Add(unchecked((byte)(value>>(8*i))));}
        private static ulong Leaf(ulong tag,byte[] row)
        {
            ulong hash=14695981039346656037UL;
            for(int i=0;i<8;i++)hash=unchecked((hash^((tag>>(8*i))&255))*1099511628211UL);
            foreach(byte value in row)hash=unchecked((hash^value)*1099511628211UL);
            hash=unchecked((hash^(hash>>30))*0xBF58476D1CE4E5B9UL);
            hash=unchecked((hash^(hash>>27))*0x94D049BB133111EBUL);return hash^(hash>>31);
        }
        internal sealed class Document
        {
            internal readonly byte[] Prefix,Header;internal readonly List<byte[]>[] Tables=new List<byte[]>[9];
            internal Document(Simulation sim)
            {
                byte[] snapshot=sim.SaveRetainedSnapshot();var writer=new SimStateWriter(sim.Entities,materialize:true);sim.Entities.Gameplay.Save(writer);
                byte[] section=writer.ToArray();Prefix=Slice(snapshot,0,snapshot.Length-section.Length);Header=Slice(section,8,32);int at=40;
                for(int table=0;table<9;table++)
                {
                    Assert.That(Word(section,at),Is.EqualTo((ulong)table+1));int count=(int)Word(section,at+8);at+=16;Tables[table]=new List<byte[]>();
                    for(int i=0;i<count;i++){int length=(int)Word(section,at);at+=8;Tables[table].Add(Slice(section,at,length));at+=length;}at+=16;
                }
                Assert.That(at,Is.EqualTo(section.Length));
            }
            internal byte[] Encode()
            {
                var body=new List<byte>(Header);
                for(int table=0;table<9;table++)
                {
                    ulong tag=(ulong)table+1,sum=0;Append(body,tag);Append(body,(ulong)Tables[table].Count);
                    foreach(byte[] row in Tables[table]){Append(body,(ulong)row.Length);body.AddRange(row);sum=unchecked(sum+Leaf(tag,row));}
                    Append(body,(ulong)Tables[table].Count);Append(body,sum);
                }
                var result=new List<byte>(Prefix);Append(result,(ulong)body.Count);result.AddRange(body);return result.ToArray();
            }
        }
        [TestCase("effect-cursor"),TestCase("token-duplicate"),TestCase("owner-generation"),TestCase("source-generation"),
         TestCase("group-scope"),TestCase("attribution-target"),TestCase("stacks"),TestCase("strength"),TestCase("duration-kind"),
         TestCase("infinite-expiry"),TestCase("absorb-negative"),TestCase("captured-tick"),TestCase("snapshot-source"),TestCase("attribute-count"),TestCase("row-order"),TestCase("stack-above-cap"),TestCase("unknown-definition"),
         TestCase("source-absent"),TestCase("immune-negative"),TestCase("capacity-negative"),TestCase("row-trailing"),
         TestCase("producer-live-alias"),TestCase("producer-history-alias"),TestCase("producer-cursor")]
        public void MalformedStatusRowsRejectAfterIndependentLeafRecalculation(string fault)
        {
            VectorJson item=FindCase("independent");Simulation original=Create(item);var doc=new Document(original);byte[] row=doc.Tables[6][0];
            switch(fault)
            {
                case "effect-cursor":Put(doc.Tables[0][0],0,0);break;
                case "token-duplicate":Put(doc.Tables[6][1],17,0);break;
                case "owner-generation":Put(row,34,1);break;
                case "source-generation":Put(row,123,65536);break;
                case "group-scope":row[16]=1;break;
                case "attribution-target":Put(row,106,0);break;
                case "stacks":Put(row,131,0);break;
                case "strength":Put(row,139,ulong.MaxValue);break;
                case "duration-kind":Put(row,155,3);break;
                case "infinite-expiry":Put(row,163,2);break;
                case "absorb-negative":Put(row,187,ulong.MaxValue);break;
                case "captured-tick":Put(row,220,2);break;
                case "snapshot-source":Put(row,196,0);break;
                case "attribute-count":Put(row,228,1);break;
                case "row-order":doc.Tables[6].Reverse();break;
                case "stack-above-cap":Put(row,131,2);break;
                case "unknown-definition":Put(row,8,999);Put(row,42,999);break;
                case "source-absent":row[114]=0;break;
                case "immune-negative":Put(doc.Tables[8][0],32,ulong.MaxValue);break;
                case "capacity-negative":Put(doc.Tables[8][0],40,ulong.MaxValue);break;
                case "row-trailing":var extended=new byte[row.Length+1];Array.Copy(row,extended,row.Length);doc.Tables[6][0]=extended;break;
                case "producer-live-alias":Put(row,66,1);Put(row,82,1);break;
                case "producer-history-alias":
                    Put(doc.Tables[0][0],0,3);Put(row,66,1);Put(row,74,Target.Id.Value);Put(row,82,2);
                    Put(doc.Tables[6][1],66,1);Put(doc.Tables[6][1],82,2);break;
                case "producer-cursor":Put(row,66,1);Put(row,82,2);break;
            }
            Simulation restored=Create(item,true,false);
            Assert.Throws<SimSnapshotException>(()=>restored.RestoreRetainedSnapshot(doc.Encode(),true),fault);
            Assert.Throws<InvalidOperationException>(()=>restored.Step(default),fault+" faulted");
        }
        [TestCase("independent"),TestCase("strongest")]
        public void NonrefreshingPoliciesRequireTheWinningCaptureToStartTheInstance(string name)
        {
            VectorJson item=FindCase(name);Simulation original=Create(item);original.Step(default);
            var document=new Document(original);Put(document.Tables[6][0],220,2);
            Simulation restored=Create(item,true,false);
            SimSnapshotException failure=Assert.Throws<SimSnapshotException>(()=>restored.RestoreRetainedSnapshot(document.Encode(),true));
            StringAssert.Contains("M0C_STATUS_START_CAPTURE",failure.Message);
        }
        [TestCase("orphan-tasks"),TestCase("task-count"),TestCase("task-key"),TestCase("action-capture"),TestCase("action-count")]
        public void StatusTaskAndCaptureReferencesRejectAfterLeafRecalculation(string fault)
        {
            VectorJson item=FindCase("periodic-1");Simulation original=Create(item);var document=new Document(original);byte[] row=document.Tables[6][0];
            switch(fault)
            {
                case "orphan-tasks":document.Tables[6].Clear();break;
                case "task-count":Put(row,276,3);break;
                case "task-key":Put(row,308,999);break;
                case "action-capture":Put(row,244,999);break;
                case "action-count":Put(row,236,0);break;
            }
            Simulation restored=Create(item,true,false);
            Assert.Throws<SimSnapshotException>(()=>restored.RestoreRetainedSnapshot(document.Encode(),true),fault);
            Assert.Throws<InvalidOperationException>(()=>restored.Step(default),fault+" faulted");
        }
        [TestCase("expiry"),TestCase("pulse"),TestCase("pulse-history")]
        public void UnreachableTimesRejectWithMatchingSchedulerAndRecomputedLeaves(string fault)
        {
            VectorJson item=FindCase("periodic-1");Simulation original=Create(item);var before=new Document(original);byte[] row=before.Tables[6][0];
            bool expiry=fault=="expiry";int keyAt=expiry?324:284;
            var previous=new TaskKey((long)Word(row,keyAt),Word(row,keyAt+8),new StableId(Word(row,keyAt+16)),Word(row,keyAt+24),Word(row,keyAt+32));
            long due=expiry?7:fault=="pulse"?3:4;
            Assert.That(original.Scheduler.CancelStable(previous),Is.True);
            TaskKey replacement=original.Scheduler.ScheduleFor(due,previous.OriginDomain,previous.Origin,previous.Kind,
                new StableSimEvent(expiry?GameplayEvents.StatusExpiry:GameplayEvents.StatusPulse,target:new EntityLink(Target.Id)));
            var document=new Document(original);row=document.Tables[6][0];Put(row,expiry?163:171,(ulong)due);Put(row,keyAt,(ulong)due);Put(row,keyAt+24,replacement.Counter);
            if(fault=="pulse-history")Put(row,179,1);
            Simulation restored=Create(item,true,false);
            SimSnapshotException failure=Assert.Throws<SimSnapshotException>(()=>restored.RestoreRetainedSnapshot(document.Encode(),true));
            StringAssert.Contains(expiry?"M0C_STATUS_EXPIRY_REACHABILITY":fault=="pulse"?"M0C_STATUS_PULSE_REACHABILITY":"M0C_STATUS_PULSE_HISTORY",failure.Message);
        }
        private sealed class ResourceDependencyScript:IRetainedGameplaySystem
        {
            public int Order=>1;public string StateTypeId=>"test.s2.resource-dependency";public int StateVersion=>1;
            public void Step(SimContext c)
            {
                if(c.Clock.Tick==1)c.ApplyStatus(c.Owner.GameplayTarget(Target.Id),
                    new GameplayApplyKey(Source.Id,1,0,Source.Id,1,1),1);
            }
            public void SaveState(SimStateWriter w){}public void LoadState(SimStateReader r){}public void WriteState(StateHash h){}
        }
        private static Simulation ResourceDependencyWorld(bool periodic)
        {
            var valid=new GameplayStatusDef(1,"valid",GameplayStatusDuration.Infinite,
                periodTicks:periodic?2:0,firstPulseDelayTicks:periodic?2:0,
                periodicActions:periodic?new[]{GameplayPeriodicAction.ChangeResource(1,7,3)}:null);
            var invalidOwner=new GameplayStatusDef(2,"owner-missing-resource",GameplayStatusDuration.Infinite,
                periodTicks:periodic?2:0,firstPulseDelayTicks:periodic?2:0,
                periodicActions:periodic?new[]{GameplayPeriodicAction.ChangeResource(1,0,3)}:null,
                absorbResourceKey:periodic?null:(ulong?)0);
            var schema=new GameplaySchema(new[]{new GameplayResourceDef(0,"other",0,1000,100),new GameplayResourceDef(7,"bound",0,1000,100)},
                new[]{new GameplayDamageTypeDef(9,"damage",7,10)},new[]{new GameplayPipelineDef(10,new GameplayDamageSpec(0))},
                new[]{new GameplayArchetypeDef(12,new[]{7UL})},new GameplayCapacities(2,2,16,32,statuses:4),
                "s2.resource-dependency",new[]{valid,invalidOwner});
            var catalog=new PlacedCatalog(new StableContentManifest(0),new PlacedCatalogEntry(37,1,1),new PlacedCatalogEntry(37,2,1));
            var sim=Simulation.CreateRetainedWorld(91,catalog,new SimConfig(entityCapacity:4,timerCapacity:16),gameplaySchema:schema);
            sim.BindGameplay(sim.Entities.Create(Target),12);sim.BindGameplay(sim.Entities.Create(Source),12);
            sim.RegisterGameplay("s2.resource-dependency",new ResourceDependencyScript(),SimSystemPhase.Queries);return sim;
        }
        [TestCase(false),TestCase(true)]
        public void RestoredStatusRequiresItsOwnersAbsorbAndPeriodicResources(bool periodic)
        {
            Simulation original=ResourceDependencyWorld(periodic);original.Step(default);
            var document=new Document(original);byte[] row=document.Tables[6][0];Put(row,8,2);Put(row,42,2);
            if(periodic)Put(row,244,2);
            Simulation restored=ResourceDependencyWorld(periodic);
            SimSnapshotException failure=Assert.Throws<SimSnapshotException>(()=>restored.RestoreRetainedSnapshot(document.Encode(),true));
            StringAssert.Contains("M0C_STATUS_OWNER_RESOURCE",failure.Message);
            Assert.Throws<InvalidOperationException>(()=>restored.Step(default));
        }
        [TestCase(false),TestCase(true)]
        public void WeakSourceGenerationsCannotReferToAFutureOrNonPlacedLife(bool nonPlaced)
        {
            VectorJson item=FindCase("independent");Simulation original=Create(item);var document=new Document(original);
            byte[] row=document.Tables[6][0];Put(row,123,1);Put(row,204,1);
            if(nonPlaced)
            {
                Assert.That(original.Placed.Catalog.Find(new StableId(0)),Is.EqualTo(-1));
                Put(row,50,0);Put(row,115,0);Put(row,196,0);
            }
            Simulation restored=Create(item,true,false);
            SimSnapshotException failure=Assert.Throws<SimSnapshotException>(()=>restored.RestoreRetainedSnapshot(document.Encode(),true));
            StringAssert.Contains("M0C_STATUS_SOURCE_GENERATION",failure.Message);
            Assert.Throws<InvalidOperationException>(()=>restored.Step(default));
        }
    }
}
