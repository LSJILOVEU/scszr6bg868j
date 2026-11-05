using System;
using NUnit.Framework;
using WarCore.Sim;
using Simulation=WarCore.Sim.Sim;

namespace WarCore.Tests
{
    [TestFixture,Category("M0c2S1"),Category("M0c2S1Vector")]
    public sealed class M0c2S1VectorTests
    {
        private static byte[] Hex(string text)
        {
            var bytes=new byte[text.Length/2];
            for(int i=0;i<bytes.Length;i++)bytes[i]=Convert.ToByte(text.Substring(i*2,2),16);
            return bytes;
        }
        private static Simulation Create(VectorJson item)
        {
            long initial=item["initial"].Integer,regen=item["regen"].Integer;
            ushort generation=(ushort)item["generation"].Integer;
            var schema=new GameplaySchema(new[] { new GameplayResourceDef(7,"resource",-140737488355327L,140737488355327L,
                    initial,regen,regen==0?0:1,true,true) },new GameplayDamageTypeDef[0],new GameplayPipelineDef[0],
                new[] { new GameplayArchetypeDef(9,new ulong[] { 7 }) },new GameplayCapacities(1,1,1,2),"vector.critical");
            Assert.That(schema.RulesDigest.ToString("X16"),Is.EqualTo(item["rules"].Text),item["name"].Text+" rules");
            Assert.That(schema.CapacityDigest.ToString("X16"),Is.EqualTo(item["capacity"].Text),item["name"].Text+" capacities");
            var content=new StableContentManifest(0);
            var catalog=new PlacedCatalog(content,new PlacedCatalogEntry(17,1,1));
            var sparse=generation==0?new PlacedGeneration[0]:new[] { new PlacedGeneration(0,generation) };
            var sim=Simulation.CreateRetainedWorld(41,catalog,initial:new InitialPlacedState(catalog.Digest,new int[0],sparse),gameplaySchema:schema);
            var key=StableEntityKey.Placed(17,1);
            Assert.That(key.Id.Value.ToString("X16"),Is.EqualTo(item["owner"].Text));
            sim.BindGameplay(sim.Entities.Create(key),9);
            for(int tick=0;tick<item["tick"].Integer;tick++)sim.Step(default);
            return sim;
        }
        [Test]
        public void IndependentS1GameplayBytesAndIncrementalLeavesMatch()
        {
            VectorJson cases=VectorJson.Load("m0c2-s1-gameplay-v6-v8.json")["cases"];
            for(int i=0;i<cases.Count;i++)
            {
                VectorJson item=cases[i];Simulation sim=Create(item);
                var writer=new SimStateWriter(sim.Entities,materialize:true);
                sim.Entities.Gameplay.WriteHash(writer);
                Assert.That(writer.ToArray(),Is.EqualTo(Hex(item["gameplay_hash"].Text)),item["name"].Text+" incremental leaves");
                byte[] snapshot=sim.SaveRetainedSnapshot(),expected=Hex(item["gameplay_snapshot"].Text);
                var section=new byte[expected.Length];Array.Copy(snapshot,snapshot.Length-section.Length,section,0,section.Length);
                Assert.That(section,Is.EqualTo(expected),item["name"].Text+" canonical bytes");
                Assert.That(sim.GameplayResource(StableEntityKey.Placed(17,1).Id,7),Is.EqualTo(item["value"].Integer));
                Simulation restored=Create(item);restored.RestoreRetainedSnapshot(snapshot,true);
                Assert.That(restored.StateHashRetained(),Is.EqualTo(sim.StateHashRetained()),item["name"].Text+" reversed restore");
                sim.Step(default);restored.Step(default);
                Assert.That(restored.StateHashRetained(),Is.EqualTo(sim.StateHashRetained()),item["name"].Text+" continuation");
            }
        }
    }
}
