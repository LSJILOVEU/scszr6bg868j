using System;
namespace WarCore.Sim
{
    public enum GameplayStatusDuration { Instant=0,Timed=1,Infinite=2 }
    public enum GameplayStackPolicy { Refresh=0,Stack=1,Independent=2,Strongest=3 }
    public enum GameplayStackScope { Target=0,TargetAndSource=1 }
    public readonly struct GameplayStatusModifierDef
    {
        public readonly ulong Key,DamageType;
        public readonly GameplayModifierStage Stage;
        public readonly long MagnitudeRaw;
        public GameplayStatusModifierDef(ulong key,ulong damageType,GameplayModifierStage stage,long magnitudeRaw)
        {Key=key;DamageType=damageType;Stage=stage;MagnitudeRaw=magnitudeRaw;}
    }
    public readonly struct GameplayPeriodicAction
    {
        public readonly uint Ordinal;
        public readonly bool IsDamage;
        public readonly ulong Definition;
        public readonly long AmountRaw;
        private GameplayPeriodicAction(uint ordinal,bool damage,ulong definition,long amount)
        {Ordinal=ordinal;IsDamage=damage;Definition=definition;AmountRaw=amount;}
        public static GameplayPeriodicAction Damage(uint ordinal,ulong damageType,long amountRaw)=>new GameplayPeriodicAction(ordinal,true,damageType,amountRaw);
        public static GameplayPeriodicAction ChangeResource(uint ordinal,ulong resource,long deltaRaw)=>new GameplayPeriodicAction(ordinal,false,resource,deltaRaw);
    }
    public sealed class GameplayStatusDef : IComparable<GameplayStatusDef>
    {
        private readonly GameplayStatusModifierDef[] modifiers;
        private readonly GameplayPeriodicAction[] periodicActions;
        public ulong Key {get;} public string Name {get;}
        public GameplayStatusDuration DurationKind {get;} public int DurationTicks {get;}
        public int PeriodTicks {get;} public int FirstPulseDelayTicks {get;}
        public GameplayStackPolicy StackPolicy {get;} public GameplayStackScope StackScope {get;}
        public int MaxStacks {get;} public int MaxInstances {get;} public long StrengthRaw {get;}
        public ulong Tags {get;} public ulong DispelTags {get;} public ulong ImmunityTags {get;}
        public int Priority {get;} public bool HasAbsorbResource {get;} public ulong AbsorbResourceKey {get;}
        public int ModifierCount=>modifiers.Length; public GameplayStatusModifierDef ModifierAt(int i)=>modifiers[i];
        public int PeriodicActionCount=>periodicActions.Length; public GameplayPeriodicAction PeriodicActionAt(int i)=>periodicActions[i];
        public GameplayStatusDef(ulong key,string name,GameplayStatusDuration durationKind,int durationTicks=0,int periodTicks=0,
            int firstPulseDelayTicks=0,GameplayStackPolicy stackPolicy=GameplayStackPolicy.Refresh,
            GameplayStackScope stackScope=GameplayStackScope.Target,int maxStacks=1,int maxInstances=1,long strengthRaw=0,
            GameplayStatusModifierDef[] modifiers=null,GameplayPeriodicAction[] periodicActions=null,ulong tags=0,
            ulong dispelTags=0,ulong immunityTags=0,int priority=0,ulong? absorbResourceKey=null)
        {
            Key=key;Name=name;DurationKind=durationKind;DurationTicks=durationTicks;PeriodTicks=periodTicks;
            FirstPulseDelayTicks=firstPulseDelayTicks;StackPolicy=stackPolicy;StackScope=stackScope;
            MaxStacks=maxStacks;MaxInstances=maxInstances;StrengthRaw=strengthRaw;Tags=tags;DispelTags=dispelTags;
            ImmunityTags=immunityTags;Priority=priority;HasAbsorbResource=absorbResourceKey.HasValue;AbsorbResourceKey=absorbResourceKey.GetValueOrDefault();
            if((modifiers!=null && modifiers.Length>GameplayMath.MaxModifiers)||(periodicActions!=null && periodicActions.Length>GameplayMath.MaxPackets))
                throw new SimBoundaryException("M0C_STATUS_DEFINITION_CAPACITY");
            this.modifiers=modifiers==null?new GameplayStatusModifierDef[0]:(GameplayStatusModifierDef[])modifiers.Clone();
            this.periodicActions=periodicActions==null?new GameplayPeriodicAction[0]:(GameplayPeriodicAction[])periodicActions.Clone();
            for(int i=1;i<this.modifiers.Length;i++){var v=this.modifiers[i];int j=i;while(j>0 && this.modifiers[j-1].Key>v.Key){this.modifiers[j]=this.modifiers[j-1];j--;}this.modifiers[j]=v;}
            for(int i=1;i<this.periodicActions.Length;i++){var v=this.periodicActions[i];int j=i;while(j>0 && this.periodicActions[j-1].Ordinal>v.Ordinal){this.periodicActions[j]=this.periodicActions[j-1];j--;}this.periodicActions[j]=v;}
        }
        public int CompareTo(GameplayStatusDef b)=>Key.CompareTo(b.Key);
    }
    public readonly struct GameplayStatusInstanceKey : IComparable<GameplayStatusInstanceKey>
    {
        public readonly StableId Target;public readonly ulong Definition,Token;
        public readonly bool HasGroupSource;public readonly GameplayStamp GroupSource;
        public GameplayStatusInstanceKey(StableId target,ulong definition,bool hasGroupSource,GameplayStamp groupSource,ulong token)
        {Target=target;Definition=definition;HasGroupSource=hasGroupSource;GroupSource=groupSource;Token=token;}
        public int CompareTo(GameplayStatusInstanceKey b)
        {int c=Target.CompareTo(b.Target);if(c!=0)return c;c=Definition.CompareTo(b.Definition);if(c!=0)return c;
            c=HasGroupSource.CompareTo(b.HasGroupSource);if(c!=0)return c;c=GroupSource.Id.CompareTo(b.GroupSource.Id);if(c!=0)return c;
            c=GroupSource.Generation.CompareTo(b.GroupSource.Generation);return c!=0?c:Token.CompareTo(b.Token);}
    }
    internal struct GameplayStatusRow
    {
        internal GameplayStatusInstanceKey Key;internal GameplayStamp Target;internal GameplayApplyKey Attribution;
        internal GameplaySourceSnapshot SourceSnapshot;internal int Stacks;
        internal long StrengthRaw,StartTick,ExpiryTick,NextPulseTick,AttributionTick,AbsorbRemainingRaw;
        internal uint PulseOrdinal;internal bool HasExpiryTask,HasPulseTask;internal TaskKey ExpiryTask,PulseTask;
    }
    public readonly struct GameplayStatusView
    {
        public readonly GameplayStatusInstanceKey Key;public readonly GameplayStamp Target;public readonly GameplayApplyKey Attribution;
        public readonly GameplaySourceSnapshot SourceSnapshot;public readonly int Stacks;
        public readonly long StrengthRaw,StartTick,ExpiryTick,NextPulseTick,AbsorbRemainingRaw;public readonly uint PulseOrdinal;
        internal GameplayStatusView(GameplayStatusRow r){Key=r.Key;Target=r.Target;Attribution=r.Attribution;SourceSnapshot=r.SourceSnapshot;Stacks=r.Stacks;
            StrengthRaw=r.StrengthRaw;StartTick=r.StartTick;ExpiryTick=r.ExpiryTick;NextPulseTick=r.NextPulseTick;AbsorbRemainingRaw=r.AbsorbRemainingRaw;PulseOrdinal=r.PulseOrdinal;}
    }
}
