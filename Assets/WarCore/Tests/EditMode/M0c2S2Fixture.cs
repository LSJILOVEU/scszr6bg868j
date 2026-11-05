using System;
using WarCore.Sim;
using Simulation=WarCore.Sim.Sim;

namespace WarCore.Tests
{
    public static class M0c2S2Fixture
    {
        public const long S=65536;
        public static readonly StableEntityKey Target=StableEntityKey.Placed(29,1);
        public static readonly StableEntityKey SourceA=StableEntityKey.Placed(29,2);
        public static readonly StableEntityKey SourceB=StableEntityKey.Placed(29,3);
        public static StableId Low=>SourceA.Id.CompareTo(SourceB.Id)<0?SourceA.Id:SourceB.Id;
        public static StableId High=>SourceA.Id.CompareTo(SourceB.Id)>0?SourceA.Id:SourceB.Id;
        public static GameplaySchema Schema(bool packageB=false,GameplayStatusDef[] statuses=null,int capacity=64)
        {
            if(statuses==null)statuses=Definitions(packageB);
            return new GameplaySchema(new[]{new GameplayResourceDef(1,"health",-10000*S,10000*S,8000*S,isLife:true),
                new GameplayResourceDef(2,packageB?"chakra":"ammunition",0,10000*S,100*S)},
                new[]{new GameplayDamageTypeDef(1,"direct",1,1)},
                new[]{new GameplayPipelineDef(1,new GameplayDamageSpec(0,GameplayDamageStages.All))},
                new[]{new GameplayArchetypeDef(1,new[]{1UL,2UL})},
                new GameplayCapacities(4,8,128,256,statuses:capacity),"s2.critical",statuses:statuses);
        }
        public static GameplayStatusDef[] Definitions(bool packageB=false)=>new[]{
            new GameplayStatusDef(1,packageB?"healing":"burn",GameplayStatusDuration.Timed,7,2,1,
                GameplayStackPolicy.Refresh,periodicActions:packageB?
                    new[]{GameplayPeriodicAction.ChangeResource(1,1,3*S)}:new[]{GameplayPeriodicAction.Damage(1,1,3*S)},dispelTags:1),
            new GameplayStatusDef(2,packageB?"chakra-recovery":"supply",GameplayStatusDuration.Timed,5,2,1,
                GameplayStackPolicy.Stack,maxStacks:3,periodicActions:new[]{GameplayPeriodicAction.ChangeResource(1,2,2*S)},dispelTags:2),
            new GameplayStatusDef(3,packageB?"barrier":"shield",GameplayStatusDuration.Timed,4,
                stackPolicy:GameplayStackPolicy.Independent,maxInstances:12,priority:5,absorbResourceKey:1),
            new GameplayStatusDef(4,packageB?"binding":"slow",GameplayStatusDuration.Timed,6,
                // Tags + strength are control data for later consumers; Product only changes incoming damage.
                stackPolicy:GameplayStackPolicy.Strongest,strengthRaw:S/2,tags:4,dispelTags:4,
                modifiers:new[]{new GameplayStatusModifierDef(1,1,GameplayModifierStage.Product,S/2)}),
            new GameplayStatusDef(5,packageB?"seal":"protection",GameplayStatusDuration.Timed,3,immunityTags:4,dispelTags:8)
        };
        public static Simulation Create(bool packageB=false,bool reverse=false,int slotVariant=0)
            =>CreateWith(out _,new[]{new S2Tick(new[]{S2Op.Apply(1,false,1),S2Op.Apply(2,true,1),
                S2Op.Apply(4,false,2),S2Op.Absorb(3,true,5*S,2),S2Op.Damage(false,19*S,3),
                S2Op.Damage(true,13*S,3),S2Op.Change(false,1,7*S,4)})},packageB,reverse,repeat:true,slotVariant:slotVariant);
        public static Simulation CreateWith(out S2Script script,S2Tick[] plan,bool packageB=false,bool reverse=false,
            GameplayStatusDef[] statuses=null,int capacity=64,bool repeat=false,int retireTick=0,int slotVariant=0)
        {
            var catalog=new PlacedCatalog(new StableContentManifest(0),new PlacedCatalogEntry(29,1,1),
                new PlacedCatalogEntry(29,2,1),new PlacedCatalogEntry(29,3,1),new PlacedCatalogEntry(29,4,1));
            var sim=Simulation.CreateRetainedWorld(41,catalog,new SimConfig(entityCapacity:16,timerCapacity:256),
                gameplaySchema:Schema(packageB,statuses,capacity));
            var keys=new[]{Target,SourceA,SourceB};
            EntityId dummy=default;
            if(slotVariant==0)dummy=sim.Entities.Create(StableEntityKey.Placed(29,4));
            for(int i=0;i<keys.Length;i++)sim.BindGameplay(sim.Entities.Create(keys[reverse?keys.Length-1-i:i]),1);
            if(slotVariant!=0)dummy=sim.Entities.Create(StableEntityKey.Placed(29,4));
            sim.Entities.Destroy(dummy);
            script=new S2Script(plan,reverse,repeat,retireTick);
            sim.RegisterGameplay("s2.data-program",script,SimSystemPhase.Queries);
            return sim;
        }
        public static ulong Step(Simulation sim){sim.Step(new InputFrame(default,default,primaryAttack:true));return sim.StateHashRetained();}
    }
    public readonly struct S2Op
    {
        public readonly int Kind;public readonly bool High;public readonly ulong Definition;public readonly long Amount;
        public readonly uint Ordinal;public readonly int Maximum;public readonly ulong ProducerToken;
        private S2Op(int kind,bool high,ulong definition,long amount,uint ordinal,int maximum=1,ulong producerToken=0)
        {Kind=kind;High=high;Definition=definition;Amount=amount;Ordinal=ordinal;Maximum=maximum;ProducerToken=producerToken;}
        public static S2Op Apply(ulong definition,bool high=false,uint ordinal=1,long strength=-1,ulong producerToken=0)=>new S2Op(0,high,definition,strength,ordinal,producerToken:producerToken);
        public static S2Op Absorb(ulong definition,bool high,long amount,uint ordinal=1)=>new S2Op(1,high,definition,amount,ordinal);
        public static S2Op Dispel(ulong tags,bool high=false,uint ordinal=1,int maximum=1)=>new S2Op(2,high,tags,0,ordinal,maximum);
        public static S2Op Damage(bool high,long amount,uint ordinal=1)=>new S2Op(3,high,1,amount,ordinal);
        public static S2Op Change(bool high,ulong resource,long amount,uint ordinal=1)=>new S2Op(4,high,resource,amount,ordinal);
    }
    public sealed class S2Tick
    {
        public readonly S2Op[] Operations;
        public S2Tick(S2Op[] operations){Operations=operations;}
    }
    public sealed class S2Script : IRetainedGameplaySystem
    {
        private readonly S2Tick[] plan;private readonly bool reverse,repeat;private readonly int retireTick;private readonly ulong planDigest;
        // One batch buffer sized to the largest per-source tick in the plan: a per-Step stackalloc of 128 actions was a
        // 214,016 B frame that overflowed small runner stacks (block2-independent-review STK-1).
        private readonly GameplayAction[] batchBuffer;
        private static readonly GameplayCost[] EmptyCosts=new GameplayCost[0];
        public GameplayFailure LastFailure;public int Rejections;
        public S2Script(S2Tick[] plan,bool reverse,bool repeat,int retireTick)
        {
            this.plan=plan;this.reverse=reverse;this.repeat=repeat;this.retireTick=retireTick;
            var hash=new StateHash();hash.Add(plan.Length);int largest=1;
            foreach(S2Tick tick in plan)
            {
                hash.Add(tick.Operations.Length);int low=0,high=0;
                foreach(S2Op op in tick.Operations)
                {hash.Add(op.Kind);hash.Add(op.High);hash.Add(op.Definition);hash.Add(op.Amount);hash.Add((ulong)op.Ordinal);hash.Add(op.Maximum);hash.Add(op.ProducerToken);if(op.High)high++;else low++;}
                largest=Math.Max(largest,Math.Max(low,high));
            }
            planDigest=hash.Value;batchBuffer=new GameplayAction[largest];
        }
        public int Order=>10;public string StateTypeId=>"test.s2.data-program";public int StateVersion=>1;
        public void Step(SimContext c)
        {
            int at=(int)c.Clock.Tick-1;
            if(repeat && plan.Length!=0)at%=plan.Length;
            if(at<plan.Length && c.Input.PrimaryAttack)
            {
                S2Op[] operations=plan[at].Operations;Span<GameplayAction> batch=batchBuffer;
                for(int source=0;source<2;source++)
                {
                    StableId id=source==0?M0c2S2Fixture.Low:M0c2S2Fixture.High;int count=0;
                    for(int index=0;index<operations.Length;index++)
                    {
                        S2Op op=operations[reverse?operations.Length-1-index:index];if(op.High!=(source==1))continue;
                        var key=new GameplayApplyKey(id,(ulong)c.Clock.Tick,0,id,op.ProducerToken==0?(ulong)c.Clock.Tick:op.ProducerToken,op.Ordinal);
                        GameplayStamp target=c.Owner.GameplayTarget(M0c2S2Fixture.Target.Id);
                        switch(op.Kind)
                        {
                            case 0:batch[count++]=GameplayAction.ApplyStatus(target,key,op.Definition,op.Amount);break;
                            case 1:batch[count++]=GameplayAction.GrantAbsorb(target,key,op.Definition,op.Amount);break;
                            case 2:batch[count++]=GameplayAction.Dispel(target,key,op.Definition,op.Maximum);break;
                            case 3:batch[count++]=GameplayAction.Damage(target,key,op.Definition,op.Amount);break;
                            default:batch[count++]=GameplayAction.ChangeResource(target,key,op.Definition,op.Amount);break;
                        }
                    }
                    if(count!=0)
                    {
                        LastFailure=c.SubmitGameplay(c.Owner.GameplayTarget(id),EmptyCosts,batch.Slice(0,count),0);
                        if(LastFailure!=GameplayFailure.None)Rejections++;
                    }
                }
            }
            if(c.Clock.Tick==retireTick)c.Entities.Destroy(c.Entities.Resolve(new EntityLink(M0c2S2Fixture.Low)));
        }
        public void SaveState(SimStateWriter w){w.UInt64(planDigest);w.Bool(repeat);w.Int32(retireTick);}
        public void LoadState(SimStateReader r)
        {SimStateReader.Require(r.UInt64()==planDigest && r.Bool()==repeat && r.Int32()==retireTick,"S2_SCRIPT_DATA");}
        public void WriteState(StateHash h){}
    }
}
