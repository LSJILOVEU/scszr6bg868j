using System;
namespace WarCore.Sim
{
    internal sealed partial class GameplayState
    {
        private GameplayLifeState[] lifeStates;
        private long[] lifeEntered,downedDue;
        private bool[] hasDownedTask;
        private TaskKey[] downedTasks;
        private void InitLife()
        {
            int count=schema.Capacities.Owners;
            lifeStates=new GameplayLifeState[count];lifeEntered=new long[count];downedDue=new long[count];
            hasDownedTask=new bool[count];downedTasks=new TaskKey[count];InitNotices();
        }
        private void CopyLife(int from,int to)
        {lifeStates[to]=lifeStates[from];lifeEntered[to]=lifeEntered[from];downedDue[to]=downedDue[from];hasDownedTask[to]=hasDownedTask[from];downedTasks[to]=downedTasks[from];}
        private void ClearLife(int at)
        {lifeStates[at]=GameplayLifeState.Active;lifeEntered[at]=downedDue[at]=0;hasDownedTask[at]=false;downedTasks[at]=default;}
        private bool IsDead(int owner)=>owner>=0 && lifeStates[owner]==GameplayLifeState.Dead;
        private ulong LifeTags(int owner)=>schema.ArchetypeAt(schema.FindArchetype(archetypes[owner])).Tags|StatusTags(owners[owner]);
        internal GameplayLifeView Life(StableId id)
        {int at=OwnerIndex(id);if(at<0)throw new SimBoundaryException("M0C_LIFE_OWNER");return new GameplayLifeView(lifeStates[at],lifeEntered[at],downedDue[at]);}
        // Reserve a future downed timer for every potentially downable owner.
        // This is a capacity promise, not a scheduler entry or a per-tick countdown.
        private int MissingLifeTimers()
        {
            int count=0;if(schema.LifeRuleCount==0)return 0;
            for(int i=0;i<ownerCount;i++){int rule=schema.FindLifeRule(archetypes[i]);
                if(rule>=0 && schema.LifeRuleAt(rule).AllowDowned && !IsDead(i) && !hasDownedTask[i])count++;}
            return count;
        }
        private void BeginLife()
        {
            while(sim.Scheduler.TryDequeueStable(GameplayEvents.DownedExpiry,out StableScheduledTask due))
            {
                int owner=OwnerIndex(due.Key.Origin);
                if(owner<0 || !hasDownedTask[owner] || !downedTasks[owner].Equals(due.Key))throw new SimBoundaryException("M0C_DOWNED_TASK");
                ownerSum=unchecked(ownerSum-RowLeaf(3,owner));hasDownedTask[owner]=false;downedTasks[owner]=default;
                ownerSum=unchecked(ownerSum+RowLeaf(3,owner));
            }
        }
        private void ResolveLife()
        {
            if(schema.LifeRuleCount==0)return;
            for(int owner=0;owner<ownerCount;owner++)
            {
                if(IsDead(owner))continue; // S4: dead is terminal; no second side effect.
                int ruleIndex=schema.FindLifeRule(archetypes[owner]);if(ruleIndex<0)continue;
                var rule=schema.LifeRuleAt(ruleIndex);int resource=ResourceIndex(owners[owner],rule.ResourceKey);
                long threshold=schema.ResourceAt(schema.FindResource(rule.ResourceKey)).LifeThresholdRaw;
                ulong tags=LifeTags(owner);GameplayLifeState next=lifeStates[owner];
                bool terminate=rule.TerminateTags!=0 && (tags&rule.TerminateTags)!=0;
                bool recovered=values[resource]>threshold && (tags&rule.RescueTags)==rule.RescueTags;
                if(terminate)next=GameplayLifeState.Dead;
                else if(next==GameplayLifeState.Downed)
                {
                    if(recovered)next=GameplayLifeState.Active;
                    else if(downedDue[owner]<=sim.Clock.Tick)next=GameplayLifeState.Dead;
                }
                else if(values[resource]<=threshold)
                    next=rule.AllowDowned && (tags&rule.PreventDownedTags)==0?GameplayLifeState.Downed:GameplayLifeState.Dead;
                if(next!=lifeStates[owner])TransitionLife(owner,next,rule);
            }
        }
        private void TransitionLife(int owner,GameplayLifeState next,GameplayLifeRuleDef rule)
        {
            if(effectNext==ulong.MaxValue)throw new SimBoundaryException("M0C_LIFE_TOKEN_EXHAUSTED");
            ulong token=effectNext;effectNext++;RefreshStatusMeta();
            var previous=lifeStates[owner];StableId id=owners[owner];
            ownerSum=unchecked(ownerSum-RowLeaf(3,owner));
            if(hasDownedTask[owner])sim.Scheduler.CancelStable(downedTasks[owner]);
            hasDownedTask[owner]=false;downedTasks[owner]=default;downedDue[owner]=0;
            lifeStates[owner]=next;lifeEntered[owner]=sim.Clock.Tick;
            if(next==GameplayLifeState.Downed)
            {
                int cast=FindAbilityCast(id);if(cast>=0)DeleteAbilityCast(cast);
                downedDue[owner]=GameplayMath.FutureTick(sim.Clock.Tick,rule.DownedTicks,ref timeSaturations);
                downedTasks[owner]=sim.Scheduler.ScheduleFor(downedDue[owner],0,id,token,new StableSimEvent(GameplayEvents.DownedExpiry,target:new EntityLink(id)));
                hasDownedTask[owner]=true;
            }
            else if(next==GameplayLifeState.Dead)
            {
                CleanupAbilities(id);CleanupStatuses(id,41);
                // Retain the authoritative Dead row for structural consumers. Resource
                // regeneration stops too; later packets cannot change a dead owner.
                for(int r=0;r<resourceCount;r++)if(resourceOwners[r].Equals(id) && hasTask[r])
                {resourceSum=unchecked(resourceSum-RowLeaf(4,r));sim.Scheduler.CancelStable(tasks[r]);hasTask[r]=false;tasks[r]=default;resourceSum=unchecked(resourceSum+RowLeaf(4,r));}
            }
            ownerSum=unchecked(ownerSum+RowLeaf(3,owner));
            QueueNotice(41,id,new GameplayApplyKey(id,0,2,id,token,0),3,
                new StableSimEvent(GameplayEvents.Life,target:new EntityLink(id),magnitude:SimFix.FromRaw((int)next),radius:SimFix.FromRaw((int)previous)));
        }
        private void WriteLife(SimStateWriter w,int index)
        {w.Int32((int)lifeStates[index]);w.Int64(lifeEntered[index]);w.Int64(downedDue[index]);w.Bool(hasDownedTask[index]);if(hasDownedTask[index])w.TaskKeyValue(downedTasks[index]);}
        private void ReadLife(SimStateReader row,int index,long tick)
        {
            int state=row.Int32();long entered=row.Int64(),due=row.Int64();bool task=row.Bool();
            int ruleIndex=schema.FindLifeRule(archetypes[index]);
            SimStateReader.Require(state>=0 && state<=2 && entered>=0 && entered<=tick && (state==0 || entered>0),"M0C_LIFE_STATE");
            SimStateReader.Require(ruleIndex>=0 || state==0 && entered==0 && due==0 && !task,"M0C_LIFE_DISABLED");
            if(state==1)
            {
                SimStateReader.Require(ruleIndex>=0,"M0C_LIFE_RULE");var rule=schema.LifeRuleAt(ruleIndex);
                SimStateReader.Require(rule.AllowDowned && task && due>tick && due<=long.MaxValue-1
                    && due==StatusProtocolFuture(entered,(ulong)rule.DownedTicks),"M0C_DOWNED_TIME");
            }
            else SimStateReader.Require(due==0 && !task,"M0C_LIFE_NO_TASK");
            lifeStates[index]=(GameplayLifeState)state;lifeEntered[index]=entered;downedDue[index]=due;hasDownedTask[index]=task;
            downedTasks[index]=task?row.ReadTaskKey():default;
            if(task)
            {
                TaskKey key=downedTasks[index];
                SimStateReader.Require(key.DueTick==due && key.OriginDomain==0 && key.Origin.Equals(owners[index]) && key.Kind<effectNext
                    && sim.Scheduler.TryGetPendingStable(key,out StableScheduledTask scheduled)
                    && scheduled.Event.Type.Equals(GameplayEvents.DownedExpiry) && !scheduled.Event.Source.HasValue
                    && scheduled.Event.Target.HasValue && scheduled.Event.Target.Id.Equals(owners[index])
                    && scheduled.Event.Tag==0 && scheduled.Event.Payload.Magnitude.Raw==0 && scheduled.Event.Payload.Radius.Raw==0
                    && scheduled.Event.Payload.Position.Equals(default(SimVec3)) && scheduled.Event.Payload.Aux.Equals(default(SimVec3)),"M0C_DOWNED_RESTORE");
            }
        }
        private void ValidateRestoredLife(long tick)
        {
            for(int owner=0;owner<ownerCount;owner++)
            {
                int ri=schema.FindLifeRule(archetypes[owner]);if(ri<0)continue;
                var rule=schema.LifeRuleAt(ri);long health=Read(owners[owner],rule.ResourceKey);
                long threshold=schema.ResourceAt(schema.FindResource(rule.ResourceKey)).LifeThresholdRaw;ulong tags=LifeTags(owner);
                if(tick>0 && !IsDead(owner))
                {
                    SimStateReader.Require((tags&rule.TerminateTags)==0,"M0C_LIFE_UNSETTLED_TERMINATION");
                    SimStateReader.Require(lifeStates[owner]==GameplayLifeState.Downed
                        ?!(health>threshold && (tags&rule.RescueTags)==rule.RescueTags):health>threshold,"M0C_LIFE_UNSETTLED_RESOURCE");
                }
                int cast=FindAbilityCast(owners[owner]);
                if(lifeStates[owner]==GameplayLifeState.Downed && cast>=0)
                {
                    SimStateReader.Require(schema.AbilityAt(schema.FindAbility(abilityCasts[cast].AbilityKey)).AllowWhileDowned,"M0C_DOWNED_CAST");
                    SimStateReader.Require(abilityCasts[cast].SourceSnapshot.CapturedTick>lifeEntered[owner],"M0C_DOWNED_CAST_HISTORY");
                }
                if(lifeStates[owner]==GameplayLifeState.Active && lifeEntered[owner]>0 && cast>=0
                    && abilityCasts[cast].SourceSnapshot.CapturedTick<=lifeEntered[owner])
                    SimStateReader.Require(schema.AbilityAt(schema.FindAbility(abilityCasts[cast].AbilityKey)).AllowWhileDowned,"M0C_RECOVERED_CAST_HISTORY");
                if(IsDead(owner))
                {
                    SimStateReader.Require(FindAbilityCast(owners[owner])<0,"M0C_DEAD_CAST");
                    for(int i=0;i<cooldownCount;i++)SimStateReader.Require(!abilityCooldowns[i].Owner.Id.Equals(owners[owner]),"M0C_DEAD_COOLDOWN");
                    for(int i=0;i<statusCount;i++)SimStateReader.Require(!statuses[i].Target.Id.Equals(owners[owner]),"M0C_DEAD_STATUS");
                }
                if(hasDownedTask[owner])
                {
                    for(int i=0;i<owner;i++)SimStateReader.Require(!hasDownedTask[i] || downedTasks[i].Kind!=downedTasks[owner].Kind,"M0C_LIFE_TOKEN_ALIAS");
                    for(int i=0;i<statusCount;i++)SimStateReader.Require(statuses[i].Key.Token!=downedTasks[owner].Kind
                        && (statuses[i].Attribution.ProducerKind==0 || statuses[i].Attribution.ProducerToken!=downedTasks[owner].Kind
                        || statuses[i].Attribution.ProducerKind==2 && statuses[i].Attribution.Producer.Equals(owners[owner])),"M0C_LIFE_TOKEN_ALIAS");
                }
            }
            sim.Scheduler.ValidateGameplayLife(downedTasks,hasDownedTask,ownerCount);
            SimStateReader.Require(MissingLifeTimers()<=sim.Scheduler.Capacity-sim.Scheduler.PendingCount-sim.Scheduler.DueCount-MissingStatusTimers()-heldAbilityTimers,"M0C_LIFE_FUTURE_CAPACITY");
        }
    }
    public sealed partial class SimScheduler
    {
        internal void ValidateGameplayLife(TaskKey[] keys,bool[] present,int length)
        {
            int matched=0,expected=0;for(int i=0;i<length;i++)if(present[i])expected++;
            for(int slot=0;slot<tasks.Length;slot++)
            {
                if(taskStates[slot]==0 || !stableTasks[slot].Event.Type.Equals(GameplayEvents.DownedExpiry))continue;
                int found=0;for(int i=0;i<length;i++)if(present[i] && keys[i].Equals(stableTasks[slot].Key))found++;
                SimStateReader.Require(found==1,"M0C_LIFE_ORPHAN_TASK");matched++;
            }
            SimStateReader.Require(matched==expected,"M0C_LIFE_TASK_COUNT");
        }
    }
}
