using System;
namespace WarCore.Sim
{
    // Values are the frozen first-failure precedence from block2-design appendix D.
    public enum GameplayAbilityFailure
    {
        None=0, InvalidInput=1, DuplicateCommand=2, UnauthorizedActor=3, ActorGone=4,
        ActorLifeMismatch=5, UnknownAbility=6, UnsupportedAction=7, BlockedState=8,
        Busy=9, Cooldown=10, InvalidTarget=11, TargetGone=12, TargetLifeMismatch=13,
        LayerRejected=14, RelationRejected=15, OutOfRange=16, Occluded=17, NoTarget=18,
        TargetOverflow=19, InsufficientResource=20, StatusCapacity=21, CommandCapacity=22,
        TimerCapacity=23, SequenceExhausted=24
    }
    public enum GameplayInputKind { Activate=0, Cancel=1 }
    public enum GameplayTargetMode { Self=0, Explicit=1, Query=2 }
    public enum GameplayTargetShape { Ray=0, Sphere=1, Sector=2, Capsule=3 }
    public enum GameplayTargetSelection { Nearest=0, AllCapped=1 }
    public enum GameplayTargetTiming { LockAtAdmission=0, RequeryAtRelease=1 }
    public enum GameplayRelation { Any=0, SameFaction=1, OtherFaction=2 }
    public enum GameplayCastPhase { Idle=0, Windup=1, Active=2, Recovery=3 }
    public enum GameplayActionTrigger { OnRelease=0, ActivePulse=1, OnEnd=2 }
    public enum GameplayPrimitive { Damage=0, ApplyStatus=1, Dispel=2, ChangeResource=3, GrantAbsorb=4, RequestMove=5, SpawnPlaceholder=6 }
    public readonly struct GameplayTargetSpec
    {
        public readonly GameplayTargetMode Mode;
        public readonly GameplayTargetShape Shape;
        public readonly GameplayTargetSelection Selection;
        public readonly GameplayTargetTiming Timing;
        public readonly GameplayRelation Relation;
        public readonly long RangeRaw,RadiusRaw,HalfHeightRaw;
        public readonly uint LayerMask,OcclusionMask;
        public readonly int MaximumTargets,CosineHalfAnglePermille;
        public readonly bool RequiresAll,UseTargetPoint,ExcludeSelf;
        public readonly ulong RequiredTags,RejectedTags;
        public GameplayTargetSpec(GameplayTargetMode mode,GameplayTargetShape shape=GameplayTargetShape.Ray,
            GameplayTargetSelection selection=GameplayTargetSelection.Nearest,GameplayTargetTiming timing=GameplayTargetTiming.LockAtAdmission,
            long rangeRaw=0,uint layerMask=0,int maximumTargets=1,bool requiresAll=false,long radiusRaw=0,
            long halfHeightRaw=0,int cosineHalfAnglePermille=0,uint occlusionMask=0,bool useTargetPoint=false,
            bool excludeSelf=false,GameplayRelation relation=GameplayRelation.Any,ulong requiredTags=0,ulong rejectedTags=0)
        {Mode=mode;Shape=shape;Selection=selection;Timing=timing;RangeRaw=rangeRaw;LayerMask=layerMask;
            MaximumTargets=maximumTargets;RequiresAll=requiresAll;RadiusRaw=radiusRaw;HalfHeightRaw=halfHeightRaw;
            CosineHalfAnglePermille=cosineHalfAnglePermille;OcclusionMask=occlusionMask;UseTargetPoint=useTargetPoint;
            ExcludeSelf=excludeSelf;Relation=relation;RequiredTags=requiredTags;RejectedTags=rejectedTags;}
    }
    public readonly struct GameplayAbilityAction
    {
        public readonly uint Ordinal;
        public readonly GameplayActionTrigger Trigger;
        public readonly GameplayPrimitive Primitive;
        public readonly ulong Definition;
        public readonly long AmountRaw;
        public readonly int Maximum;
        public readonly bool RequiredCapability,HasAmountAttribute,HasMultiplierAttribute;
        public readonly ulong AmountAttributeKey,MultiplierAttributeKey;
        public GameplayAbilityAction(uint ordinal,GameplayActionTrigger trigger,GameplayPrimitive primitive,ulong definition,
            long amountRaw=0,int maximum=1,bool requiredCapability=false,ulong? amountAttributeKey=null,ulong? multiplierAttributeKey=null)
        {Ordinal=ordinal;Trigger=trigger;Primitive=primitive;Definition=definition;AmountRaw=amountRaw;Maximum=maximum;RequiredCapability=requiredCapability;HasAmountAttribute=amountAttributeKey.HasValue;AmountAttributeKey=amountAttributeKey.GetValueOrDefault();HasMultiplierAttribute=multiplierAttributeKey.HasValue;MultiplierAttributeKey=multiplierAttributeKey.GetValueOrDefault();}
    }
    public sealed class GameplayAbilityDef : IComparable<GameplayAbilityDef>
    {
        private readonly GameplayCost[] costs;
        private readonly GameplayAbilityAction[] actions;
        public ulong Key {get;} public string Name {get;} public ulong SlotKey {get;} public ulong ArchetypeKey {get;}
        public ulong CooldownKey {get;} public int CooldownTicks {get;}
        public int WindupTicks {get;} public int ActiveTicks {get;} public int RecoveryTicks {get;} public int PulsePeriodTicks {get;}
        public bool AllowWhileDowned {get;}
        public ulong InterruptTags {get;} public GameplayTargetSpec Target {get;}
        public int CostCount=>costs.Length; public GameplayCost CostAt(int i)=>costs[i];
        internal ReadOnlySpan<GameplayCost> Costs=>costs;
        public int ActionCount=>actions.Length; public GameplayAbilityAction ActionAt(int i)=>actions[i];
        public GameplayAbilityDef(ulong key,string name,ulong slotKey,ulong archetypeKey,GameplayTargetSpec target,
            GameplayAbilityAction[] actions,GameplayCost[] costs=null,ulong cooldownKey=0,int cooldownTicks=0,
            int windupTicks=0,int activeTicks=0,int recoveryTicks=0,int pulsePeriodTicks=0,ulong interruptTags=0,bool allowWhileDowned=false)
        {
            if(actions==null || actions.Length>GameplayMath.MaxModifiers || costs!=null && costs.Length>GameplayMath.MaxModifiers)
                throw new SimBoundaryException("M0C_ABILITY_DEFINITION_CAPACITY");
            AllowWhileDowned=allowWhileDowned;Key=key;Name=name;SlotKey=slotKey;ArchetypeKey=archetypeKey;Target=target;CooldownKey=cooldownKey;CooldownTicks=cooldownTicks;
            WindupTicks=windupTicks;ActiveTicks=activeTicks;RecoveryTicks=recoveryTicks;PulsePeriodTicks=pulsePeriodTicks;InterruptTags=interruptTags;
            this.actions=(GameplayAbilityAction[])actions.Clone();this.costs=costs==null?new GameplayCost[0]:(GameplayCost[])costs.Clone();
            for(int i=1;i<this.actions.Length;i++){var value=this.actions[i];int j=i;while(j>0 && this.actions[j-1].Ordinal>value.Ordinal){this.actions[j]=this.actions[j-1];j--;}this.actions[j]=value;}
            for(int i=1;i<this.costs.Length;i++){var value=this.costs[i];int j=i;while(j>0 && this.costs[j-1].ResourceKey>value.ResourceKey){this.costs[j]=this.costs[j-1];j--;}this.costs[j]=value;}
        }
        public int CompareTo(GameplayAbilityDef other)=>Key.CompareTo(other.Key);
    }
    internal struct GameplayCastRow
    {
        internal GameplayStamp Owner,ExplicitTarget;
        internal ulong Token,AbilityKey;
        internal GameplayCastPhase Phase;
        internal long PhaseEnteredTick,NextDueTick,NextPulseTick;
        internal uint NextPulseOrdinal,InputOrdinal;
        internal GameplayTargetMode TargetMode;
        internal bool HasExplicitTarget,HasPhaseTask,HasPulseTask,Released;
        internal SimVec3 AimDirection,TargetPoint;
        internal GameplaySourceSnapshot SourceSnapshot;
        internal TaskKey PhaseTask,PulseTask;
        internal int TargetCount,TotalHits,ReservedCommands,ReservedStatuses,ReservedTimers;
    }
    internal struct GameplayCooldownRow
    {internal GameplayStamp Owner;internal ulong Key;internal long ReadyTick;internal bool HasTask;internal TaskKey CooldownTask;}
    public readonly struct GameplayCastView
    {
        public readonly GameplayStamp Owner; public readonly ulong Token,AbilityKey;
        public readonly GameplayCastPhase Phase;public readonly long PhaseEnteredTick,NextDueTick;
        public readonly uint NextPulseOrdinal,InputOrdinal; public readonly int TargetCount,TotalHits;
        public readonly GameplaySourceSnapshot SourceSnapshot;
        internal GameplayCastView(GameplayCastRow r){Owner=r.Owner;Token=r.Token;AbilityKey=r.AbilityKey;Phase=r.Phase;
            PhaseEnteredTick=r.PhaseEnteredTick;NextDueTick=r.NextDueTick;NextPulseOrdinal=r.NextPulseOrdinal;
            InputOrdinal=r.InputOrdinal;TargetCount=r.TargetCount;TotalHits=r.TotalHits;SourceSnapshot=r.SourceSnapshot;}
    }
    public readonly struct GameplayInputResult
    {
        public readonly GameplayStamp Actor;public readonly uint CommandOrdinal;public readonly GameplayAbilityFailure Failure;
        public readonly ulong CastToken;public readonly int Written,TotalHits;
        internal GameplayInputResult(GameplayInputCommand command,GameplayAbilityFailure failure,ulong token=0,int written=0,int totalHits=0)
        {Actor=command.Actor;CommandOrdinal=command.CommandOrdinal;Failure=failure;CastToken=token;Written=written;TotalHits=totalHits;}
    }
}
