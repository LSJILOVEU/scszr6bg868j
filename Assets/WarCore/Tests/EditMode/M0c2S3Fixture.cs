using System;
using WarCore.Sim;
using Simulation=WarCore.Sim.Sim;
namespace WarCore.Tests
{
    // A/B are frozen content only. Both packages run the identical Sim interpreter.
    public static class M0c2S3Fixture
    {
        public const long S=65536;
        public const uint UnitLayer=0x80000000u;
        public static readonly StableEntityKey Actor=StableEntityKey.Placed(73,1);
        public static readonly StableEntityKey OtherActor=StableEntityKey.Placed(73,2);
        public static readonly StableEntityKey Target=StableEntityKey.Placed(73,3);
        public static readonly StableEntityKey Target2=StableEntityKey.Placed(73,4);
        public static readonly StableEntityKey Target3=StableEntityKey.Placed(73,5);
        private static readonly GameplayInputCommand[][] Script=BuildScript();
        public static GameplayAbilityAction Action(uint ordinal,GameplayPrimitive kind,ulong definition,long amount=0,
            GameplayActionTrigger trigger=GameplayActionTrigger.OnRelease,int maximum=1,bool required=false)
            =>new GameplayAbilityAction(ordinal,trigger,kind,definition,amount,maximum,required);
        public static GameplayAbilityDef[] Definitions(bool packageB=false)
        {
            var ray=new GameplayTargetSpec(GameplayTargetMode.Query,rangeRaw:12*S,layerMask:UnitLayer,excludeSelf:true,relation:GameplayRelation.OtherFaction);
            var sphere=new GameplayTargetSpec(GameplayTargetMode.Query,GameplayTargetShape.Sphere,GameplayTargetSelection.AllCapped,
                rangeRaw:12*S,layerMask:UnitLayer,maximumTargets:4,radiusRaw:5*S,useTargetPoint:true,excludeSelf:true,relation:GameplayRelation.OtherFaction);
            var self=new GameplayTargetSpec(GameplayTargetMode.Self);
            var specific=new GameplayTargetSpec(GameplayTargetMode.Explicit,rangeRaw:12*S,layerMask:UnitLayer);
            var sector=new GameplayTargetSpec(GameplayTargetMode.Query,GameplayTargetShape.Sector,GameplayTargetSelection.AllCapped,
                rangeRaw:12*S,layerMask:UnitLayer,maximumTargets:4,radiusRaw:12*S,halfHeightRaw:2*S,
                cosineHalfAnglePermille:600,excludeSelf:true,relation:GameplayRelation.OtherFaction);
            return new[]{
                new GameplayAbilityDef(1,packageB?"fire-technique":"rifle",1,1,ray,new[]{Action(1,GameplayPrimitive.Damage,1,11),
                    Action(2,GameplayPrimitive.Damage,1,3,GameplayActionTrigger.ActivePulse),Action(3,GameplayPrimitive.Damage,1,5,GameplayActionTrigger.OnEnd)},
                    new[]{new GameplayCost(2,3)},1,11,2,5,2,2,4),
                new GameplayAbilityDef(2,packageB?"area-technique":"grenade",2,1,sphere,new[]{Action(1,GameplayPrimitive.Damage,1,15)},
                    new[]{new GameplayCost(2,5)},2,6,1),
                new GameplayAbilityDef(3,packageB?"chakra-barrier":"armour",3,1,self,new[]{Action(1,GameplayPrimitive.GrantAbsorb,3,20)},
                    new[]{new GameplayCost(2,4)},3,8),
                new GameplayAbilityDef(4,packageB?"healing-technique":"medkit",4,1,self,new[]{Action(1,GameplayPrimitive.ChangeResource,1,17)},
                    new[]{new GameplayCost(2,2)},4,5),
                new GameplayAbilityDef(5,packageB?"binding-technique":"stun",5,1,specific,new[]{Action(1,GameplayPrimitive.ApplyStatus,2,-1)},
                    new[]{new GameplayCost(2,8)},5,8,1),
                new GameplayAbilityDef(6,packageB?"poison-technique":"burn",6,1,specific,new[]{Action(1,GameplayPrimitive.ApplyStatus,1,-1)},
                    new[]{new GameplayCost(2,6)},6,8),
                new GameplayAbilityDef(7,packageB?"chakra-recovery":"supply",7,1,self,new[]{Action(1,GameplayPrimitive.ChangeResource,2,20)}),
                new GameplayAbilityDef(8,packageB?"fan-strike":"melee",8,1,sector,new[]{Action(1,GameplayPrimitive.Damage,1,7)},
                    new[]{new GameplayCost(2,3)},8,4),
                new GameplayAbilityDef(9,packageB?"body-flicker":"dash",9,1,self,new[]{Action(1,GameplayPrimitive.RequestMove,1)}),
                new GameplayAbilityDef(10,"placeholder",10,1,self,new[]{Action(1,GameplayPrimitive.SpawnPlaceholder,1)}),
                new GameplayAbilityDef(11,packageB?"seal-release":"cleanse",11,1,self,new[]{Action(1,GameplayPrimitive.Dispel,4)})
            };
        }
        public static GameplaySchema Schema(bool packageB=false,GameplayAbilityDef[] abilities=null,long energy=100000,
            GameplayInputAuthority authority=null,int commands=128,int timers=128)
        {
            return new GameplaySchema(new[]{new GameplayResourceDef(1,"health",0,1000000,8000),
                    new GameplayResourceDef(2,packageB?"chakra":"ammunition",0,1000000,energy),new GameplayResourceDef(3,"reserve",0,100,2)},
                new[]{new GameplayDamageTypeDef(1,"direct",1,1)},
                new[]{new GameplayPipelineDef(1,new GameplayDamageSpec(0,GameplayDamageStages.None))},
                new[]{new GameplayArchetypeDef(1,new[]{1UL,2UL,3UL},factionKey:1),new GameplayArchetypeDef(2,new[]{1UL,2UL,3UL},factionKey:2)},
                new GameplayCapacities(8,24,commands,256,statuses:32,cooldowns:64,casts:8,inputCommands:32,targetsPerCast:8),
                "s3.critical",new[]{
                    new GameplayStatusDef(1,packageB?"poison":"burn",GameplayStatusDuration.Timed,7,2,1,
                        periodicActions:new[]{GameplayPeriodicAction.Damage(1,1,2)},dispelTags:1),
                    new GameplayStatusDef(2,packageB?"binding":"stun",GameplayStatusDuration.Timed,4,tags:4,dispelTags:4),
                    new GameplayStatusDef(3,packageB?"barrier":"armour",GameplayStatusDuration.Timed,6,absorbResourceKey:1)},
                abilities??Definitions(packageB),authority??new GameplayInputAuthority(1,new[]{Actor.Id,OtherActor.Id}));
        }
        public static Simulation Create(bool packageB=false,bool reverse=false,int slotVariant=0)
            =>CreateWith(Schema(packageB),reverse,slotVariant);
        public static Simulation CreateWith(GameplaySchema schema,bool reverse=false,int slotVariant=0,int timerCapacity=256)
        {
            var catalog=new PlacedCatalog(new StableContentManifest(0),new PlacedCatalogEntry(73,1,1),new PlacedCatalogEntry(73,2,1),
                new PlacedCatalogEntry(73,3,1),new PlacedCatalogEntry(73,4,1),new PlacedCatalogEntry(73,5,1),new PlacedCatalogEntry(73,6,1));
            var sim=Simulation.CreateRetainedWorld(73,catalog,new SimConfig(entityCapacity:16,timerCapacity:timerCapacity),gameplaySchema:schema);
            var positions=sim.Entities.Register<Position>("position",new PositionSerializer());
            var colliders=sim.Entities.Register<Collider>("collider",new ColliderSerializer());
            var keys=new[]{Actor,OtherActor,Target,Target2,Target3};
            EntityId dummy=default;if(slotVariant==0)dummy=sim.Entities.Create(StableEntityKey.Placed(73,6));
            for(int j=0;j<keys.Length;j++)
            {
                int i=reverse?keys.Length-1-j:j;EntityId entity=sim.Entities.Create(keys[i]);sim.BindGameplay(entity,i<2?1UL:2UL);
                sim.Entities.Set(positions,entity,new Position(SimVec3.Metres(i<2?0:2*i,0,i==1?3:0)));
                sim.Entities.Set(colliders,entity,new Collider(SimFix.Ratio(1,4),SimFix.FromInt(1),UnitLayer));
            }
            if(slotVariant!=0)dummy=sim.Entities.Create(StableEntityKey.Placed(73,6));sim.Entities.Destroy(dummy);
            sim.ConfigureSpatial(positions,colliders,SimFix.FromInt(3));return sim;
        }
        public static GameplayInputCommand Command(ulong slot,StableEntityKey? actor=null,uint ordinal=1,GameplayTargetMode? mode=null,
            GameplayStamp? target=null,GameplayInputKind kind=GameplayInputKind.Activate)
        {
            GameplayTargetMode selected=mode??(slot==1 || slot==2 || slot==8?GameplayTargetMode.Query:slot==5 || slot==6?GameplayTargetMode.Explicit:GameplayTargetMode.Self);
            return new GameplayInputCommand(new GameplayStamp((actor??Actor).Id),ordinal,kind,slot,selected,
                target??(selected==GameplayTargetMode.Explicit?(GameplayStamp?)new GameplayStamp(Target.Id):null),
                selected==GameplayTargetMode.Query?SimVec3.Metres(1,0,0):default,
                slot==2?SimVec3.Metres(6,0,0):default);
        }
        public static void Input(Simulation sim,params GameplayInputCommand[] commands)
            =>sim.Step(new InputFrame(default,default,gameplayTick:sim.Clock.Tick+1,skillCommands:commands));
        public static ulong Step(Simulation sim)
        {
            int at=(int)(sim.Clock.Tick%37);sim.Step(new InputFrame(default,default,gameplayTick:sim.Clock.Tick+1,skillCommands:Script[at]));
            return sim.StateHashRetained();
        }
        private static GameplayInputCommand[][] BuildScript()
        {
            var script=new GameplayInputCommand[37][];
            script[0]=new[]{Command(1)};script[11]=new[]{Command(2)};script[14]=new[]{Command(3)};script[18]=new[]{Command(4)};
            script[21]=new[]{Command(5)};script[24]=new[]{Command(6)};script[27]=new[]{Command(8)};script[30]=new[]{Command(7)};return script;
        }
    }
}
