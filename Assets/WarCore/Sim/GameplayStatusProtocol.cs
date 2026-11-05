namespace WarCore.Sim
{
    internal sealed partial class GameplayState
    {
        private void RefreshStatusMeta() { metaSum=Leaf(1,effectNext,0); }
        private static void WriteWeakStamp(SimStateWriter w,GameplayStamp stamp)
        { Stamp(w,stamp.Id,stamp.Generation); }
        private GameplayStamp ReadWeakStamp(SimStateReader r)
        {
            SimStateReader.Require(r.Bool(),"M0C_STATUS_SOURCE_REQUIRED");
            StableId id=new StableId(r.UInt64());ulong generation=r.UInt64();
            // A weak source may be retired, but its recorded life cannot be newer than the durable placed cursor.
            // Non-Placed identities have no restorable life cursor and therefore always use generation zero.
            SimStateReader.Require(generation<=ushort.MaxValue && generation<=Generation(id),"M0C_STATUS_SOURCE_GENERATION");
            return new GameplayStamp(id,(ushort)generation);
        }
        private static void WriteApplyKey(SimStateWriter w,GameplayApplyKey key,StableId target)
        {
            w.UInt64(key.Source.Value);w.UInt64(key.CastToken);w.UInt64(key.ProducerKind);
            w.UInt64(key.Producer.Value);w.UInt64(key.ProducerToken);w.UInt64(key.ActionOrdinal);
            w.UInt64(key.PulseOrdinal);w.UInt64(target.Value);
        }
        private static GameplayApplyKey ReadApplyKey(SimStateReader r,StableId target)
        {
            StableId source=new StableId(r.UInt64());ulong cast=r.UInt64(),kind=r.UInt64();
            StableId producer=new StableId(r.UInt64());ulong token=r.UInt64(),action=r.UInt64(),pulse=r.UInt64();
            SimStateReader.Require(kind<=2 && action<=uint.MaxValue && pulse<=uint.MaxValue
                && r.UInt64()==target.Value,"M0C_STATUS_APPLY_KEY");
            return new GameplayApplyKey(source,cast,kind,producer,token,(uint)action,(uint)pulse);
        }
        private void WriteSourceSnapshot(SimStateWriter w,GameplayStatusRow row,GameplayStatusDef def)
        {
            if(row.SourceSnapshot.AttributeCount!=0 || row.SourceSnapshot.ActionCaptureCount!=0)
            {WriteFullSourceSnapshot(w,row.SourceSnapshot);return;}
            WriteWeakStamp(w,row.SourceSnapshot.Source);w.UInt64(0);w.Int64(row.SourceSnapshot.CapturedTick);
            // S2 has no mutable attack attributes. The frozen definition owns every periodic action.
            w.Int32(0);w.Int32(def.PeriodicActionCount);
            for(int i=0;i<def.PeriodicActionCount;i++)
            {
                GameplayPeriodicAction action=def.PeriodicActionAt(i);
                w.UInt64(def.Key);w.UInt64(action.Ordinal);w.UInt64(action.IsDamage?action.Definition:0);
                w.UInt64(action.IsDamage?schema.DamageTypeAt(schema.FindDamageType(action.Definition)).PipelineKey:0);
            }
        }
        private GameplaySourceSnapshot ReadSourceSnapshot(SimStateReader r,GameplayStatusDef def,GameplayStamp source,long tick)
        {
            GameplayStamp repeated=ReadWeakStamp(r);ulong ability=r.UInt64();long captured=r.Int64();
            int attributes=r.Count(GameplaySourceSnapshot.MaximumAttributes);
            SimStateReader.Require(repeated.Id.Equals(source.Id) && repeated.Generation==source.Generation
                && captured>0 && captured<=tick,"M0C_STATUS_SOURCE_SNAPSHOT");
            if(attributes!=0 || ability!=0)return ReadFullSourceBody(r,tick,repeated,ability,captured,attributes);
            return ReadZeroAttributeStatusSource(r,def,repeated,captured);
        }
        private GameplaySourceSnapshot ReadZeroAttributeStatusSource(SimStateReader r,GameplayStatusDef def,GameplayStamp source,long captured)
        {
            // Ability zero is a valid content key. With no attributes, distinguish the
            // legacy S2 capture and complete S3 capture by their frozen binding sets.
            int maximum=def.PeriodicActionCount>GameplaySourceSnapshot.MaximumActions?def.PeriodicActionCount:GameplaySourceSnapshot.MaximumActions;
            int count=r.Count(maximum),ability=schema.FindAbility(0);bool legacy=count==def.PeriodicActionCount;
            var expected=new GameplaySourceSnapshot(source,captured,0);
            bool complete=ability>=0 && schema.ArchetypeAt(schema.FindArchetype(schema.AbilityAt(ability).ArchetypeKey)).ResourceCount==0;
            if(complete)expected=BuildExpectedAbilityBindings(expected,schema.AbilityAt(ability));
            complete=complete && count==expected.ActionCaptureCount;
            var result=new GameplaySourceSnapshot(source,captured,0);
            for(int i=0;i<count;i++)
            {
                ulong key=r.UInt64(),ordinal=r.UInt64(),damage=r.UInt64(),pipeline=r.UInt64();
                if(legacy)
                {
                    var action=def.PeriodicActionAt(i);ulong expectedDamage=action.IsDamage?action.Definition:0;
                    ulong expectedPipeline=action.IsDamage?schema.DamageTypeAt(schema.FindDamageType(action.Definition)).PipelineKey:0;
                    legacy=key==def.Key && ordinal==action.Ordinal && damage==expectedDamage && pipeline==expectedPipeline;
                }
                if(complete)
                {
                    var action=expected.ActionCaptureAt(i);
                    complete=key==action.DefinitionKey && ordinal==action.Ordinal && damage==action.DamageTypeKey && pipeline==action.PipelineKey;
                    if(complete)result=result.WithActionCapture(new GameplayCapturedAction(key,(uint)ordinal,damage,pipeline));
                }
            }
            SimStateReader.Require(legacy || complete,"M0C_STATUS_ACTION_CAPTURE");
            return complete?result:new GameplaySourceSnapshot(source,captured);
        }
        private void WriteStatusRow(SimStateWriter w,int index)
        {
            GameplayStatusRow row=statuses[index];GameplayStatusInstanceKey key=row.Key;
            GameplayStatusDef def=schema.StatusAt(schema.FindStatus(key.Definition));
            w.UInt64(key.Target.Value);w.UInt64(key.Definition);w.Bool(key.HasGroupSource);
            if(key.HasGroupSource){w.UInt64(key.GroupSource.Id.Value);w.UInt64(key.GroupSource.Generation);}
            w.UInt64(key.Token);Stamp(w,row.Target.Id,row.Target.Generation);w.UInt64(key.Definition);
            WriteApplyKey(w,row.Attribution,row.Target.Id);WriteWeakStamp(w,row.SourceSnapshot.Source);
            w.Int32(row.Stacks);w.Int64(row.StrengthRaw);w.Int64(row.StartTick);w.Int32((int)def.DurationKind);
            w.Int64(row.ExpiryTick);w.Int64(row.NextPulseTick);w.UInt64(row.PulseOrdinal);w.Int64(row.AbsorbRemainingRaw);
            WriteSourceSnapshot(w,row,def);
            w.Int32((row.HasExpiryTask?1:0)+(row.HasPulseTask?1:0));
            if(row.HasExpiryTask && row.HasPulseTask)
            {
                bool expiryFirst=row.ExpiryTask.CompareTo(row.PulseTask)<0;
                w.TaskKeyValue(expiryFirst?row.ExpiryTask:row.PulseTask);
                w.TaskKeyValue(expiryFirst?row.PulseTask:row.ExpiryTask);
            }
            else if(row.HasExpiryTask)w.TaskKeyValue(row.ExpiryTask);
            else if(row.HasPulseTask)w.TaskKeyValue(row.PulseTask);
        }
        private long StatusRowLength(int index)
        {
            GameplayStatusRow row=statuses[index];GameplayStatusDef def=schema.StatusAt(schema.FindStatus(row.Key.Definition));
            // Every count/enum is LE64. Required source/owner and optional group flags are bytes.
            long capture=row.SourceSnapshot.AttributeCount!=0 || row.SourceSnapshot.ActionCaptureCount!=0
                ?FullSourceLength(row.SourceSnapshot):49L+32L*def.PeriodicActionCount;
            return 203L+(row.Key.HasGroupSource?16:0)+capture
                +(row.HasExpiryTask?40:0)+(row.HasPulseTask?40:0);
        }
        private void ReadStatusRow(SimStateReader r,int index,long tick)
        {
            StableId target=new StableId(r.UInt64());ulong definition=r.UInt64();bool grouped=r.Bool();
            GameplayStamp group=default;
            if(grouped)
            {
                StableId groupId=new StableId(r.UInt64());ulong generation=r.UInt64();
                SimStateReader.Require(generation<=ushort.MaxValue,"M0C_STATUS_GROUP_GENERATION");
                group=new GameplayStamp(groupId,(ushort)generation);
            }
            ulong token=r.UInt64();var key=new GameplayStatusInstanceKey(target,definition,grouped,group,token);
            GameplayStamp owner=ReadStamp(r);ulong repeatedDefinition=r.UInt64();int definitionIndex=schema.FindStatus(definition);
            SimStateReader.Require(definitionIndex>=0 && owner.Id.Equals(target) && OwnerIndex(target)>=0
                && repeatedDefinition==definition && token<effectNext
                && (index==0 || statuses[index-1].Key.CompareTo(key)<0),"M0C_STATUS_KEY");
            GameplayStatusDef def=schema.StatusAt(definitionIndex);
            SimStateReader.Require(!def.HasAbsorbResource || ResourceIndex(target,def.AbsorbResourceKey)>=0,"M0C_STATUS_OWNER_RESOURCE");
            for(int i=0;i<def.PeriodicActionCount;i++)
            {
                GameplayPeriodicAction action=def.PeriodicActionAt(i);
                ulong resource=action.IsDamage?schema.DamageTypeAt(schema.FindDamageType(action.Definition)).ResourceKey:action.Definition;
                SimStateReader.Require(ResourceIndex(target,resource)>=0,"M0C_STATUS_OWNER_RESOURCE");
            }
            GameplayApplyKey attribution=ReadApplyKey(r,target);GameplayStamp source=ReadWeakStamp(r);
            SimStateReader.Require(source.Id.Equals(attribution.Source) && grouped==(def.StackScope==GameplayStackScope.TargetAndSource)
                && (!grouped || group.Id.Equals(source.Id) && group.Generation==source.Generation),"M0C_STATUS_ATTRIBUTION");
            int stacks=r.Int32();long strength=r.Int64(),start=r.Int64();ulong duration=r.UInt64();
            long expiry=r.Int64(),nextPulse=r.Int64();ulong pulse=r.UInt64();long absorb=r.Int64();
            SimStateReader.Require(stacks>=1 && stacks<=def.MaxStacks && (def.StackPolicy==GameplayStackPolicy.Stack || stacks==1)
                && strength>=0 && strength<=GameplayMath.MaxMagnitude && start>0 && start<=tick
                && duration==(ulong)def.DurationKind && duration!=0 && pulse<=uint.MaxValue
                && absorb>=0 && absorb<=GameplayMath.MaxMagnitude
                && (def.HasAbsorbResource || absorb==0),"M0C_STATUS_RANGE");
            if(def.DurationKind==GameplayStatusDuration.Timed)
                SimStateReader.Require(def.DurationTicks>0 && expiry>tick && expiry<=long.MaxValue-1,"M0C_STATUS_EXPIRY");
            else SimStateReader.Require(expiry==0,"M0C_STATUS_INFINITE_EXPIRY");
            GameplaySourceSnapshot snapshot=ReadSourceSnapshot(r,def,source,tick);
            bool nonrefreshing=def.StackPolicy==GameplayStackPolicy.Independent || def.StackPolicy==GameplayStackPolicy.Strongest;
            if(def.DurationKind==GameplayStatusDuration.Infinite) // S2_LA:duration
            {
                // Infinite rows carry no expiry or refresh flag. Captured > Start is
                // observable refresh evidence only for Refresh/Stack; absence is not proof.
                bool refreshed=!nonrefreshing && snapshot.CapturedTick>start;
                if(refreshed)SimStateReader.Require(start<=snapshot.CapturedTick,"M0C_STATUS_CAPTURE_TICK");
                else SimStateReader.Require(start<=tick && snapshot.CapturedTick<=tick,"M0C_STATUS_CAPTURE_TICK");
                if(nonrefreshing)SimStateReader.Require(snapshot.CapturedTick<=start,"M0C_STATUS_START_CAPTURE");
            }
            else if(expiry==long.MaxValue-1) // S2_LA:saturation
            {
                // Clamping destroys the inverse Expiry-Duration. Do not infer LA.
                SimStateReader.Require(start<=snapshot.CapturedTick && snapshot.CapturedTick<=tick,"M0C_STATUS_CAPTURE_TICK");
            }
            else
            {
                long lastApplication=expiry-def.DurationTicks;
                SimStateReader.Require(start<=lastApplication && lastApplication<=tick,"M0C_STATUS_EXPIRY_REACHABILITY");
                if(nonrefreshing)
                    SimStateReader.Require(start==lastApplication && snapshot.CapturedTick<=lastApplication,"M0C_STATUS_START_CAPTURE");
                else SimStateReader.Require(snapshot.CapturedTick<=lastApplication,"M0C_STATUS_CAPTURE_TICK");
                // Refresh/Stack preserve first Start and NextPulse; LA advances on application.
                // LA == Start describes a new or same-tick-refreshed instance.
            }
            if(def.PeriodTicks>0)
            {
                ulong delay=(ulong)(def.FirstPulseDelayTicks==0?1:def.FirstPulseDelayTicks);
                long first=StatusProtocolFuture(start,delay);
                // uint32 ordinal times positive int31 period is strictly below 2^63.
                ulong elapsed=pulse*(ulong)def.PeriodTicks;
                SimStateReader.Require(nextPulse==StatusProtocolFuture(first,elapsed),"M0C_STATUS_PULSE_REACHABILITY");
                if(pulse!=0)
                    SimStateReader.Require(StatusProtocolFuture(first,(pulse-1)*(ulong)def.PeriodTicks)<=tick,"M0C_STATUS_PULSE_HISTORY");
            }
            var row=new GameplayStatusRow { Key=key,Target=owner,Attribution=attribution,SourceSnapshot=snapshot,
                Stacks=stacks,StrengthRaw=strength,StartTick=start,ExpiryTick=expiry,NextPulseTick=nextPulse,
                PulseOrdinal=(uint)pulse,AbsorbRemainingRaw=absorb,AttributionTick=snapshot.CapturedTick };
            int taskCount=r.Count(2);TaskKey previous=default;
            for(int i=0;i<taskCount;i++)
            {
                TaskKey task=r.ReadTaskKey();StableScheduledTask scheduled=default;
                SimStateReader.Require((i==0 || previous.CompareTo(task)<0) && task.DueTick>tick
                    && task.OriginDomain==0 && task.Origin.Equals(target) && task.Kind==token
                    && sim.Scheduler.TryGetPendingStable(task,out scheduled),"M0C_STATUS_TASK_KEY");
                SimStateReader.Require(scheduled.Event.Target.HasValue && scheduled.Event.Target.Id.Equals(target)
                    && !scheduled.Event.Source.HasValue && scheduled.Event.Tag==0 && scheduled.Event.Payload.Magnitude.Raw==0
                    && scheduled.Event.Payload.Radius.Raw==0 && scheduled.Event.Payload.Position.Equals(default(SimVec3))
                    && scheduled.Event.Payload.Aux.Equals(default(SimVec3)),"M0C_STATUS_TASK_PAYLOAD");
                if(scheduled.Event.Type.Equals(GameplayEvents.StatusExpiry))
                {
                    SimStateReader.Require(!row.HasExpiryTask && def.DurationKind==GameplayStatusDuration.Timed
                        && task.DueTick==expiry,"M0C_STATUS_EXPIRY_TASK");
                    row.HasExpiryTask=true;row.ExpiryTask=task;
                }
                else
                {
                    SimStateReader.Require(scheduled.Event.Type.Equals(GameplayEvents.StatusPulse) && !row.HasPulseTask
                        && def.PeriodTicks>0 && task.DueTick==nextPulse,"M0C_STATUS_PULSE_TASK");
                    row.HasPulseTask=true;row.PulseTask=task;
                }
                previous=task;
            }
            SimStateReader.Require(row.HasExpiryTask==(def.DurationKind==GameplayStatusDuration.Timed),"M0C_STATUS_EXPIRY_TASK_REQUIRED");
            if(def.PeriodTicks==0)
                SimStateReader.Require(nextPulse==0 && pulse==0 && !row.HasPulseTask,"M0C_STATUS_NO_PERIOD");
            else
                SimStateReader.Require(nextPulse>tick && nextPulse<=long.MaxValue-1
                    && row.HasPulseTask==(expiry==0 || nextPulse<expiry),"M0C_STATUS_PULSE_STATE");
            statuses[index]=row;statusCount++;
        }
        private static long StatusProtocolFuture(long start,ulong duration)
        {
            long limit=long.MaxValue-1;
            return duration>(ulong)(limit-start)?limit:start+(long)duration;
        }
        private void ValidateRestoredStatuses(long tick)
        {
            for(int i=0;i<statusCount;i++)
            {
                GameplayStatusRow row=statuses[i];GameplayStatusDef def=schema.StatusAt(schema.FindStatus(row.Key.Definition));
                int groupCount=0;
                for(int j=0;j<statusCount;j++)
                {
                    GameplayStatusRow other=statuses[j];
                    SimStateReader.Require(i==j || row.Key.Token!=other.Key.Token,"M0C_STATUS_DUPLICATE_TOKEN");
                    if(row.Attribution.ProducerKind!=0 && row.Attribution.ProducerToken==other.Key.Token)
                        SimStateReader.Require(row.Attribution.ProducerKind==1 && row.Attribution.Producer.Equals(other.Target.Id),"M0C_STATUS_PRODUCER_IDENTITY");
                    if(row.Attribution.ProducerKind!=0 && other.Attribution.ProducerKind!=0
                        && row.Attribution.ProducerToken==other.Attribution.ProducerToken)
                        SimStateReader.Require(row.Attribution.ProducerKind==other.Attribution.ProducerKind
                            && row.Attribution.Producer.Equals(other.Attribution.Producer),"M0C_STATUS_PRODUCER_ALIAS");
                    if(row.Key.Target.Equals(other.Key.Target) && row.Key.Definition==other.Key.Definition
                        && row.Key.HasGroupSource==other.Key.HasGroupSource && row.Key.GroupSource.Id.Equals(other.Key.GroupSource.Id)
                        && row.Key.GroupSource.Generation==other.Key.GroupSource.Generation)groupCount++;
                }
                SimStateReader.Require(groupCount<=(def.StackPolicy==GameplayStackPolicy.Independent?def.MaxInstances:1),"M0C_STATUS_GROUP_CAPACITY");
                if(row.Attribution.ProducerKind!=0)
                    SimStateReader.Require(row.Attribution.ProducerToken<effectNext,"M0C_STATUS_PRODUCER_CURSOR");
            }
            sim.Scheduler.ValidateGameplayStatuses(statuses,statusCount);
            ValidateStatusReservations(tick);
        }
    }
    public sealed partial class SimScheduler
    {
        internal void ValidateGameplayStatuses(GameplayStatusRow[] rows,int length)
        {
            int matched=0,expected=0;
            for(int i=0;i<length;i++)expected+=(rows[i].HasExpiryTask?1:0)+(rows[i].HasPulseTask?1:0);
            for(int slot=0;slot<tasks.Length;slot++)
            {
                if(taskStates[slot]==0)continue;
                StableScheduledTask task=stableTasks[slot];
                if(!task.Event.Type.Equals(GameplayEvents.StatusExpiry) && !task.Event.Type.Equals(GameplayEvents.StatusPulse))continue;
                int found=0;
                for(int i=0;i<length;i++)
                {
                    if(rows[i].HasExpiryTask && rows[i].ExpiryTask.Equals(task.Key))found++;
                    if(rows[i].HasPulseTask && rows[i].PulseTask.Equals(task.Key))found++;
                }
                SimStateReader.Require(found==1,"M0C_STATUS_ORPHAN_TASK");matched++;
            }
            SimStateReader.Require(matched==expected,"M0C_STATUS_TASK_COUNT");
        }
    }
}
