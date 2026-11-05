using System;
using System.Globalization;
using NUnit.Framework;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    [TestFixture, Category("M0c1"), Category("M0c1Mutation")]
    public sealed class M0c1ProtocolTests
    {
        internal static byte[] Tail(Simulation sim)
        {var writer=new SimStateWriter(sim.Entities,materialize:true);sim.Placed.Save(writer);return writer.ToArray();}
        internal static byte[] ReplaceTail(Simulation sim,byte[] tail)
        {
            byte[] snapshot=sim.SaveRetainedSnapshot();int start=snapshot.Length-Tail(sim).Length;
            byte[] result=new byte[start+tail.Length];Array.Copy(snapshot,result,start);Array.Copy(tail,0,result,start,tail.Length);return result;
        }
        internal static void Put(byte[] bytes,int offset,ulong value)
        {for(int b=0;b<8;b++)bytes[offset+b]=unchecked((byte)(value>>(8*b)));}
        internal static byte[] Hex(string value)
        {var bytes=new byte[value.Length/2];for(int i=0;i<bytes.Length;i++)bytes[i]=byte.Parse(value.Substring(i*2,2),NumberStyles.HexNumber);return bytes;}
        private static ulong IndexLeaf(ulong index)
        {
            ulong h=14695981039346656037UL;
            foreach(ulong word in new[]{1UL,index,1UL})
                for(int b=0;b<8;b++)h=unchecked((h^((word>>(8*b))&255))*1099511628211UL);
            h=unchecked((h^(h>>30))*0xBF58476D1CE4E5B9UL);h=unchecked((h^(h>>27))*0x94D049BB133111EBUL);return h^(h>>31);
        }
        [Test]
        public void IndependentFullV5V7VectorsMatch()
        {
            VectorJson cases=VectorJson.Load("m0c1-placed-v5-v7.json")["cases"];
            for(int row=0;row<cases.Count;row++)
            {
                var item=cases[row];var entries=new PlacedCatalogEntry[item["entries"].Count];
                for(int i=0;i<entries.Length;i++)entries[i]=new PlacedCatalogEntry((ulong)item["entries"][i][0].Integer,(ulong)item["entries"][i][1].Integer,(byte)item["entries"][i][2].Integer);
                var content=new StableContentManifest(0);var catalog=new PlacedCatalog(content,entries);
                Assert.That(catalog.Digest.ToString("X16"),Is.EqualTo(item["catalog"].Text));
                var destroyed=new int[item["destroyed"].Count];for(int i=0;i<destroyed.Length;i++)destroyed[i]=(int)item["destroyed"][i].Integer;
                var gens=new PlacedGeneration[item["generations"].Count];for(int i=0;i<gens.Length;i++)gens[i]=new PlacedGeneration((int)item["generations"][i][0].Integer,(ushort)item["generations"][i][1].Integer);
                var sim=Simulation.CreateRetainedWorld(123,catalog,initial:new InitialPlacedState(catalog.Digest,destroyed,gens));
                Assert.That(sim.StateHashRetained().ToString("X16"),Is.EqualTo(item["hash"].Text),item["name"].Text);
                Assert.That(sim.SaveRetainedSnapshot(),Is.EqualTo(Hex(item["snapshot"].Text)),item["name"].Text);
                Assert.That(Tail(sim),Is.EqualTo(Hex(item["tail"].Text)));
                var writer=new SimStateWriter(sim.Entities,materialize:true);sim.Placed.WriteHash(writer);
                Assert.That(writer.ToArray(),Is.EqualTo(Hex(item["hash_tail"].Text)));
                sim.RestoreRetainedSnapshot(Hex(item["snapshot"].Text),true);
                Assert.That(sim.StateHashRetained().ToString("X16"),Is.EqualTo(item["hash"].Text));
            }
        }
        [Test]
        public void CatalogOrderPolicyAndInitialInputAreBoundAndCopied()
        {
            var content=new StableContentManifest(0);
            var a=new PlacedCatalogEntry(7,11,1);var b=new PlacedCatalogEntry(7,12);
            var catalog=new PlacedCatalog(content,a,b);Assert.That(new PlacedCatalog(content,b,a).Digest,Is.EqualTo(catalog.Digest));
            Assert.That(new PlacedCatalog(content,new PlacedCatalogEntry(7,11),b).Digest,Is.Not.EqualTo(catalog.Digest));
            Assert.Throws<SimBoundaryException>(()=>new PlacedCatalog(content,a,a));
            Assert.Throws<SimBoundaryException>(()=>new PlacedCatalog(content,default(PlacedCatalogEntry)));
            Assert.Throws<SimBoundaryException>(()=>new PlacedCatalogEntry(1,1,2));
            Assert.Throws<SimBoundaryException>(()=>catalog.Index(StableEntityKey.Placed(8,11)));
            Assert.Throws<SimBoundaryException>(()=>Simulation.CreateRetainedWorld(1,(PlacedCatalog)null));
            Assert.Throws<SimBoundaryException>(()=>Simulation.CreateRetainedWorld(1,catalog,initial:new InitialPlacedState(catalog.Digest^1,new int[0])));
            Assert.Throws<SimBoundaryException>(()=>Simulation.CreateRetainedWorld(1,catalog,initial:new InitialPlacedState(catalog.Digest,new[]{1,0})));
            Assert.Throws<SimBoundaryException>(()=>Simulation.CreateRetainedWorld(1,catalog,initial:new InitialPlacedState(catalog.Digest,new[]{0,0})));
            Assert.Throws<SimBoundaryException>(()=>Simulation.CreateRetainedWorld(1,catalog,initial:new InitialPlacedState(catalog.Digest,new[]{2})));
            int index=catalog.Index(a.Key);var values=new[]{index};var input=new InitialPlacedState(catalog.Digest,values);values[0]=2;
            var sim=Simulation.CreateRetainedWorld(1,catalog,initial:input);
            Assert.That(sim.Placed.IsDestroyed(a.Key),Is.True);
            Assert.Throws<SimBoundaryException>(()=>sim.Entities.Create(a.Key));
        }
        [TestCase(0),TestCase(1),TestCase(2),TestCase(3),TestCase(4),TestCase(5)]
        public void MalformedSparseSnapshotIsRejected(int corruption)
        {
            var baseSim=M0c1Fixture.Create(out _,count:10);
            var input=new InitialPlacedState(baseSim.Placed.Catalog.Digest,new[]{0,7,8},new[]{new PlacedGeneration(1,1),new PlacedGeneration(9,2)});
            var sim=M0c1Fixture.Create(out _,initial:input,count:10);byte[] tail=Tail(sim);
            // Header: 48 bytes; count at 48; indices at 56/64/72; genCount at 80.
            if(corruption==0)Put(tail,64,0); // duplicate
            if(corruption==1){Put(tail,56,7);Put(tail,64,0);} // descending
            if(corruption==2){Put(tail,72,10);Put(tail,32,unchecked(IndexLeaf(0)+IndexLeaf(7)+IndexLeaf(10)));} // valid checksum, out of range
            if(corruption==3)Put(tail,48,11); // oversized count
            if(corruption==4)Put(tail,104,1); // repeated generation index
            if(corruption==5)Put(tail,96,65536); // generation overflow
            var target=M0c1Fixture.Create(out _,count:10);
            Assert.Throws<SimSnapshotException>(()=>target.RestoreRetainedSnapshot(ReplaceTail(sim,tail)));
        }
        [Test]
        public void SnapshotCatalogMismatchTruncationAndOldVersionsFail()
        {
            var sim=M0c1Fixture.Create(out _);byte[] tail=Tail(sim);tail[16]^=1;
            var target=M0c1Fixture.Create(out _);
            Assert.Throws<SimSnapshotException>(()=>target.RestoreRetainedSnapshot(ReplaceTail(sim,tail)));
            byte[] snapshot=sim.SaveRetainedSnapshot();Array.Resize(ref snapshot,snapshot.Length-1);
            Assert.Throws<SimSnapshotException>(()=>target.RestoreRetainedSnapshot(snapshot));
            foreach(int version in new[]{3,4,5,6,8})
            {byte[] wrong=sim.SaveRetainedSnapshot();Put(wrong,8,(ulong)version);Assert.That(Assert.Throws<SimSnapshotException>(()=>target.RestoreRetainedSnapshot(wrong)).Message,Does.Contain("M0C_SNAPSHOT_VERSION"));}
        }
        [Test]
        public void RollbackReplacesBitsAndGenerationRatherThanMerging()
        {
            var sim=M0c1Fixture.Create(out var system,0);byte[] initial=sim.SaveRetainedSnapshot();ulong initialHash=sim.StateHashRetained();
            sim.Entities.Destroy(sim.Entities.Create(M0c1Fixture.Parent));sim.Step(default);
            ulong destroyedHash=sim.StateHashRetained();byte[] destroyed=sim.SaveRetainedSnapshot();
            system.Restore=true;sim.Step(default);
            Assert.That(sim.Placed.DestroyedCount,Is.Zero);Assert.That(sim.Placed.DestroyedDigest,Is.Zero);
            Assert.That(sim.Placed.GenerationCount,Is.EqualTo(1));Assert.That(sim.Placed.GenerationDigest,Is.Not.Zero);
            byte[] restored=sim.SaveRetainedSnapshot();ulong restoredHash=sim.StateHashRetained();
            Assert.DoesNotThrow(()=>sim.RestoreRetainedSnapshot(initial,true));Assert.That(sim.StateHashRetained(),Is.EqualTo(initialHash));
            Assert.That(sim.Placed.GenerationCount,Is.Zero);Assert.That(sim.Placed.GenerationDigest,Is.Zero);
            sim.RestoreRetainedSnapshot(destroyed);Assert.That(sim.StateHashRetained(),Is.EqualTo(destroyedHash));
            sim.RestoreRetainedSnapshot(restored);Assert.That(sim.StateHashRetained(),Is.EqualTo(restoredHash));
        }
        [Test]
        public void OnlyBitmapOrGenerationDifferencesChangeStateHash()
        {
            var original=M0c1Fixture.Create(out _);var catalog=original.Placed.Catalog;
            var destroyed=M0c1Fixture.Create(out _,initial:new InitialPlacedState(catalog.Digest,new[]{0}));
            var generation=M0c1Fixture.Create(out _,initial:new InitialPlacedState(catalog.Digest,new int[0],new[]{new PlacedGeneration(0,1)}));
            Assert.That(destroyed.StateHashRetained(),Is.Not.EqualTo(original.StateHashRetained()));
            Assert.That(generation.StateHashRetained(),Is.Not.EqualTo(original.StateHashRetained()));
        }
        [TestCase(false),TestCase(true)]
        public void InconsistentLifeBitsFailEvenWithMatchingSparseDigest(bool retired)
        {
            var sim=M0c1Fixture.Create(out _,32);EntityId entity=sim.Entities.Create(M0c1Fixture.Parent);
            if(retired)sim.Entities.Destroy(entity);
            var other=M0c1Fixture.Create(out _,32,initial:retired?null:new InitialPlacedState(sim.Placed.Catalog.Digest,new[]{0}));
            byte[] bad=ReplaceTail(sim,Tail(other));var target=M0c1Fixture.Create(out _,32);
            Assert.That(Assert.Throws<SimSnapshotException>(()=>target.RestoreRetainedSnapshot(bad)).Message,Does.Contain("M0C_PLACED_LIFE"));
        }
        [Test]
        public void BitmapUsesCatalogIndicesAndLsbOrderAfterSlotReuse()
        {
            var sim=M0c1Fixture.Create(out _,0,count:10);
            foreach(int index in new[]{8,0,7})
            {
                var key=sim.Placed.Catalog.Entry(index).Key;
                sim.Entities.Destroy(sim.Entities.Create(key));sim.Step(default);
                Assert.That(sim.Placed.IsDestroyed(key),Is.True);
            }
            var bits=(byte[])typeof(PlacedDestructionState).GetField("bits",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).GetValue(sim.Placed);
            Assert.That(bits,Is.EqualTo(new byte[]{0x81,0x01}));
            byte[] tail=Tail(sim);Assert.That(BitConverter.ToUInt64(tail,56),Is.EqualTo(0));
            Assert.That(BitConverter.ToUInt64(tail,64),Is.EqualTo(7));Assert.That(BitConverter.ToUInt64(tail,72),Is.EqualTo(8));
        }
        [Test]
        public void GenerationDigestReplacesOldLeafAndRestoresIntoCursors()
        {
            var sim=M0c1Fixture.Create(out var system,0);var key=M0c1Fixture.Parent;
            for(int generation=1;generation<=3;generation++)
            {
                sim.Entities.Destroy(sim.Entities.Create(key));sim.Step(default);system.Restore=true;sim.Step(default);
                var expected=M0c1Fixture.Create(out _,0,initial:new InitialPlacedState(sim.Placed.Catalog.Digest,new int[0],new[]{new PlacedGeneration(0,(ushort)generation)}));
                Assert.That(sim.Placed.GenerationDigest,Is.EqualTo(expected.Placed.GenerationDigest));
                Assert.That(Tail(sim),Is.EqualTo(Tail(expected)));
            }
        }
        [Test]
        public void InitialGenerationTableRejectsZeroDuplicatesPolicyAndRange()
        {
            var sim=M0c1Fixture.Create(out _);var catalog=sim.Placed.Catalog;
            foreach(var table in new[]{new[]{new PlacedGeneration(0,0)},new[]{new PlacedGeneration(-1,1)},new[]{new PlacedGeneration(1,1)},new[]{new PlacedGeneration(0,1),new PlacedGeneration(0,2)}})
                Assert.Throws<SimBoundaryException>(()=>M0c1Fixture.Create(out _,initial:new InitialPlacedState(catalog.Digest,new int[0],table)));
            var permanent=M0c1Fixture.Create(out _,policy:0);
            Assert.Throws<SimBoundaryException>(()=>M0c1Fixture.Create(out _,policy:0,initial:new InitialPlacedState(permanent.Placed.Catalog.Digest,new int[0],new[]{new PlacedGeneration(0,1)})));
        }
        [Test]
        public void SnapshotRejectsCursorFromAnotherGeneration()
        {
            var sim=M0c1Fixture.Create(out _);sim.Entities.Create(M0c1Fixture.Parent);
            var advanced=M0c1Fixture.Create(out _,initial:new InitialPlacedState(sim.Placed.Catalog.Digest,new int[0],new[]{new PlacedGeneration(0,1)}));
            byte[] bad=ReplaceTail(sim,Tail(advanced));var target=M0c1Fixture.Create(out _);
            Assert.That(Assert.Throws<SimSnapshotException>(()=>target.RestoreRetainedSnapshot(bad)).Message,Does.Contain("M0C_PLACED_CURSOR"));
        }
    }
}
