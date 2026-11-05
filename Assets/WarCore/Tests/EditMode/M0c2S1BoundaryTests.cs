using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Sim;
using Simulation=WarCore.Sim.Sim;

namespace WarCore.Tests
{
    [TestFixture,Category("M0c2S1"),Category("M0c2S1Boundary")]
    public sealed class M0c2S1BoundaryTests
    {
        [Test]
        public void SavingAfterOutsideTickDestructionCleansResourcesAndRegeneration()
        {
            Simulation sim=M0c2S1Fixture.Create();sim.Step(default);
            Assert.That(sim.Scheduler.PendingCount,Is.EqualTo(3));
            sim.Entities.Destroy(sim.Entities.Resolve(new EntityLink(M0c2S1Fixture.SourceA.Id)));
            Assert.Throws<SimBoundaryException>(() => sim.StateHashRetained());
            byte[] snapshot=sim.SaveRetainedSnapshot();
            Assert.That(sim.Scheduler.PendingCount,Is.EqualTo(2));
            Assert.Throws<SimBoundaryException>(() => sim.GameplayResource(M0c2S1Fixture.SourceA.Id,2));
            Simulation restored=M0c2S1Fixture.Create(reverse:true);restored.RestoreRetainedSnapshot(snapshot,true);
            Assert.That(restored.StateHashRetained(),Is.EqualTo(sim.StateHashRetained()));
            for(int tick=0;tick<6;tick++)
            {
                sim.Step(default);restored.Step(default);
                Assert.That(restored.StateHashRetained(),Is.EqualTo(sim.StateHashRetained()));
            }
        }

        [Test]
        public void CheckpointDoesNotChangeNextTickAfterOutsideTickDeathBeforeRegeneration()
        {
            Simulation uninterrupted=M0c2S1Fixture.Create(),checkpoint=M0c2S1Fixture.Create();
            for(int tick=0;tick<3;tick++){uninterrupted.Step(default);checkpoint.Step(default);}
            uninterrupted.Entities.Destroy(uninterrupted.Entities.Resolve(new EntityLink(M0c2S1Fixture.SourceA.Id)));
            checkpoint.Entities.Destroy(checkpoint.Entities.Resolve(new EntityLink(M0c2S1Fixture.SourceA.Id)));
            byte[] snapshot=checkpoint.SaveRetainedSnapshot();
            Simulation resumed=M0c2S1Fixture.Create(reverse:true);resumed.RestoreRetainedSnapshot(snapshot,true);
            uninterrupted.Step(default);resumed.Step(default);
            Assert.That(uninterrupted.GameplayResource(M0c2S1Fixture.Target.Id,2),
                Is.EqualTo(resumed.GameplayResource(M0c2S1Fixture.Target.Id,2)));
            Assert.That(uninterrupted.Scheduler.PendingCount,Is.EqualTo(resumed.Scheduler.PendingCount));
            Assert.That(uninterrupted.StateHashRetained(),Is.EqualTo(resumed.StateHashRetained()),
                "Uninterrupted tick 4 events="+uninterrupted.Events.Read(4).Length+
                "; saved/restored tick 4 events="+resumed.Events.Read(4).Length+
                "; taking a checkpoint must not change the next tick's authority.");
        }

        [TestCase(false),TestCase(true)]
        public void RuntimeBindingReplacementOrRemovalRejectsTheNextStep(bool remove)
        {
            Simulation sim=M0c2S1Fixture.Create();sim.Step(default);
            ComponentType<GameplayBinding> type=sim.Entities.Resolve<GameplayBinding>();
            EntityId target=sim.Entities.Resolve(new EntityLink(M0c2S1Fixture.Target.Id));
            if(remove)sim.Entities.Remove(type,target);
            else sim.Entities.Set(type,target,new GameplayBinding(999));
            SimBoundaryException failure=Assert.Throws<SimBoundaryException>(() => sim.Step(default));
            Assert.That(failure.Message,Does.Contain("BINDING"));
            Assert.That(sim.GameplayResource(M0c2S1Fixture.Target.Id,1),Is.EqualTo(80*M0c2S1Fixture.S));
            Assert.Throws<InvalidOperationException>(() => sim.Step(default));
        }

        [Test]
        public void SnapshotBoundaryRejectsOrphanBindingOnAPendingBirth()
        {
            Simulation sim=M0c2S1Fixture.Create();sim.Step(default);
            EntityId newborn=sim.Entities.Create(StableEntityKey.World(99,sim.Clock.Tick,1));
            sim.Entities.Set(sim.Entities.Resolve<GameplayBinding>(),newborn,new GameplayBinding(1));
            byte[] invalid;
            try { invalid=sim.SaveRetainedSnapshot(); }
            catch(SimBoundaryException) { return; }
            Simulation restored=M0c2S1Fixture.Create(reverse:true);
            SimSnapshotException failure=Assert.Throws<SimSnapshotException>(() => restored.RestoreRetainedSnapshot(invalid,true));
            Assert.Fail("Snapshot boundary returned "+invalid.Length+" bytes that restore rejects: "+failure.Message);
        }

        private sealed class ReleaseThenRetire : IRetainedGameplaySystem
        {
            public int Order=>20;
            public string StateTypeId=>"test.s1.release-then-retire";
            public int StateVersion=>1;
            public void Step(SimContext context)
            {
                if(context.Clock.Tick!=1)return;
                StableId source=M0c2S1Fixture.SourceA.Id;
                var key=new GameplayApplyKey(source,1,0,source,1,0);
                Assert.That(context.Damage(context.Owner.GameplayTarget(M0c2S1Fixture.Target.Id),key,2,7*M0c2S1Fixture.S),
                    Is.EqualTo(GameplayFailure.None));
                context.Entities.Destroy(context.Entities.Resolve(new EntityLink(source)));
            }
            public void SaveState(SimStateWriter writer) { }
            public void LoadState(SimStateReader reader) { }
            public void WriteState(StateHash hash) { }
        }
        private static Simulation WithRetirementScript(bool reverse=false)
        {
            Simulation sim=M0c2S1Fixture.Create(reverse:reverse);
            sim.RegisterGameplay("s1.release-then-retire",new ReleaseThenRetire(),SimSystemPhase.Queries);
            return sim;
        }
        [Test]
        public void ReleasedDamageRetainsCapturedAmountAfterItsSourceRetires()
        {
            Simulation sim=WithRetirementScript();sim.Step(default);
            Assert.That(sim.Entities.TryResolve(M0c2S1Fixture.SourceA.Id,out _),Is.False);
            Assert.That(sim.GameplayResource(M0c2S1Fixture.Target.Id,1),Is.EqualTo(73*M0c2S1Fixture.S));
            StableSimEvent damage=sim.ReadStableEvent(1,0);
            Assert.That(damage.Type,Is.EqualTo(GameplayEvents.Damage));
            Assert.That(damage.Source.Id,Is.EqualTo(M0c2S1Fixture.SourceA.Id));
            Assert.That(damage.Payload.Magnitude.Raw,Is.EqualTo(7*M0c2S1Fixture.S));
            Assert.That(sim.Scheduler.PendingCount,Is.EqualTo(2));
            byte[] snapshot=sim.SaveRetainedSnapshot();Simulation restored=WithRetirementScript(true);
            restored.RestoreRetainedSnapshot(snapshot,true);
            Assert.That(restored.StateHashRetained(),Is.EqualTo(sim.StateHashRetained()));
            sim.Step(default);restored.Step(default);
            Assert.That(restored.StateHashRetained(),Is.EqualTo(sim.StateHashRetained()));
        }

        // This decoder only locates canonical rows. The mutated row sums below are
        // independently rebuilt so rejection cannot be credited to a stale checksum.
        private sealed class GameplayDocument
        {
            internal readonly byte[] Prefix,Header;
            internal readonly List<byte[]>[] Tables=new List<byte[]>[9];
            internal GameplayDocument(Simulation sim)
            {
                byte[] snapshot=sim.SaveRetainedSnapshot();
                var sectionWriter=new SimStateWriter(null,materialize:true);sim.Entities.Gameplay.Save(sectionWriter);
                int start=snapshot.Length-sectionWriter.ToArray().Length;
                Prefix=Slice(snapshot,0,start);Header=Slice(snapshot,start+8,32);
                int at=start+40;
                for(int table=0;table<9;table++)
                {
                    Assert.That(ReadWord(snapshot,at),Is.EqualTo((ulong)table+1));at+=8;
                    int count=(int)ReadWord(snapshot,at);at+=8;Tables[table]=new List<byte[]>();
                    for(int row=0;row<count;row++)
                    {
                        int length=(int)ReadWord(snapshot,at);at+=8;
                        Tables[table].Add(Slice(snapshot,at,length));at+=length;
                    }
                    at+=16;
                }
                Assert.That(at,Is.EqualTo(snapshot.Length));
            }
            internal byte[] Encode(bool extraSectionByte=false)
            {
                var body=new List<byte>(Header);
                for(int table=0;table<9;table++)
                {
                    ulong tag=(ulong)table+1,sum=0;List<byte[]> rows=Tables[table];
                    AppendWord(body,tag);AppendWord(body,(ulong)rows.Count);
                    foreach(byte[] row in rows)
                    {
                        AppendWord(body,(ulong)row.Length);body.AddRange(row);
                        sum=unchecked(sum+Leaf(tag,row));
                    }
                    AppendWord(body,(ulong)rows.Count);AppendWord(body,sum);
                }
                if(extraSectionByte)body.Add(0);
                var all=new List<byte>(Prefix);AppendWord(all,(ulong)body.Count);all.AddRange(body);return all.ToArray();
            }
        }
        private static byte[] Slice(byte[] input,int offset,int count)
        { var output=new byte[count];Array.Copy(input,offset,output,0,count);return output; }
        private static ulong ReadWord(byte[] bytes,int offset)
        {
            ulong value=0;for(int i=0;i<8;i++)value|=(ulong)bytes[offset+i]<<(8*i);return value;
        }
        private static void WriteWord(byte[] bytes,int offset,ulong value)
        { for(int i=0;i<8;i++)bytes[offset+i]=unchecked((byte)(value>>(8*i))); }
        private static void AppendWord(List<byte> bytes,ulong value)
        { for(int i=0;i<8;i++)bytes.Add(unchecked((byte)(value>>(8*i)))); }
        private static ulong Leaf(ulong tag,byte[] row)
        {
            ulong hash=14695981039346656037UL;
            for(int i=0;i<8;i++)hash=unchecked((hash^((tag>>(8*i))&255))*1099511628211UL);
            foreach(byte value in row)hash=unchecked((hash^value)*1099511628211UL);
            hash=unchecked((hash^(hash>>30))*0xBF58476D1CE4E5B9UL);
            hash=unchecked((hash^(hash>>27))*0x94D049BB133111EBUL);
            return hash^(hash>>31);
        }
        private static void AssertRejected(byte[] snapshot)
        {
            Simulation restored=M0c2S1Fixture.Create(reverse:true);
            Assert.Throws<SimSnapshotException>(() => restored.RestoreRetainedSnapshot(snapshot,true));
            Assert.Throws<InvalidOperationException>(() => restored.Step(default));
        }

        [Test]
        public void SnapshotWithRecomputedLeavesStillRejectsAnOrphanBinding()
        {
            var document=new GameplayDocument(M0c2S1Fixture.Create());
            ulong removed=ReadWord(document.Tables[2][0],1);document.Tables[2].RemoveAt(0);
            document.Tables[3].RemoveAll(row => ReadWord(row,1)==removed);
            AssertRejected(document.Encode());
        }

        [TestCase("owner-generation"),TestCase("owner-archetype"),TestCase("resource-generation"),
         TestCase("resource-key"),TestCase("resource-value"),TestCase("owner-order"),TestCase("resource-order"),
         TestCase("row-trailing-byte"),TestCase("section-trailing-byte")]
        public void InvalidGameplayRowsRejectEvenWithCorrectRecomputedLeaves(string fault)
        {
            var document=new GameplayDocument(M0c2S1Fixture.Create());
            switch(fault)
            {
                case "owner-generation":WriteWord(document.Tables[2][0],9,65536);break;
                case "owner-archetype":WriteWord(document.Tables[2][0],17,999);break;
                case "resource-generation":WriteWord(document.Tables[3][0],9,1);break;
                case "resource-key":WriteWord(document.Tables[3][0],17,999);break;
                case "resource-value":WriteWord(document.Tables[3][0],25,140737488355327UL);break;
                case "owner-order":document.Tables[2].Reverse();break;
                case "resource-order":document.Tables[3].Reverse();break;
                case "row-trailing-byte":
                    byte[] expanded=new byte[document.Tables[3][0].Length+1];
                    Array.Copy(document.Tables[3][0],expanded,expanded.Length-1);document.Tables[3][0]=expanded;break;
            }
            AssertRejected(document.Encode(fault=="section-trailing-byte"));
        }

        [TestCase("due"),TestCase("origin-domain"),TestCase("counter"),TestCase("kind")]
        public void InvalidRegenerationKeysRejectWithCorrectResourceLeaf(string field)
        {
            Simulation sim=M0c2S1Fixture.Create();sim.Step(default);
            var document=new GameplayDocument(sim);
            byte[] row=document.Tables[3].Find(resource => resource[33]!=0);
            Assert.That(row,Is.Not.Null);
            int offset=field=="due"?34:field=="origin-domain"?42:field=="counter"?58:66;
            WriteWord(row,offset,field=="due"?1UL:field=="origin-domain"?1UL:999UL);
            AssertRejected(document.Encode());
        }
    }
}
