namespace WarCore.Sim
{
    internal sealed partial class GameplayState
    {
        private ulong cooldownSum,abilityCastSum;
        private void ResetAbilityProtocol()
        {
            abilityCastCount=cooldownCount=abilityInputCount=resultCount=0;cooldownSum=abilityCastSum=0;
            heldAbilityCommands=heldAbilityStatuses=heldAbilityTimers=0;abilityInputInvalid=false;
            for(int i=0;i<abilityFailures.Length;i++)abilityFailures[i]=0;
        }
        private void RefreshAbilityHash()
        {
            if(schema.AbilityCount==0)return;
            inputSum=RowLeaf(2,0);RefreshDiagnostics();
        }
        private void BeginAbilityCastChange(int index){abilityCastSum=unchecked(abilityCastSum-RowLeaf(6,index));}
        private void EndAbilityCastChange(int index){abilityCastSum=unchecked(abilityCastSum+RowLeaf(6,index));}
        private void BeginAbilityCooldownChange(int index){cooldownSum=unchecked(cooldownSum-RowLeaf(5,index));}
        private void EndAbilityCooldownChange(int index){cooldownSum=unchecked(cooldownSum+RowLeaf(5,index));}
        private bool ValidCastNext(ulong next,GameplayStamp owner)=>sim.Placed.Catalog.Find(owner.Id)<0 || next>>48==owner.Generation;
        private bool ValidCastToken(ulong token,GameplayStamp owner)=>sim.Placed.Catalog.Find(owner.Id)<0?token!=ulong.MaxValue:
            token>>48==owner.Generation && (token&0x0000FFFFFFFFFFFFUL)!=0x0000FFFFFFFFFFFFUL;
        private static GameplayStamp ReadInputStamp(SimStateReader r)
        {
            SimStateReader.Require(r.Bool(),"M0C_INPUT_STAMP");var id=new StableId(r.UInt64());ulong gen=r.UInt64();
            SimStateReader.Require(gen<=ushort.MaxValue,"M0C_INPUT_GENERATION");return new GameplayStamp(id,(ushort)gen);
        }
        private static void WriteOptionalStamp(SimStateWriter w,bool present,GameplayStamp value)
        {w.Bool(present);if(present){w.UInt64(value.Id.Value);w.UInt64(value.Generation);}}
        private static GameplayStamp ReadOptionalStamp(SimStateReader r,out bool present)
        {
            present=r.Bool();if(!present)return default;var id=new StableId(r.UInt64());ulong generation=r.UInt64();
            SimStateReader.Require(generation<=ushort.MaxValue,"M0C_TARGET_GENERATION");return new GameplayStamp(id,(ushort)generation);
        }
        private void WriteAbilityInput(SimStateWriter w)
        {
            w.Int64(inputTick);w.Int32(abilityInputCount);
            for(int i=0;i<abilityInputCount;i++)
            {
                var c=abilityInput[i];Stamp(w,c.Actor.Id,c.Actor.Generation);w.UInt64(c.CommandOrdinal);w.Int32((int)c.Kind);
                w.UInt64(c.SlotKey);w.Int32((int)c.TargetMode);WriteOptionalStamp(w,c.HasExplicitTarget,c.ExplicitTarget);
                SnapshotValues.Vector(w,c.AimDirection);SnapshotValues.Vector(w,c.TargetPoint);
            }
        }
        private long AbilityInputLength()
        {long length=16;for(int i=0;i<abilityInputCount;i++)length+=98+(abilityInput[i].HasExplicitTarget?16:0);return length;}
        private void ReadAbilityInput(SimStateReader r,long tick)
        {
            SimStateReader.Require(r.Int64()==tick,"M0C_GAMEPLAY_ROW");int count=r.Count(abilityInput.Length);
            for(int i=0;i<count;i++)
            {
                var actor=ReadInputStamp(r);ulong ordinal=r.UInt64();var kind=(GameplayInputKind)r.Int32();ulong slot=r.UInt64();
                var mode=(GameplayTargetMode)r.Int32();var target=ReadOptionalStamp(r,out bool present);
                SimVec3 aim=SnapshotValues.Vector(r),point=SnapshotValues.Vector(r);
                SimStateReader.Require(ordinal<=uint.MaxValue,"M0C_INPUT_ORDINAL");
                var command=new GameplayInputCommand(actor,(uint)ordinal,kind,slot,mode,present?(GameplayStamp?)target:null,aim,point);
                // Rejected payloads are retained too; only their canonical encoding is constrained here.
                SimStateReader.Require(i==0 || CompareAbilityInput(abilityInput[i-1],command)<=0,"M0C_INPUT_ORDER");abilityInput[i]=command;
            }
            abilityInputCount=count;
        }
        private void WriteAbilityCooldown(SimStateWriter w,int index)
        {
            var row=abilityCooldowns[index];Stamp(w,row.Owner.Id,row.Owner.Generation);w.UInt64(row.Key);w.Int64(row.ReadyTick);
            w.Bool(row.HasTask);if(row.HasTask)w.TaskKeyValue(row.CooldownTask);
        }
        private void ReadAbilityCooldown(SimStateReader r,int index,long tick)
        {
            var owner=ReadStamp(r);ulong key=r.UInt64();long ready=r.Int64();bool hasTask=r.Bool();TaskKey task=hasTask?r.ReadTaskKey():default;
            int ownerIndex=OwnerIndex(owner.Id),maximumDuration=0;bool declared=false;
            if(ownerIndex>=0)for(int i=0;i<schema.AbilityCount;i++)
            {var a=schema.AbilityAt(i);if(a.ArchetypeKey==archetypes[ownerIndex] && a.CooldownKey==key && a.CooldownTicks>0)
                {declared=true;if(a.CooldownTicks>maximumDuration)maximumDuration=a.CooldownTicks;}}
            SimStateReader.Require(declared && ready>tick && ready<=StatusProtocolFuture(tick,(ulong)maximumDuration) && hasTask
                && (index==0 || abilityCooldowns[index-1].Owner.Id.CompareTo(owner.Id)<0
                    || abilityCooldowns[index-1].Owner.Id.Equals(owner.Id) && abilityCooldowns[index-1].Key<key),"M0C_COOLDOWN_STATE");
            RequireAbilityTask(task,owner.Id,key,ready,GameplayEvents.AbilityCooldown);
            abilityCooldowns[index]=new GameplayCooldownRow{Owner=owner,Key=key,ReadyTick=ready,HasTask=hasTask,CooldownTask=task};cooldownCount++;
        }
        private static long FullSourceLength(GameplaySourceSnapshot source)=>49L+16L*source.AttributeCount+32L*source.ActionCaptureCount;
        private static void WriteFullSourceSnapshot(SimStateWriter w,GameplaySourceSnapshot source)
        {
            Stamp(w,source.Source.Id,source.Source.Generation);w.UInt64(source.AbilityKey);w.Int64(source.CapturedTick);w.Int32(source.AttributeCount);
            for(int i=0;i<source.AttributeCount;i++){var a=source.AttributeAt(i);w.UInt64(a.Key);w.Int64(a.Raw);}
            w.Int32(source.ActionCaptureCount);
            for(int i=0;i<source.ActionCaptureCount;i++)
            {var a=source.ActionCaptureAt(i);w.UInt64(a.DefinitionKey);w.UInt64(a.Ordinal);w.UInt64(a.DamageTypeKey);w.UInt64(a.PipelineKey);}
        }
        private GameplaySourceSnapshot ReadFullSourceSnapshot(SimStateReader r,long tick)
        {
            GameplayStamp source=ReadWeakStamp(r);ulong ability=r.UInt64();long captured=r.Int64();int count=r.Count(GameplaySourceSnapshot.MaximumAttributes);
            return ReadFullSourceBody(r,tick,source,ability,captured,count);
        }
        private GameplaySourceSnapshot ReadFullSourceBody(SimStateReader r,long tick,GameplayStamp source,ulong ability,long captured,int count)
        {
            int definition=schema.FindAbility(ability);SimStateReader.Require(definition>=0 && captured>0 && captured<=tick,"M0C_ABILITY_SOURCE");
            var def=schema.AbilityAt(definition);var archetype=schema.ArchetypeAt(schema.FindArchetype(def.ArchetypeKey));
            SimStateReader.Require(count==archetype.ResourceCount,"M0C_ABILITY_ATTRIBUTES");var result=new GameplaySourceSnapshot(source,captured,ability);
            for(int i=0;i<count;i++)
            {
                ulong key=r.UInt64();long raw=r.Int64();SimStateReader.Require(key==archetype.ResourceKeyAt(i),"M0C_ABILITY_ATTRIBUTE_KEY");
                var resource=schema.ResourceAt(schema.FindResource(key));
                SimStateReader.Require(raw>=resource.LowerRaw && raw<=resource.UpperRaw,"M0C_ABILITY_ATTRIBUTE_RANGE");
                result=result.WithAttribute(new GameplayCapturedAttribute(key,raw));
            }
            var expected=BuildExpectedAbilityBindings(new GameplaySourceSnapshot(source,captured,ability),def);
            int actions=r.Count(GameplaySourceSnapshot.MaximumActions);SimStateReader.Require(actions==expected.ActionCaptureCount,"M0C_ABILITY_ACTION_COUNT");
            for(int i=0;i<actions;i++)
            {
                var action=expected.ActionCaptureAt(i);ulong key=r.UInt64(),ordinal=r.UInt64(),damage=r.UInt64(),pipeline=r.UInt64();
                SimStateReader.Require(key==action.DefinitionKey && ordinal==action.Ordinal && damage==action.DamageTypeKey
                    && pipeline==action.PipelineKey,"M0C_ABILITY_ACTION_CAPTURE");
                result=result.WithActionCapture(new GameplayCapturedAction(key,(uint)ordinal,damage,pipeline));
            }
            return result;
        }
        private long AbilityCastLength(int index)
        {
            var row=abilityCasts[index];return 146L+(row.HasExplicitTarget?16:0)+25L*row.TargetCount+FullSourceLength(row.SourceSnapshot)
                +(row.HasPhaseTask?40:0)+(row.HasPulseTask?40:0);
        }
        private void WriteAbilityCast(SimStateWriter w,int index)
        {
            var row=abilityCasts[index];Stamp(w,row.Owner.Id,row.Owner.Generation);w.UInt64(row.Token);w.UInt64(row.AbilityKey);w.Int32((int)row.Phase);
            w.Int64(row.PhaseEnteredTick);w.Int64(row.NextDueTick);w.UInt64(row.NextPulseOrdinal);w.UInt64(row.InputOrdinal);w.Int32((int)row.TargetMode);
            WriteOptionalStamp(w,row.HasExplicitTarget,row.ExplicitTarget);w.Int32(row.TargetCount);
            int offset=index*schema.Capacities.TargetsPerCast;
            for(int i=0;i<row.TargetCount;i++){var target=abilityTargets[offset+i];Stamp(w,target.Id,target.Generation);w.Int64(abilityDistances[offset+i]);}
            SnapshotValues.Vector(w,row.AimDirection);SnapshotValues.Vector(w,row.TargetPoint);WriteFullSourceSnapshot(w,row.SourceSnapshot);
            w.Int32((row.HasPhaseTask?1:0)+(row.HasPulseTask?1:0));
            if(row.HasPhaseTask && row.HasPulseTask)
            {bool phaseFirst=row.PhaseTask.CompareTo(row.PulseTask)<0;w.TaskKeyValue(phaseFirst?row.PhaseTask:row.PulseTask);w.TaskKeyValue(phaseFirst?row.PulseTask:row.PhaseTask);}
            else if(row.HasPhaseTask)w.TaskKeyValue(row.PhaseTask);else if(row.HasPulseTask)w.TaskKeyValue(row.PulseTask);
        }
        private GameplayStamp ReadCastTargetStamp(SimStateReader r)
        {
            var target=ReadInputStamp(r);
            // A target may retire during windup. Persist its original life intent;
            // the release path performs the strong activity and generation check.
            SimStateReader.Require(target.Generation<=Generation(target.Id),"M0C_CAST_TARGET_GENERATION");return target;
        }
        private void ReadAbilityCast(SimStateReader r,int index,long tick)
        {
            var row=new GameplayCastRow{Owner=ReadStamp(r),Token=r.UInt64(),AbilityKey=r.UInt64(),Phase=(GameplayCastPhase)r.Int32(),
                PhaseEnteredTick=r.Int64(),NextDueTick=r.Int64()};
            ulong pulse=r.UInt64(),input=r.UInt64();row.TargetMode=(GameplayTargetMode)r.Int32();row.ExplicitTarget=ReadOptionalStamp(r,out row.HasExplicitTarget);
            int owner=OwnerIndex(row.Owner.Id),definition=schema.FindAbility(row.AbilityKey);
            SimStateReader.Require(owner>=0 && definition>=0 && ValidCastToken(row.Token,row.Owner) && row.Token<castNext[owner] && pulse<=uint.MaxValue && input<=uint.MaxValue
                && (index==0 || abilityCasts[index-1].Owner.Id.CompareTo(row.Owner.Id)<0),"M0C_CAST_KEY");
            var def=schema.AbilityAt(definition);row.NextPulseOrdinal=(uint)pulse;row.InputOrdinal=(uint)input;
            SimStateReader.Require(def.ArchetypeKey==archetypes[owner] && row.TargetMode==def.Target.Mode
                && row.HasExplicitTarget==(row.TargetMode==GameplayTargetMode.Explicit)
                && (!row.HasExplicitTarget || row.ExplicitTarget.Generation<=Generation(row.ExplicitTarget.Id)),"M0C_CAST_DEFINITION");
            row.TargetCount=r.Count(schema.Capacities.TargetsPerCast);int offset=index*schema.Capacities.TargetsPerCast;
            for(int i=0;i<row.TargetCount;i++)
            {
                var target=ReadCastTargetStamp(r);long distance=r.Int64();SimStateReader.Require(distance>=0,"M0C_CAST_DISTANCE");
                if(i>0)SimStateReader.Require(abilityDistances[offset+i-1]<distance || abilityDistances[offset+i-1]==distance
                    && abilityTargets[offset+i-1].Id.CompareTo(target.Id)<0,"M0C_CAST_TARGET_ORDER");
                for(int j=0;j<i;j++)SimStateReader.Require(!abilityTargets[offset+j].Id.Equals(target.Id),"M0C_CAST_TARGET_DUPLICATE");
                abilityTargets[offset+i]=target;abilityDistances[offset+i]=distance;
            }
            if(row.TargetCount!=0 && row.TargetMode!=GameplayTargetMode.Query)
            {
                var expected=row.TargetMode==GameplayTargetMode.Self?row.Owner:row.ExplicitTarget;
                SimStateReader.Require(row.TargetCount==1 && abilityTargets[offset].Id.Equals(expected.Id)
                    && abilityTargets[offset].Generation==expected.Generation
                    && (row.TargetMode!=GameplayTargetMode.Self || abilityDistances[offset]==0),"M0C_CAST_TARGET_INTENT");
            }
            row.AimDirection=SnapshotValues.Vector(r);row.TargetPoint=SnapshotValues.Vector(r);row.SourceSnapshot=ReadFullSourceSnapshot(r,tick);
            SimStateReader.Require(AbilityVectorValid(row.AimDirection) && AbilityVectorValid(row.TargetPoint)
                && row.SourceSnapshot.Source.Id.Equals(row.Owner.Id) && row.SourceSnapshot.Source.Generation==row.Owner.Generation
                && row.SourceSnapshot.AbilityKey==row.AbilityKey,"M0C_CAST_SOURCE");
            int count=r.Count(2);TaskKey previous=default;
            for(int i=0;i<count;i++)
            {
                TaskKey task=r.ReadTaskKey();SimStateReader.Require((i==0 || previous.CompareTo(task)<0)
                    && sim.Scheduler.TryGetPendingStable(task,out StableScheduledTask _),"M0C_CAST_TASK_ORDER");
                sim.Scheduler.TryGetPendingStable(task,out StableScheduledTask scheduled);
                if(scheduled.Event.Type.Equals(GameplayEvents.AbilityPhase))
                {SimStateReader.Require(!row.HasPhaseTask,"M0C_CAST_DUPLICATE_PHASE");RequireAbilityTask(task,row.Owner.Id,row.Token,row.NextDueTick,GameplayEvents.AbilityPhase);row.HasPhaseTask=true;row.PhaseTask=task;}
                else
                {SimStateReader.Require(!row.HasPulseTask,"M0C_CAST_DUPLICATE_PULSE");RequireAbilityTask(task,row.Owner.Id,row.Token,task.DueTick,GameplayEvents.AbilityPulse);row.HasPulseTask=true;row.PulseTask=task;row.NextPulseTick=task.DueTick;}
                previous=task;
            }
            row.Released=row.Phase!=GameplayCastPhase.Windup;row.TotalHits=row.TargetCount;
            ValidateCastTimeline(row,def,tick);abilityCasts[index]=row;abilityCastCount++;
        }
        private void ValidateCastTimeline(GameplayCastRow row,GameplayAbilityDef def,long tick)
        {
            long release=StatusProtocolFuture(row.SourceSnapshot.CapturedTick,(ulong)def.WindupTicks);
            long activeEnd=StatusProtocolFuture(release,(ulong)def.ActiveTicks),end=StatusProtocolFuture(activeEnd,(ulong)def.RecoveryTicks);
            long entered=row.Phase==GameplayCastPhase.Windup?row.SourceSnapshot.CapturedTick:row.Phase==GameplayCastPhase.Active?release:activeEnd;
            long due=row.Phase==GameplayCastPhase.Windup?release:row.Phase==GameplayCastPhase.Active?activeEnd:end;
            SimStateReader.Require((int)row.Phase>=1 && (int)row.Phase<=3 && entered<=tick && due>tick
                && row.PhaseEnteredTick==entered && row.NextDueTick==due && row.HasPhaseTask,"M0C_CAST_TIMELINE");
            if(row.Phase==GameplayCastPhase.Windup)
                SimStateReader.Require(row.NextPulseOrdinal==1 && !row.HasPulseTask,"M0C_CAST_WINDUP_PULSE");
            if(def.Target.Timing==GameplayTargetTiming.RequeryAtRelease)
                SimStateReader.Require(row.TargetCount==0,"M0C_CAST_REQUERY_TARGETS");
            else SimStateReader.Require((row.TargetCount>0 || def.Target.MaximumTargets==0) && row.TargetCount<=def.Target.MaximumTargets,"M0C_CAST_TARGET_COUNT");
            if(row.Phase!=GameplayCastPhase.Windup)
            {
                long through=tick<activeEnd?tick:activeEnd-1;
                ulong pulses=def.PulsePeriodTicks>0 && through>release?(ulong)(through-release)/(ulong)def.PulsePeriodTicks:0;
                ulong nextOrdinal=pulses+1;
                long next=def.PulsePeriodTicks>0?StatusProtocolFuture(release,nextOrdinal*(ulong)def.PulsePeriodTicks):0;
                bool expectedPulse=row.Phase==GameplayCastPhase.Active && next>tick && next<activeEnd;
                SimStateReader.Require(row.NextPulseOrdinal==nextOrdinal && row.HasPulseTask==expectedPulse
                    && (!expectedPulse || row.NextPulseTick==next),"M0C_CAST_PULSE_TIMELINE");
            }
        }
        private void RequireAbilityTask(TaskKey task,StableId owner,ulong kind,long due,EventTypeId type)
        {
            SimStateReader.Require(task.DueTick==due && task.DueTick>inputTick && task.OriginDomain==0 && task.Origin.Equals(owner) && task.Kind==kind
                && sim.Scheduler.TryGetPendingStable(task,out StableScheduledTask _),"M0C_ABILITY_TASK");
            sim.Scheduler.TryGetPendingStable(task,out StableScheduledTask scheduled);
            SimStateReader.Require(scheduled.Event.Type.Equals(type) && scheduled.Event.Target.HasValue && scheduled.Event.Target.Id.Equals(owner)
                && !scheduled.Event.Source.HasValue && scheduled.Event.Tag==0 && scheduled.Event.Payload.Magnitude.Raw==0
                && scheduled.Event.Payload.Radius.Raw==0 && scheduled.Event.Payload.Position.Equals(default(SimVec3))
                && scheduled.Event.Payload.Aux.Equals(default(SimVec3)),"M0C_ABILITY_TASK_PAYLOAD");
        }
        private void ValidateRestoredAbilities(long tick)
        {
            for(int i=0;i<abilityCastCount;i++)
            {
                var cast=abilityCasts[i];var def=schema.AbilityAt(schema.FindAbility(cast.AbilityKey));
                if(def.CooldownTicks==0)continue;
                long ready=StatusProtocolFuture(cast.SourceSnapshot.CapturedTick,(ulong)def.CooldownTicks);
                if(ready<=tick)continue;
                // ReadAbilityCooldown already rejects duplicate owner/key rows.
                int cooldown=FindAbilityCooldown(cast.Owner,def.CooldownKey);
                SimStateReader.Require(cooldown>=0 && abilityCooldowns[cooldown].ReadyTick==ready,"M0C_CAST_COOLDOWN");
            }
            sim.Scheduler.ValidateGameplayAbilities(abilityCasts,abilityCastCount,abilityCooldowns,cooldownCount);
            RebuildAbilityReservations();ValidateAbilityReservations();
        }
    }
    public sealed partial class SimScheduler
    {
        internal void ValidateGameplayAbilities(GameplayCastRow[] casts,int castCount,GameplayCooldownRow[] cooldowns,int cooldownCount)
        {
            int matched=0,expected=cooldownCount;
            for(int i=0;i<castCount;i++)expected+=(casts[i].HasPhaseTask?1:0)+(casts[i].HasPulseTask?1:0);
            for(int slot=0;slot<tasks.Length;slot++)
            {
                if(taskStates[slot]==0)continue;var task=stableTasks[slot];
                if(!task.Event.Type.Equals(GameplayEvents.AbilityPhase) && !task.Event.Type.Equals(GameplayEvents.AbilityPulse)
                    && !task.Event.Type.Equals(GameplayEvents.AbilityCooldown))continue;
                int found=0;
                for(int i=0;i<castCount;i++)
                {if(casts[i].HasPhaseTask && casts[i].PhaseTask.Equals(task.Key))found++;if(casts[i].HasPulseTask && casts[i].PulseTask.Equals(task.Key))found++;}
                for(int i=0;i<cooldownCount;i++)if(cooldowns[i].HasTask && cooldowns[i].CooldownTask.Equals(task.Key))found++;
                SimStateReader.Require(found==1,"M0C_ABILITY_ORPHAN_TASK");matched++;
            }
            SimStateReader.Require(matched==expected,"M0C_ABILITY_TASK_COUNT");
        }
    }
}
