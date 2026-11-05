using System;
using WarCore.Sim;
using Simulation=WarCore.Sim.Sim;
namespace WarCore.Tests
{
    public static class M0c2S1Fixture
    {
        public const long S=65536;
        public static readonly StableEntityKey Target=StableEntityKey.Placed(17,1);
        public static readonly StableEntityKey SourceA=StableEntityKey.Placed(17,2);
        public static readonly StableEntityKey SourceB=StableEntityKey.Placed(17,3);
        public static StableId Low => SourceA.Id.CompareTo(SourceB.Id)<0?SourceA.Id:SourceB.Id;
        public static StableId High => SourceA.Id.CompareTo(SourceB.Id)>0?SourceA.Id:SourceB.Id;
        public static GameplaySchema Schema(bool packageB=false,int commands=8,long armorRaw=S/2)
        {
            return new GameplaySchema(new[]{
                new GameplayResourceDef(1,"health",-100*S,100*S,80*S,isLife:true),
                new GameplayResourceDef(2,packageB?"chakra":"ammunition",-10*S,100*S,10*S,3*S,3)},
                new[]{new GameplayDamageTypeDef(1,packageB?"melee":"rifle",1,1),new GameplayDamageTypeDef(2,"direct",1,2)},
                new[]{new GameplayPipelineDef(1,new GameplayDamageSpec(20*S,GameplayDamageStages.Resistance|GameplayDamageStages.Critical,
                    resistanceRaw:S/2,penetrationRaw:S/4,criticalChanceRaw:S/2,criticalFactorRaw:S+S/2)),
                    new GameplayPipelineDef(2,new GameplayDamageSpec(0,GameplayDamageStages.None))},
                new[]{new GameplayArchetypeDef(1,new[]{1UL,2UL},new[]{new GameplayDefenseDef(1,armorRaw)})},new GameplayCapacities(4,8,commands,commands+8),"combat.critical");
        }
        public static Simulation Create(bool packageB=false,bool reverse=false)
            => CreateWith(out _,packageB,reverse);
        public static Simulation CreateWith(out S1Script script,bool packageB=false,bool reverse=false,int commands=8,long armorRaw=S/2)
        {
            var content=new StableContentManifest(0);
            var catalog=new PlacedCatalog(content,new PlacedCatalogEntry(17,1,1),new PlacedCatalogEntry(17,2,1),new PlacedCatalogEntry(17,3,1));
            var sim=Simulation.CreateRetainedWorld(41,catalog,new SimConfig(entityCapacity:8,timerCapacity:32),
                gameplaySchema:Schema(packageB,commands,armorRaw));
            var keys=new[]{Target,SourceA,SourceB};
            for(int i=0;i<3;i++){var key=keys[reverse?2-i:i];sim.BindGameplay(sim.Entities.Create(key),1);}
            script=new S1Script(reverse,packageB?17*S:20*S,packageB?13*S:11*S);
            sim.RegisterGameplay("s1.data-program",script,SimSystemPhase.Queries);
            return sim;
        }
        public static ulong Step(Simulation sim)
        { sim.Step(new InputFrame(default,default,primaryAttack:true));return sim.StateHashRetained(); }
    }
    public sealed class S1Script : IRetainedGameplaySystem
    {
        private readonly bool reverse;
        private readonly long damage,heal;
        private readonly GameplayCost[] cost={new GameplayCost(2,M0c2S1Fixture.S)};
        private readonly GameplayCost[] insufficient={new GameplayCost(1,1),new GameplayCost(2,1000*M0c2S1Fixture.S)};
        public GameplayFailure LastFailure;
        public S1Script(bool reverse,long damage,long heal){this.reverse=reverse;this.damage=damage;this.heal=heal;}
        public int Order=>10;
        public string StateTypeId=>"test.s1.data-program";
        public int StateVersion=>1;
        private static GameplayApplyKey Key(StableId source,ulong token,uint action,ulong producer=0)
            => new GameplayApplyKey(source,token,0,source,producer,action);
        public void Step(SimContext c)
        {
            if(!c.Input.PrimaryAttack)return;
            var target=c.Owner.GameplayTarget(M0c2S1Fixture.Target.Id);ulong token=(ulong)c.Clock.Tick;
            int mode=c.Input.SkillSlot;
            if(mode==2)
            { LastFailure=c.SpendResources(target,insufficient);return; }
            if(mode==1)
            {
                c.ChangeResource(target,Key(M0c2S1Fixture.Low,token,2),1,150*M0c2S1Fixture.S);
                c.Damage(target,Key(M0c2S1Fixture.High,token,1),2,200*M0c2S1Fixture.S);return;
            }
            if(mode==3)
            {
                // Same source; producer token must order noncommuting signed changes.
                c.ChangeResource(target,Key(M0c2S1Fixture.Low,token,1,2),1,-100*M0c2S1Fixture.S);
                c.ChangeResource(target,Key(M0c2S1Fixture.Low,token,1,1),1,100*M0c2S1Fixture.S);return;
            }
            if(mode==4)
            {
                LastFailure=c.Damage(target,Key(M0c2S1Fixture.Low,token,1),2,1);
                LastFailure=c.Damage(target,Key(M0c2S1Fixture.Low,token,2),2,1);return;
            }
            if(mode==5)
            {
                System.Span<GameplayAction> batch=stackalloc GameplayAction[2];
                batch[0]=GameplayAction.Damage(target,Key(M0c2S1Fixture.Low,token,1),2,1);
                batch[1]=GameplayAction.Damage(target,Key(M0c2S1Fixture.Low,token,2),2,1);
                LastFailure=c.SubmitGameplay(target,cost,batch);return;
            }
            if(mode==6)
            { LastFailure=c.Damage(new GameplayStamp(target.Id,1),Key(M0c2S1Fixture.Low,token,1),1,damage);return; }
            if(mode==7)
            {
                var key=Key(M0c2S1Fixture.Low,token,1);
                c.Damage(target,key,2,1);c.ChangeResource(target,key,1,1);return;
            }
            if(mode==8)
            {
                c.SpendResources(c.Owner.GameplayTarget(M0c2S1Fixture.High),cost);
                c.SpendResources(c.Owner.GameplayTarget(M0c2S1Fixture.Low),cost);return;
            }
            if(mode==9)
            { c.Damage(target,Key(M0c2S1Fixture.Low,token,1),2,200*M0c2S1Fixture.S);return; }
            LastFailure=c.SpendResources(c.Owner.GameplayTarget(M0c2S1Fixture.Low),cost);
            if(LastFailure!=GameplayFailure.None)return;
            for(int i=0;i<2;i++)
            {
                StableId source=(reverse?1-i:i)==0?M0c2S1Fixture.High:M0c2S1Fixture.Low;
                c.Damage(target,Key(source,token,1),1,damage);
            }
            c.ChangeResource(target,Key(M0c2S1Fixture.Low,token,2),1,heal);
        }
        public void SaveState(SimStateWriter w){w.Int64(damage);w.Int64(heal);}
        public void LoadState(SimStateReader r){SimStateReader.Require(r.Int64()==damage && r.Int64()==heal,"S1_SCRIPT_DATA");}
        public void WriteState(StateHash h){}
    }
}
