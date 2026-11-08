using System;
using System.Reflection;
using NUnit.Framework;
using WarCore.Sim;
using Simulation=WarCore.Sim.Sim;
namespace WarCore.Tests
{
    // Counters restored from a valid snapshot can sit at their type limit; the next tick must still fail explicitly.
    [TestFixture,Category("M0c2S2"),Category("LoadedTokenLimit")]
    public sealed class LoadedTokenLimitTests
    {
        private static readonly StableEntityKey A=StableEntityKey.Placed(81,1),B=StableEntityKey.Placed(81,2);
        private static StableId Low=>A.Id.CompareTo(B.Id)<0?A.Id:B.Id;
        private static GameplayApplyKey Key(StableId source,ulong token,uint ordinal=0)=>new GameplayApplyKey(source,token,0,source,token,ordinal);
        private static Simulation Create(GameplayStatusDef[] statuses,GameplayLifeRuleDef[] lifeRules=null)
        {
            var schema=new GameplaySchema(new[]{new GameplayResourceDef(1,"resource",0,1000,500,isLife:lifeRules!=null)},new GameplayDamageTypeDef[0],new GameplayPipelineDef[0],
                new[]{new GameplayArchetypeDef(1,new[]{1UL})},new GameplayCapacities(2,2,32,64,4),"loaded.limit",statuses,lifeRules:lifeRules);
            var sim=Simulation.CreateRetainedWorld(42,new PlacedCatalog(new StableContentManifest(0),new PlacedCatalogEntry(81,1,1),new PlacedCatalogEntry(81,2,1)),
                new SimConfig(entityCapacity:4,timerCapacity:16,eventRetainedTicks:1),gameplaySchema:schema);
            sim.BindGameplay(sim.Entities.Create(A),1);sim.BindGameplay(sim.Entities.Create(B),1);
            return sim;
        }
        private static void SetField<T>(GameplayState state,string name,T value)=>typeof(GameplayState).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(state,value);
        private static void Refresh(GameplayState state,string method)=>typeof(GameplayState).GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(state,null);
        // Writes the limit value with its table sum, saves, and restores into a fresh world built from the same schema.
        private static Simulation Reload(Simulation sim,Func<Simulation> build,string field,object value,string refresh)
        {
            GameplayState state=sim.Entities.Gameplay;
            typeof(GameplayState).GetField(field,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(state,value);Refresh(state,refresh);
            var resumed=build();resumed.RestoreRetainedSnapshot(sim.SaveRetainedSnapshot(),true);
            Assert.That(resumed.StateHashRetained(),Is.EqualTo(sim.StateHashRetained()));
            return resumed;
        }
        private static GameplayStatusDef Ledger=>new GameplayStatusDef(1,"ledger",GameplayStatusDuration.Infinite,dispelTags:1);

        [Test] public void RestoredDiagnosticCounterAtLimitRejectsTheNextCount()
        {
            Func<Simulation> build=()=>Create(new[]{Ledger});
            var sim=build();sim.Step(default);
            var resumed=Reload(sim,build,"rejectedResources",long.MaxValue,"RefreshDiagnostics");
            GameplayState state=resumed.Entities.Gameplay;
            var ex=Assert.Throws<SimBoundaryException>(()=>state.Spend(resumed.GameplayTarget(Low),new[]{new GameplayCost(1,600)},0));
            Assert.That(ex.Message,Is.EqualTo("M0C_DIAGNOSTIC_EXHAUSTED"));
            Assert.That(resumed.GameplayResource(Low,1),Is.EqualTo(500));
        }

        [Test] public void RestoredEffectTokenAtLimitRejectsALifeTransition()
        {
            Func<Simulation> build=()=>Create(new[]{Ledger},new[]{new GameplayLifeRuleDef(1,1,false,0)});
            var sim=build();sim.Step(default);
            var resumed=Reload(sim,build,"effectNext",ulong.MaxValue,"RefreshStatusMeta");
            GameplayState state=resumed.Entities.Gameplay;
            Assert.That(state.Enqueue(false,resumed.GameplayTarget(Low),Key(Low,1),1,-(500)),Is.EqualTo(GameplayFailure.None));
            var ex=Assert.Throws<SimBoundaryException>(()=>state.Settle());
            Assert.That(ex.Message,Is.EqualTo("M0C_LIFE_TOKEN_EXHAUSTED"));
        }

        [Test] public void RestoredEffectTokenAtLimitRejectsARefreshThatADispelTurnedIntoACreate()
        {
            Func<Simulation> build=()=>Create(new[]{Ledger});
            var sim=build();sim.Step(default);
            GameplayState first=sim.Entities.Gameplay;
            Assert.That(first.AdmitStatus(GameplayAction.ApplyStatus(sim.GameplayTarget(Low),Key(Low,1),1).Command,0),Is.EqualTo(GameplayFailure.None));first.Settle();
            Assert.That(sim.GameplayStatusCount,Is.EqualTo(1));
            var resumed=Reload(sim,build,"effectNext",ulong.MaxValue,"RefreshStatusMeta");
            GameplayState state=resumed.Entities.Gameplay;
            Assert.That(state.AdmitStatus(GameplayAction.Dispel(resumed.GameplayTarget(Low),Key(Low,2),1).Command,1),Is.EqualTo(GameplayFailure.None));
            Assert.That(state.AdmitStatus(GameplayAction.ApplyStatus(resumed.GameplayTarget(Low),Key(Low,3),1).Command,2),Is.EqualTo(GameplayFailure.None));
            var ex=Assert.Throws<SimBoundaryException>(()=>state.Settle());
            Assert.That(ex.Message,Is.EqualTo("M0C_STATUS_TOKEN_EXHAUSTED"));
        }
    }
}
