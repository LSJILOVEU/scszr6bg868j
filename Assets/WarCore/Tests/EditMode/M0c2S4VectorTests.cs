using System;
using NUnit.Framework;
using WarCore.Sim;
namespace WarCore.Tests
{
    [TestFixture,Category("M0c2S4")]
    public sealed class M0c2S4VectorTests
    {
        private static byte[] Hex(string text)
        {byte[] bytes=new byte[text.Length/2];for(int i=0;i<bytes.Length;i++)bytes[i]=Convert.ToByte(text.Substring(i*2,2),16);return bytes;}
        [Test] public void IndependentLifeProtocolSectionsMatchEveryBoundary()
        {
            var cases=VectorJson.Load("m0c2-s4-life-v6-v8.json")["cases"];
            for(int n=0;n<cases.Count;n++)
            {
                var c=cases[n];bool d=c["downed"].Boolean;
                var schema=new GameplaySchema(new[]{new GameplayResourceDef(7,"life",-100,100,100,isLife:true)},
                    new[]{new GameplayDamageTypeDef(9,"direct",7,10)},new[]{new GameplayPipelineDef(10,new GameplayDamageSpec(0,GameplayDamageStages.None))},
                    new[]{new GameplayArchetypeDef(12,new[]{7UL})},new GameplayCapacities(1,1,8,16),"s4.vector",
                    lifeRules:new[]{new GameplayLifeRuleDef(12,7,d,d?3:0,terminateTags:8,preventDownedTags:4)});
                Assert.That(schema.CapacityDigest.ToString("X16"),Is.EqualTo(c["capacity"].Text),"capacity");
                Assert.That(schema.RulesDigest.ToString("X16"),Is.EqualTo(c["rules"].Text),"rules");
                var catalog=new PlacedCatalog(new StableContentManifest(0),new PlacedCatalogEntry(101,1,1));
                var sim=WarCore.Sim.Sim.CreateRetainedWorld(101,catalog,new SimConfig(entityCapacity:2,timerCapacity:8),gameplaySchema:schema);
                sim.BindGameplay(sim.Entities.Create(StableEntityKey.Placed(101,1)),12);sim.RegisterGameplay("s4.vector",new VectorScript(),SimSystemPhase.Queries);
                while(sim.Clock.Tick<c["tick"].Integer)sim.Step(default);
                var writer=new SimStateWriter(sim.Entities,materialize:true);sim.Entities.Gameplay.Save(writer);
                Assert.That(writer.ToArray(),Is.EqualTo(Hex(c["gameplay_snapshot"].Text)),c["name"].Text+" snapshot");
                var hash=new SimStateWriter(sim.Entities,materialize:true);sim.Entities.Gameplay.WriteHash(hash);
                Assert.That(hash.ToArray(),Is.EqualTo(Hex(c["gameplay_hash"].Text)),c["name"].Text+" hash");
                var bytes=sim.SaveRetainedSnapshot();sim.RestoreRetainedSnapshot(bytes,true);
            }
        }
        private sealed class VectorScript:IRetainedGameplaySystem
        {
            public int Order=>0;public string StateTypeId=>"test.s4.vector";public int StateVersion=>1;
            public void Step(SimContext c)
            {
                var id=StableEntityKey.Placed(101,1).Id;var stamp=c.Owner.GameplayTarget(id);var key=new GameplayApplyKey(id,(ulong)c.Clock.Tick,0,id,0,1);
                if(c.Clock.Tick==1 || c.Clock.Tick==3)c.Damage(stamp,key,9,110);
                if(c.Clock.Tick==2)c.ChangeResource(stamp,key,7,30);
                if(c.Clock.Tick==4)c.Damage(stamp,key,9,1);
            }
            public void SaveState(SimStateWriter w){} public void LoadState(SimStateReader r){} public void WriteState(StateHash h){}
        }
    }
}
