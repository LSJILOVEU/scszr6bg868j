using System;
namespace WarCore.Sim
{
    internal sealed partial class GameplayState
    {
        private long AbilityFutureTick(long now,int duration)=>duration==0?now:GameplayMath.FutureTick(now,duration,ref timeSaturations);
        private const ulong CastCounterMask=0x0000FFFFFFFFFFFFUL;
        private void WriteAbilityCast(int index,GameplayCastRow row)
        {BeginAbilityCastChange(index);abilityCasts[index]=row;EndAbilityCastChange(index);}
        private void WriteAbilityCooldown(int index,GameplayCooldownRow row)
        {BeginAbilityCooldownChange(index);abilityCooldowns[index]=row;EndAbilityCooldownChange(index);}
        private int FindAbilityCast(StableId owner)
        {for(int i=0;i<abilityCastCount;i++)if(abilityCasts[i].Owner.Id.Equals(owner))return i;return -1;}
        private int FindAbilityCooldown(GameplayStamp owner,ulong key)
        {for(int i=0;i<cooldownCount;i++)if(abilityCooldowns[i].Owner.Id.Equals(owner.Id) && abilityCooldowns[i].Key==key)return i;return -1;}
        private void DeleteAbilityCooldown(int index)
        {
            BeginAbilityCooldownChange(index);
            if(abilityCooldowns[index].HasTask)sim.Scheduler.CancelStable(abilityCooldowns[index].CooldownTask);
            for(int i=index+1;i<cooldownCount;i++)abilityCooldowns[i-1]=abilityCooldowns[i];
            abilityCooldowns[--cooldownCount]=default;
        }
        private void DeleteAbilityCast(int index)
        {
            BeginAbilityCastChange(index);
            var row=abilityCasts[index];if(row.HasPhaseTask)sim.Scheduler.CancelStable(row.PhaseTask);if(row.HasPulseTask)sim.Scheduler.CancelStable(row.PulseTask);
            int cap=schema.Capacities.TargetsPerCast;
            for(int i=index+1;i<abilityCastCount;i++)
            {abilityCasts[i-1]=abilityCasts[i];for(int j=0;j<cap;j++){abilityTargets[(i-1)*cap+j]=abilityTargets[i*cap+j];abilityDistances[(i-1)*cap+j]=abilityDistances[i*cap+j];}}
            abilityCasts[--abilityCastCount]=default;RebuildAbilityReservations();
        }
        private void CleanupAbilities(StableId owner)
        {
            if(schema.AbilityCount==0)return;
            int cast=FindAbilityCast(owner);if(cast>=0)DeleteAbilityCast(cast);
            for(int i=cooldownCount-1;i>=0;i--)if(abilityCooldowns[i].Owner.Id.Equals(owner))DeleteAbilityCooldown(i);
            RefreshAbilityHash();
        }
        internal void BeginAbilities()
        {
            if(schema.AbilityCount==0)return;
            while(sim.Scheduler.TryDequeueStable(GameplayEvents.AbilityCooldown,out StableScheduledTask cooldown))
            {int at=FindAbilityCooldown(new GameplayStamp(cooldown.Key.Origin),cooldown.Key.Kind);
                if(at<0 || !abilityCooldowns[at].HasTask || !abilityCooldowns[at].CooldownTask.Equals(cooldown.Key))throw new SimBoundaryException("M0C_ABILITY_COOLDOWN_TASK");
                var row=abilityCooldowns[at];row.HasTask=false;WriteAbilityCooldown(at,row);DeleteAbilityCooldown(at);}
            while(sim.Scheduler.TryDequeueStable(GameplayEvents.AbilityPhase,out StableScheduledTask phase))
            {int at=FindAbilityCast(phase.Key.Origin);
                if(at<0 || !abilityCasts[at].HasPhaseTask || !abilityCasts[at].PhaseTask.Equals(phase.Key))throw new SimBoundaryException("M0C_ABILITY_PHASE_TASK");
                var row=abilityCasts[at];row.HasPhaseTask=false;row.PhaseTask=default;WriteAbilityCast(at,row);}
            while(sim.Scheduler.TryDequeueStable(GameplayEvents.AbilityPulse,out StableScheduledTask pulse))
            {int at=FindAbilityCast(pulse.Key.Origin);
                if(at<0 || !abilityCasts[at].HasPulseTask || !abilityCasts[at].PulseTask.Equals(pulse.Key))throw new SimBoundaryException("M0C_ABILITY_PULSE_TASK");
                var row=abilityCasts[at];row.HasPulseTask=false;row.PulseTask=default;WriteAbilityCast(at,row);}
            RebuildAbilityReservations();RefreshAbilityHash();
        }
        private static int CompareCapturedAction(GameplayCapturedAction a,GameplayCapturedAction b)
        {int c=a.DefinitionKey.CompareTo(b.DefinitionKey);if(c!=0)return c;c=a.Ordinal.CompareTo(b.Ordinal);if(c!=0)return c;c=a.DamageTypeKey.CompareTo(b.DamageTypeKey);return c!=0?c:a.PipelineKey.CompareTo(b.PipelineKey);}
        private GameplaySourceSnapshot BuildExpectedAbilityBindings(GameplaySourceSnapshot snapshot,GameplayAbilityDef definition)
        {
            Span<GameplayCapturedAction> captured=stackalloc GameplayCapturedAction[GameplaySourceSnapshot.MaximumActions];int count=0;
            for(int i=0;i<definition.ActionCount;i++)
            {
                var action=definition.ActionAt(i);ulong damage=action.Primitive==GameplayPrimitive.Damage?action.Definition:0;
                AddAbilityBinding(captured,ref count,new GameplayCapturedAction(definition.Key,action.Ordinal,damage,
                    action.Primitive==GameplayPrimitive.Damage?schema.DamageTypeAt(schema.FindDamageType(damage)).PipelineKey:0));
                if(action.Primitive!=GameplayPrimitive.ApplyStatus && action.Primitive!=GameplayPrimitive.GrantAbsorb)continue;
                var status=schema.StatusAt(schema.FindStatus(action.Definition));
                for(int j=0;j<status.PeriodicActionCount;j++)
                {var child=status.PeriodicActionAt(j);AddAbilityBinding(captured,ref count,new GameplayCapturedAction(status.Key,child.Ordinal,
                    child.IsDamage?child.Definition:0,child.IsDamage?schema.DamageTypeAt(schema.FindDamageType(child.Definition)).PipelineKey:0));}
            }
            for(int i=0;i<count;i++)snapshot=snapshot.WithActionCapture(captured[i]);return snapshot;
        }
        private static void AddAbilityBinding(Span<GameplayCapturedAction> captures,ref int count,GameplayCapturedAction value)
        {
            int at=0;while(at<count && CompareCapturedAction(captures[at],value)<0)at++;
            if(at<count && CompareCapturedAction(captures[at],value)==0)return;
            if(count==captures.Length)throw new SimBoundaryException("M0C_SOURCE_CAPTURE_CAPACITY");
            for(int i=count;i>at;i--)captures[i]=captures[i-1];captures[at]=value;count++;
        }
        private GameplaySourceSnapshot CaptureAbilitySource(GameplayStamp owner,GameplayAbilityDef definition)
        {
            var snapshot=new GameplaySourceSnapshot(owner,sim.Clock.Tick,definition.Key); // S3_MUTATION:captured_tick
            var archetype=schema.ArchetypeAt(schema.FindArchetype(definition.ArchetypeKey));
            for(int i=0;i<archetype.ResourceCount;i++){ulong key=archetype.ResourceKeyAt(i);snapshot=snapshot.WithAttribute(new GameplayCapturedAttribute(key,Read(owner.Id,key)));}
            return BuildExpectedAbilityBindings(snapshot,definition);
        }
        private long CapturedAbilityAmount(GameplayAbilityAction action,GameplaySourceSnapshot snapshot)
        {
            long value=action.AmountRaw;
            if(action.HasAmountAttribute && !snapshot.TryAttribute(action.AmountAttributeKey,out value))throw new SimBoundaryException("M0C_SOURCE_ATTRIBUTE");
            if(action.HasMultiplierAttribute)
            {if(!snapshot.TryAttribute(action.MultiplierAttributeKey,out long factor))throw new SimBoundaryException("M0C_SOURCE_ATTRIBUTE");
                value=GameplayMath.MultiplyFactor(value,factor,ref numericSaturations);}
            return value;
        }
        private static long AbilityPulseCount(GameplayAbilityDef d)
        {return d.PulsePeriodTicks==0 || d.ActiveTicks==0?0:((long)d.ActiveTicks-1)/d.PulsePeriodTicks;}
        private long AbilityStatusBudget(GameplayAbilityDef d,GameplayCastRow row,long now)
        {
            long perTarget=0;
            for(int i=0;i<d.ActionCount;i++)
            {
                var a=d.ActionAt(i);if(a.Primitive!=GameplayPrimitive.ApplyStatus && a.Primitive!=GameplayPrimitive.GrantAbsorb)continue;
                var status=schema.StatusAt(schema.FindStatus(a.Definition));if(status.DurationKind==GameplayStatusDuration.Instant || status.DurationKind==GameplayStatusDuration.Timed && status.DurationTicks==0)continue;
                long times=a.Trigger==GameplayActionTrigger.ActivePulse?AbilityPulseCount(d):1;
                if(row.Phase==GameplayCastPhase.Recovery)times=0;
                else if(row.Released)
                {if(a.Trigger==GameplayActionTrigger.OnRelease)times=0;
                    else if(a.Trigger==GameplayActionTrigger.ActivePulse){times-=row.NextPulseOrdinal-1;if(times<0)times=0;}}
                perTarget+=times;
            }
            return perTarget*d.Target.MaximumTargets;
        }
        private long AbilityCommandBudget(GameplayAbilityDef d,GameplayCastRow row)
        {
            long commands=(long)d.ActionCount*d.Target.MaximumTargets;
            for(int i=0;i<d.ActionCount;i++)
            {
                var a=d.ActionAt(i);if(a.Primitive!=GameplayPrimitive.ApplyStatus && a.Primitive!=GameplayPrimitive.GrantAbsorb)continue;
                var status=schema.StatusAt(schema.FindStatus(a.Definition));if(status.DurationKind==GameplayStatusDuration.Instant || status.DurationKind==GameplayStatusDuration.Timed && status.DurationTicks==0)continue;
                long times=a.Trigger==GameplayActionTrigger.ActivePulse?AbilityPulseCount(d):1;
                if(row.Phase==GameplayCastPhase.Recovery)times=0;
                else if(row.Released)
                {if(a.Trigger==GameplayActionTrigger.OnRelease)times=0;
                    else if(a.Trigger==GameplayActionTrigger.ActivePulse){times-=row.NextPulseOrdinal-1;if(times<0)times=0;}}
                long actions=times*d.Target.MaximumTargets;
                // Each factor is separately bounded. Guard before the final multiplication.
                if(status.PeriodicActionCount!=0 && actions>GameplayMath.MaxPackets/status.PeriodicActionCount)return GameplayMath.MaxPackets+1L;
                commands+=actions*status.PeriodicActionCount;
                if(commands>GameplayMath.MaxPackets)return GameplayMath.MaxPackets+1L;
            }
            return commands;
        }
        private long ExistingStatusPulseBudget()
        {long count=0;for(int i=0;i<statusCount;i++)count+=schema.StatusAt(schema.FindStatus(statuses[i].Key.Definition)).PeriodicActionCount;return count;}
        private void RebuildAbilityReservations()
        {
            heldAbilityCommands=heldAbilityStatuses=heldAbilityTimers=0;
            for(int i=0;i<abilityCastCount;i++)
            {
                var row=abilityCasts[i];var d=schema.AbilityAt(schema.FindAbility(row.AbilityKey));
                long status=AbilityStatusBudget(d,row,sim.Clock.Tick);
                long commands=AbilityCommandBudget(d,row);
                // Pending/Due already account for owned handles. Hold only the
                // remaining phase/pulse slots, plus future status tasks.
                long timers=status*2+2-(row.HasPhaseTask?1:0)-(row.HasPulseTask?1:0);
                if(status>GameplayMath.MaxPackets || commands>GameplayMath.MaxPackets || timers>GameplayMath.MaxPackets
                    || heldAbilityCommands>GameplayMath.MaxPackets-commands || heldAbilityStatuses>GameplayMath.MaxPackets-status
                    || heldAbilityTimers>GameplayMath.MaxPackets-timers)throw new SimBoundaryException("M0C_ABILITY_RESERVATION_RANGE");
                row.ReservedCommands=(int)commands;row.ReservedStatuses=(int)status;row.ReservedTimers=(int)timers;abilityCasts[i]=row;
                heldAbilityCommands+=row.ReservedCommands;heldAbilityStatuses+=row.ReservedStatuses;heldAbilityTimers+=row.ReservedTimers;
            }
        }
        private void ValidateAbilityReservations()
        {
            SimStateReader.Require(heldAbilityCommands+ExistingStatusPulseBudget()<=commands.Length && heldAbilityCommands+ExistingStatusPulseBudget()<=notices.Length-resourceCount
                && heldAbilityStatuses<=statuses.Length-statusCount
                && heldAbilityTimers+MissingStatusTimers()+MissingLifeTimers()<=sim.Scheduler.Capacity-sim.Scheduler.PendingCount-sim.Scheduler.DueCount,"M0C_ABILITY_FUTURE_CAPACITY");
        }
        private static bool AbilityTargetsKnown(GameplayAbilityDef definition)
            =>definition.Target.Mode!=GameplayTargetMode.Query || definition.Target.Timing==GameplayTargetTiming.LockAtAdmission;
        private long RemainingAbilityStatusApplications(GameplayAbilityDef definition,GameplayCastRow row,ulong status)
        {
            if(row.Phase==GameplayCastPhase.Recovery)return 0;
            long count=0;
            for(int i=0;i<definition.ActionCount;i++)
            {
                var action=definition.ActionAt(i);
                if((action.Primitive!=GameplayPrimitive.ApplyStatus && action.Primitive!=GameplayPrimitive.GrantAbsorb) || action.Definition!=status)continue;
                long times=action.Trigger==GameplayActionTrigger.ActivePulse?AbilityPulseCount(definition):1;
                if(row.Released)
                {
                    if(action.Trigger==GameplayActionTrigger.OnRelease)times=0;
                    else if(action.Trigger==GameplayActionTrigger.ActivePulse){times-=row.NextPulseOrdinal-1;if(times<0)times=0;}
                }
                count+=times;
            }
            return count;
        }
        private bool KnownAbilityHasTarget(int index,GameplayAbilityDef definition,GameplayStamp target)
        {
            var row=abilityCasts[index];
            if(definition.Target.Mode==GameplayTargetMode.Self)return row.Owner.Id.Equals(target.Id) && row.Owner.Generation==target.Generation;
            if(definition.Target.Mode==GameplayTargetMode.Explicit)return row.ExplicitTarget.Id.Equals(target.Id) && row.ExplicitTarget.Generation==target.Generation;
            for(int i=0;i<row.TargetCount;i++)
            {var candidate=abilityTargets[index*schema.Capacities.TargetsPerCast+i];if(candidate.Id.Equals(target.Id) && candidate.Generation==target.Generation)return true;}
            return false;
        }
        private GameplayAbilityFailure CheckKnownAbilityStatusGroups(GameplayAbilityDef definition,GameplayCastRow row)
        {
            if(!AbilityTargetsKnown(definition))return GameplayAbilityFailure.None;
            for(int a=0;a<definition.ActionCount;a++)
            {
                var action=definition.ActionAt(a);if(action.Primitive!=GameplayPrimitive.ApplyStatus && action.Primitive!=GameplayPrimitive.GrantAbsorb)continue;
                var status=schema.StatusAt(schema.FindStatus(action.Definition));
                if(status.StackPolicy!=GameplayStackPolicy.Independent || status.DurationKind==GameplayStatusDuration.Instant
                    || status.DurationKind==GameplayStatusDuration.Timed && status.DurationTicks==0)continue;
                for(int t=0;t<selectedAbilityCount;t++)
                {
                    var target=selectedAbilityTargets[t];
                    var item=new GameplayCommand(2,target,new GameplayApplyKey(row.Owner.Id,0,0,row.Owner.Id,0,0),status.Key,-1,0,default);
                    long demand=RemainingAbilityStatusApplications(definition,row,status.Key);
                    for(int i=0;i<statusCount;i++)if(SameGroup(statuses[i],item,status))demand++;
                    for(int i=0;i<commandCount;i++)
                    {var prior=commands[i];if((prior.Kind==2 || prior.Kind==4) && SameGroup(prior,item,status))demand++;}
                    for(int i=0;i<abilityCastCount;i++)
                    {
                        var prior=abilityCasts[i];var priorDefinition=schema.AbilityAt(schema.FindAbility(prior.AbilityKey));
                        if(!AbilityTargetsKnown(priorDefinition) || !KnownAbilityHasTarget(i,priorDefinition,target))continue;
                        if(status.StackScope!=GameplayStackScope.Target && (!prior.Owner.Id.Equals(row.Owner.Id) || prior.Owner.Generation!=row.Owner.Generation))continue;
                        demand+=RemainingAbilityStatusApplications(priorDefinition,prior,status.Key);
                    }
                    if(demand>status.MaxInstances)return GameplayAbilityFailure.StatusCapacity; // TASK_B:known_independent_group
                }
            }
            return GameplayAbilityFailure.None;
        }
        private GameplayAbilityFailure AdmitAbility(GameplayInputCommand input,int owner,GameplayAbilityDef definition,out ulong token,out int written,out int total)
        {
            token=0;written=total=0;
            for(int i=0;i<definition.ActionCount;i++)if(definition.ActionAt(i).Primitive==GameplayPrimitive.RequestMove || definition.ActionAt(i).Primitive==GameplayPrimitive.SpawnPlaceholder)return GameplayAbilityFailure.UnsupportedAction;
            if(IsDead(owner) || lifeStates[owner]==GameplayLifeState.Downed && !definition.AllowWhileDowned || (StatusTags(input.Actor.Id)&definition.InterruptTags)!=0)return GameplayAbilityFailure.BlockedState;
            if(FindAbilityCast(input.Actor.Id)>=0)return GameplayAbilityFailure.Busy;
            if(AbilityCooldownReady(input.Actor.Id,definition.CooldownKey)>sim.Clock.Tick)return GameplayAbilityFailure.Cooldown; // S3_MUTATION:cooldown_boundary
            var targetFailure=SelectAbilityTargets(input.Actor,definition,input);if(targetFailure!=GameplayAbilityFailure.None)return targetFailure;
            if(SpendCore(input.Actor,definition.Costs,false)!=GameplayFailure.None)return GameplayAbilityFailure.InsufficientResource; // S3_MUTATION:whole_cost
            GameplayCastRow row=new GameplayCastRow{Owner=input.Actor,AbilityKey=definition.Key,Phase=GameplayCastPhase.Windup,PhaseEnteredTick=sim.Clock.Tick,
                NextPulseOrdinal=1,InputOrdinal=input.CommandOrdinal,TargetMode=input.TargetMode,HasExplicitTarget=input.HasExplicitTarget,
                ExplicitTarget=input.ExplicitTarget,AimDirection=input.AimDirection,TargetPoint=input.TargetPoint};
            var groupFailure=CheckKnownAbilityStatusGroups(definition,row);if(groupFailure!=GameplayAbilityFailure.None)return groupFailure;
            long reserveStatus=AbilityStatusBudget(definition,row,sim.Clock.Tick),reserveCommands=AbilityCommandBudget(definition,row);
            long reserveTimers=reserveStatus*2+2+(definition.CooldownTicks>0?1:0);
            if(reserveStatus>statuses.Length-statusCount-heldAbilityStatuses-reservedStatusCreates)return GameplayAbilityFailure.StatusCapacity;
            if(abilityCastCount==abilityCasts.Length || reserveCommands>commands.Length-commandCount-heldAbilityCommands-ExistingStatusPulseBudget()
                || reserveCommands>notices.Length-resourceCount-commandCount-heldAbilityCommands-ExistingStatusPulseBudget())return GameplayAbilityFailure.CommandCapacity;
            if(definition.CooldownTicks>0 && cooldownCount==abilityCooldowns.Length || reserveTimers>sim.Scheduler.Capacity-sim.Scheduler.PendingCount-sim.Scheduler.DueCount-heldAbilityTimers-MissingStatusTimers()-reservedStatusTimers-MissingLifeTimers())return GameplayAbilityFailure.TimerCapacity;
            if(sim.Placed.Catalog.Find(input.Actor.Id)>=0?(castNext[owner]&CastCounterMask)==CastCounterMask:castNext[owner]==ulong.MaxValue)return GameplayAbilityFailure.SequenceExhausted; // S3_MUTATION:cast_sentinel
            // Capture before the admission fee; a later windup release never rereads these attributes.
            row.SourceSnapshot=CaptureAbilitySource(input.Actor,definition); // S3_MUTATION:capture_admission
            AdmissionOrder(input.Actor,input.CommandOrdinal);
            if(SpendCore(input.Actor,definition.Costs)!=GameplayFailure.None)throw new SimBoundaryException("M0C_ABILITY_ATOMIC_FEE");
            ownerSum=unchecked(ownerSum-RowLeaf(3,owner));token=castNext[owner];castNext[owner]++;ownerSum=unchecked(ownerSum+RowLeaf(3,owner));row.Token=token;
            row.NextDueTick=AbilityFutureTick(sim.Clock.Tick,definition.WindupTicks);
            int at=0;while(at<abilityCastCount && abilityCasts[at].Owner.Id.CompareTo(input.Actor.Id)<0)at++;
            for(int i=abilityCastCount;i>at;i--){abilityCasts[i]=abilityCasts[i-1];for(int j=0;j<schema.Capacities.TargetsPerCast;j++)
                {abilityTargets[i*schema.Capacities.TargetsPerCast+j]=abilityTargets[(i-1)*schema.Capacities.TargetsPerCast+j];abilityDistances[i*schema.Capacities.TargetsPerCast+j]=abilityDistances[(i-1)*schema.Capacities.TargetsPerCast+j];}}
            abilityCasts[at]=row;abilityCastCount++;EndAbilityCastChange(at);StoreAbilityTargets(at);written=selectedAbilityCount;total=selectedAbilityTotal;
            if(definition.CooldownTicks>0)
            {
                int cd=0;while(cd<cooldownCount && (abilityCooldowns[cd].Owner.Id.CompareTo(input.Actor.Id)<0
                    || abilityCooldowns[cd].Owner.Id.Equals(input.Actor.Id) && abilityCooldowns[cd].Key<definition.CooldownKey))cd++;
                for(int i=cooldownCount;i>cd;i--)abilityCooldowns[i]=abilityCooldowns[i-1];
                long ready=GameplayMath.FutureTick(sim.Clock.Tick,definition.CooldownTicks,ref timeSaturations); // S3_MUTATION:cooldown_start
                var cooldown=new GameplayCooldownRow{Owner=input.Actor,Key=definition.CooldownKey,ReadyTick=ready};
                if(ready>sim.Clock.Tick){cooldown.CooldownTask=sim.Scheduler.ScheduleFor(ready,0,input.Actor.Id,definition.CooldownKey,new StableSimEvent(GameplayEvents.AbilityCooldown,target:new EntityLink(input.Actor.Id)));cooldown.HasTask=true;}
                abilityCooldowns[cd]=cooldown;cooldownCount++;EndAbilityCooldownChange(cd);
            }
            RebuildAbilityReservations();AdvanceOneAbility(at);return GameplayAbilityFailure.None;
        }
        private void ScheduleAbility(int index)
        {
            var row=abilityCasts[index];
            if(!row.HasPhaseTask && row.NextDueTick>sim.Clock.Tick)
            {row.PhaseTask=sim.Scheduler.ScheduleFor(row.NextDueTick,0,row.Owner.Id,row.Token,new StableSimEvent(GameplayEvents.AbilityPhase,target:new EntityLink(row.Owner.Id)));row.HasPhaseTask=true;}
            if(row.Phase==GameplayCastPhase.Active && row.NextPulseTick>sim.Clock.Tick && row.NextPulseTick<row.NextDueTick && !row.HasPulseTask)
            {row.PulseTask=sim.Scheduler.ScheduleFor(row.NextPulseTick,0,row.Owner.Id,row.Token,new StableSimEvent(GameplayEvents.AbilityPulse,target:new EntityLink(row.Owner.Id)));row.HasPulseTask=true;}
            WriteAbilityCast(index,row);
        }
        private bool FailAbilityRelease(int index,GameplayAbilityFailure failure)
        {
            var row=abilityCasts[index];RecordAbilityFailure(failure);AbilityFailureNotice(row.Owner,row.InputOrdinal,failure,row.Token);
            DeleteAbilityCast(index);return false;
        }
        private static GameplayAbilityFailure AbilityReleaseFailure(GameplayFailure failure)
            =>failure==GameplayFailure.TargetGone?GameplayAbilityFailure.TargetGone:
                failure==GameplayFailure.TargetLifeMismatch?GameplayAbilityFailure.TargetLifeMismatch:
                failure==GameplayFailure.StatusCapacity?GameplayAbilityFailure.StatusCapacity:
                failure==GameplayFailure.InsufficientResource?GameplayAbilityFailure.InsufficientResource:GameplayAbilityFailure.CommandCapacity;
        private bool ReleaseAbilityActions(int index,GameplayActionTrigger trigger,uint pulse)
        {
            var targetFailure=RefreshAbilityTargets(index);
            if(targetFailure!=GameplayAbilityFailure.None)return FailAbilityRelease(index,targetFailure);
            var row=abilityCasts[index];var d=schema.AbilityAt(schema.FindAbility(row.AbilityKey));
            // The current cast owns this queue budget; other pending casts retain theirs.
            int oldCommands=heldAbilityCommands,oldStatuses=heldAbilityStatuses,oldTimers=heldAbilityTimers;
            int oldCount=commandCount,oldCreates=reservedStatusCreates,oldStatusTimers=reservedStatusTimers;long oldPulses=reservedStatusPulses;
            heldAbilityCommands-=row.ReservedCommands;heldAbilityStatuses-=row.ReservedStatuses;heldAbilityTimers-=row.ReservedTimers;
            int releaseCount=0;GameplayFailure failure=GameplayFailure.None;
            for(int a=0;a<d.ActionCount;a++)
            {
                var action=d.ActionAt(a);if(action.Trigger!=trigger)continue;long amount=CapturedAbilityAmount(action,row.SourceSnapshot);
                for(int j=0;j<row.TargetCount;j++)
                {
                    var target=abilityTargets[index*schema.Capacities.TargetsPerCast+j];
                    var key=new GameplayApplyKey(row.Owner.Id,row.Token,0,row.Owner.Id,row.Token,action.Ordinal,pulse);
                    GameplayCommand item;
                    if(action.Primitive==GameplayPrimitive.Damage || action.Primitive==GameplayPrimitive.ChangeResource)
                        item=new GameplayCommand(action.Primitive==GameplayPrimitive.Damage,target,key,action.Definition,amount);
                    else
                    {
                        int kind=action.Primitive==GameplayPrimitive.ApplyStatus?2:action.Primitive==GameplayPrimitive.Dispel?3:4;
                        item=new GameplayCommand(kind,target,key,action.Definition,amount,action.Maximum,row.SourceSnapshot);
                    }
                    abilityReleaseActions[releaseCount++]=GameplayAction.FromCommand(item);
                }
            }
            // Check the entire trigger before any of its damage or status actions enters the queue.
            for(int i=0;i<releaseCount && failure==GameplayFailure.None;i++)
            {var item=abilityReleaseActions[i].Command;failure=item.Kind>1?ValidateStatusCommand(item):ValidateTarget(item.Target);}
            if(failure==GameplayFailure.None)failure=ReserveStatuses(abilityReleaseActions.AsSpan(0,releaseCount)); // TASK_B:release_status_preflight
            if(failure==GameplayFailure.StatusCapacity){Count(ref statusCapacityRejections);RefreshDiagnostics();}
            for(int i=0;i<releaseCount && failure==GameplayFailure.None;i++)
            {
                var item=abilityReleaseActions[i].Command;
                failure=item.Kind>1?EnqueueStatus(item):Enqueue(item.Damage,item.Target,item.Key,item.Definition,item.Amount);
            }
            if(failure!=GameplayFailure.None)
            {
                commandCount=oldCount;reservedStatusCreates=oldCreates;reservedStatusTimers=oldStatusTimers;reservedStatusPulses=oldPulses;
                heldAbilityCommands=oldCommands;heldAbilityStatuses=oldStatuses;heldAbilityTimers=oldTimers;
                return FailAbilityRelease(index,AbilityReleaseFailure(failure));
            }
            heldAbilityCommands=oldCommands;heldAbilityStatuses=oldStatuses;heldAbilityTimers=oldTimers;return true;
        }
        private void AdvanceOneAbility(int index)
        {
            var row=abilityCasts[index];var definition=schema.AbilityAt(schema.FindAbility(row.AbilityKey));long now=sim.Clock.Tick;
            if(row.Phase==GameplayCastPhase.Active && row.NextPulseTick!=0 && row.NextPulseTick<=now && row.NextPulseTick<row.NextDueTick)
            {
                if(!ReleaseAbilityActions(index,GameplayActionTrigger.ActivePulse,row.NextPulseOrdinal))return;
                row=abilityCasts[index];if(row.NextPulseOrdinal==uint.MaxValue)throw new SimBoundaryException("M0C_ABILITY_PULSE_EXHAUSTED");
                row.NextPulseOrdinal++;row.NextPulseTick=definition.PulsePeriodTicks<row.NextDueTick-now?GameplayMath.FutureTick(now,definition.PulsePeriodTicks,ref timeSaturations):0;WriteAbilityCast(index,row);
            }
            for(int phase=0;phase<3;phase++)
            {
                row=abilityCasts[index];if(row.NextDueTick>now)break; // S3_MUTATION:phase_boundary
                if(row.Phase==GameplayCastPhase.Windup)
                {
                    if(!ReleaseAbilityActions(index,GameplayActionTrigger.OnRelease,0))return;
                    row=abilityCasts[index];row.Released=true;row.Phase=GameplayCastPhase.Active;row.PhaseEnteredTick=now;
                    row.NextDueTick=AbilityFutureTick(now,definition.ActiveTicks);
                    row.NextPulseTick=definition.PulsePeriodTicks>0 && definition.PulsePeriodTicks<row.NextDueTick-now?GameplayMath.FutureTick(now,definition.PulsePeriodTicks,ref timeSaturations):0;WriteAbilityCast(index,row);
                }
                else if(row.Phase==GameplayCastPhase.Active)
                {
                    if(!ReleaseAbilityActions(index,GameplayActionTrigger.OnEnd,0))return;
                    row=abilityCasts[index];if(row.HasPulseTask)sim.Scheduler.CancelStable(row.PulseTask);
                    row.HasPulseTask=false;row.PulseTask=default;row.NextPulseTick=0;row.Phase=GameplayCastPhase.Recovery;row.PhaseEnteredTick=now;
                    row.NextDueTick=AbilityFutureTick(now,definition.RecoveryTicks);WriteAbilityCast(index,row);
                }
                else{DeleteAbilityCast(index);return;}
            }
            ScheduleAbility(index);
            if(definition.Target.Timing==GameplayTargetTiming.RequeryAtRelease){row=abilityCasts[index];row.TargetCount=0;WriteAbilityCast(index,row);}
            RebuildAbilityReservations();
        }
        private void AdvanceCompletedAbilitiesBeforeInput()
        {
            for(int i=0;i<abilityCastCount;)
            {
                var row=abilityCasts[i];var definition=schema.AbilityAt(schema.FindAbility(row.AbilityKey));
                bool ends=row.NextDueTick<=sim.Clock.Tick && (row.Phase==GameplayCastPhase.Recovery
                    || row.Phase==GameplayCastPhase.Active && definition.RecoveryTicks==0
                    || row.Phase==GameplayCastPhase.Windup && definition.ActiveTicks==0 && definition.RecoveryTicks==0);
                if(ends)AdvanceOneAbility(i);
                if(!ends || i<abilityCastCount && abilityCasts[i].Owner.Id.Equals(row.Owner.Id) && abilityCasts[i].Token==row.Token)i++;
            }
        }
        private void AdvanceAbilityReleases()
        {
            for(int i=0;i<abilityCastCount;)
            {ulong token=abilityCasts[i].Token;StableId owner=abilityCasts[i].Owner.Id;AdvanceOneAbility(i);
                if(i<abilityCastCount && abilityCasts[i].Token==token && abilityCasts[i].Owner.Id.Equals(owner))i++;}
        }
        private void InterruptAbilities()
        {
            if(schema.AbilityCount==0)return;
            for(int i=abilityCastCount-1;i>=0;i--)
            {var row=abilityCasts[i];var definition=schema.AbilityAt(schema.FindAbility(row.AbilityKey));
                if((StatusTags(row.Owner.Id)&definition.InterruptTags)!=0)DeleteAbilityCast(i); // S3_MUTATION:interrupt_control
            }
            RefreshAbilityHash();
        }
    }
}
