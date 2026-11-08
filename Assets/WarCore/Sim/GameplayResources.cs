using System;
namespace WarCore.Sim
{
    internal sealed partial class GameplayState
    {
        private readonly Sim sim;
        private readonly ComponentType<GameplayBinding> binding;
        private readonly StableId[] owners, resourceOwners;
        private readonly ushort[] generations, resourceGenerations;
        private readonly ulong[] archetypes, resourceKeys;
        private readonly ulong[] castNext;
        private readonly long[] values, tickStart;
        private readonly bool[] hasTask, affected;
        private readonly TaskKey[] tasks;
        private int ownerCount, resourceCount,retiredCount;
        private readonly StableId[] retiredOwners;
        private long numericSaturations, rejectedResources, rejectedCapacity, timeSaturations;
        private readonly RandomStreamId criticalStream;
        private int OwnerIndex(StableId id)
        {
            int lo=0,hi=ownerCount-1;
            while(lo<=hi) { int mid=lo+(hi-lo)/2; int c=owners[mid].CompareTo(id); if(c==0)return mid; if(c<0)lo=mid+1;else hi=mid-1; }
            return ~lo;
        }
        private int ResourceIndex(StableId owner,ulong key)
        {
            int lo=0,hi=resourceCount-1;
            while(lo<=hi) { int mid=lo+(hi-lo)/2; int c=resourceOwners[mid].CompareTo(owner); if(c==0)c=resourceKeys[mid].CompareTo(key); if(c==0)return mid; if(c<0)lo=mid+1;else hi=mid-1; }
            return ~lo;
        }
        internal ushort Generation(StableId id)
        {
            int i=sim.Placed.Catalog.Find(id);
            return i<0?(ushort)0:sim.Placed.GenerationAt(i);
        }
        internal bool Contains(StableId owner) => OwnerIndex(owner)>=0;
        internal void Bind(EntityId entity,ulong archetype)
        {
            StableId id=sim.Entities.StableOf(entity);
            int a=schema.FindArchetype(archetype),at=OwnerIndex(id);
            if(a<0 || at>=0)throw new SimBoundaryException("M0C_GAMEPLAY_BINDING");
            GameplayArchetypeDef def=schema.ArchetypeAt(a);
            if(ownerCount==owners.Length || def.ResourceCount>values.Length-resourceCount)
                throw new SimBoundaryException("M0C_GAMEPLAY_CAPACITY");
            at=~at;
            for(int i=ownerCount;i>at;i--) { owners[i]=owners[i-1];generations[i]=generations[i-1];archetypes[i]=archetypes[i-1];castNext[i]=castNext[i-1];CopyLife(i-1,i); }
            owners[at]=id;generations[at]=Generation(id);archetypes[at]=archetype;ownerCount++;
            castNext[at]=(ulong)generations[at]<<48;ClearLife(at);
            ownerSum=unchecked(ownerSum+RowLeaf(3,at));
            for(int r=0;r<def.ResourceCount;r++)
            {
                ulong key=def.ResourceKeyAt(r); int insert=~ResourceIndex(id,key);
                for(int i=resourceCount;i>insert;i--)CopyResource(i-1,i);
                GameplayResourceDef rd=schema.ResourceAt(schema.FindResource(key));
                resourceOwners[insert]=id; resourceGenerations[insert]=Generation(id); resourceKeys[insert]=key;
                values[insert]=rd.InitialRaw;hasTask[insert]=false;tasks[insert]=default;resourceCount++;
                resourceSum=unchecked(resourceSum+RowLeaf(4,insert));
            }
            sim.Entities.Set(binding,entity,new GameplayBinding(archetype));
        }
        private void CopyResource(int from,int to)
        {
            resourceOwners[to]=resourceOwners[from];resourceGenerations[to]=resourceGenerations[from];resourceKeys[to]=resourceKeys[from];
            values[to]=values[from];hasTask[to]=hasTask[from];tasks[to]=tasks[from];tickStart[to]=tickStart[from];affected[to]=affected[from];
        }
        internal long Read(StableId owner,ulong key)
        {
            int i=ResourceIndex(owner,key);if(i<0)throw new SimBoundaryException("M0C_RESOURCE_MISSING");return values[i];
        }
        private void WriteValue(int i,long value)
        {
            resourceSum=unchecked(resourceSum-RowLeaf(4,i)); values[i]=value;
            resourceSum=unchecked(resourceSum+RowLeaf(4,i)); affected[i]=true;
        }
        private void Count(ref long count)
        {
            if(count==long.MaxValue)throw new SimBoundaryException("M0C_DIAGNOSTIC_EXHAUSTED"); count++;
        }
        private bool hasAdmission;
        private StableId admissionOwner;
        private uint admissionOrdinal;
        private void AdmissionOrder(GameplayStamp owner,uint ordinal)
        {
            int c=hasAdmission?admissionOwner.CompareTo(owner.Id):-1;
            if(hasAdmission && (c>0 || c==0 && ordinal<=admissionOrdinal))
                throw new SimBoundaryException("M0C_ADMISSION_ORDER: Q0 requires (SourceStableId, CommandOrdinal) order.");
            hasAdmission=true;admissionOwner=owner.Id;admissionOrdinal=ordinal;
        }
        internal GameplayFailure Spend(GameplayStamp target,ReadOnlySpan<GameplayCost> costs,uint ordinal)
        { AdmissionOrder(target,ordinal);return SpendCore(target,costs); }
        private GameplayFailure SpendCore(GameplayStamp target,ReadOnlySpan<GameplayCost> costs,bool commit=true)
        {
            GameplayFailure failure=ValidateTarget(target);if(failure!=GameplayFailure.None)return failure;
            if(costs.Length>GameplayMath.MaxModifiers)throw new SimBoundaryException("M0C_COST_COUNT");
            // Validate the entire list before choosing the resource failure reason.
            for(int i=0;i<costs.Length;i++)
            {
                GameplayMath.RequireMagnitude(costs[i].AmountRaw);
                if(i>0 && costs[i].ResourceKey<costs[i-1].ResourceKey)throw new SimBoundaryException("M0C_COST_ORDER");
                int row=ResourceIndex(target.Id,costs[i].ResourceKey);
                if(row<0)throw new SimBoundaryException("M0C_COST_RESOURCE");
                if(!schema.ResourceAt(schema.FindResource(costs[i].ResourceKey)).AllowSpend)throw new SimBoundaryException("M0C_COST_DISABLED");
            }
            for(int i=0;i<costs.Length;)
            {
                ulong key=costs[i].ResourceKey;FixedUInt128 total=default;
                do { total=total+new FixedUInt128((ulong)costs[i].AmountRaw);i++; }
                while(i<costs.Length && costs[i].ResourceKey==key);
                int row=ResourceIndex(target.Id,key);
                GameplayResourceDef def=schema.ResourceAt(schema.FindResource(key));
                if(total.CompareTo(new FixedUInt128((ulong)(values[row]-def.LowerRaw)))>0)
                { Count(ref rejectedResources);RefreshDiagnostics();return GameplayFailure.InsufficientResource; }
            }
            if(!commit)return GameplayFailure.None;
            for(int i=0;i<costs.Length;)
            {
                ulong key=costs[i].ResourceKey;FixedUInt128 total=default;
                do { total=total+new FixedUInt128((ulong)costs[i].AmountRaw);i++; }while(i<costs.Length && costs[i].ResourceKey==key);
                int row=ResourceIndex(target.Id,key);WriteValue(row,values[row]-(long)total.Low);
            }
            return GameplayFailure.None;
        }
        private GameplayFailure ValidateTarget(GameplayStamp target)
        {
            int owner=OwnerIndex(target.Id);
            if(owner<0 || IsDead(owner) || !sim.Entities.TryResolve(target.Id,out _))return GameplayFailure.TargetGone;
            return generations[owner]==target.Generation && Generation(target.Id)==target.Generation
                ? GameplayFailure.None:GameplayFailure.TargetLifeMismatch;
        }
        internal void BeginResources()
        {
            Cleanup();ValidateBindings();hasAdmission=false;ResetStatusReservations();
            for(int i=0;i<resourceCount;i++){tickStart[i]=values[i];affected[i]=false;}
            BeginLife();BeginStatuses();
            // Resource rows are in (OwnerStableId, ResourceKey) order before allocating scheduler counters.
            int needed=0;
            for(int i=0;i<resourceCount;i++)if(!hasTask[i] && !IsDead(OwnerIndex(resourceOwners[i])) && schema.ResourceAt(schema.FindResource(resourceKeys[i])).RegenPeriodTicks>0)needed++;
            if(needed>sim.Scheduler.Capacity-sim.Scheduler.PendingCount-sim.Scheduler.DueCount-MissingStatusTimers()-heldAbilityTimers-MissingLifeTimers())
                throw new SimBoundaryException("M0C_REGEN_TIMER_CAPACITY");
            while(sim.Scheduler.TryDequeueStable(GameplayEvents.Regen,out StableScheduledTask due))
            {
                int row=ResourceIndex(due.Key.Origin,due.Key.Kind);
                if(row<0 || !hasTask[row] || !tasks[row].Equals(due.Key))throw new SimBoundaryException("M0C_REGEN_TASK");
                GameplayResourceDef def=schema.ResourceAt(schema.FindResource(resourceKeys[row]));
                long value=GameplayMath.ChangeResource(values[row],def.RegenAmountRaw,def.LowerRaw,def.UpperRaw,ref numericSaturations);
                WriteValue(row,value);
                resourceSum=unchecked(resourceSum-RowLeaf(4,row));hasTask[row]=false;tasks[row]=default;
                resourceSum=unchecked(resourceSum+RowLeaf(4,row));
            }
            for(int i=0;i<resourceCount;i++)
            {
                GameplayResourceDef def=schema.ResourceAt(schema.FindResource(resourceKeys[i]));
                if(hasTask[i] || IsDead(OwnerIndex(resourceOwners[i])) || def.RegenPeriodTicks==0)continue;
                long due=GameplayMath.FutureTick(sim.Clock.Tick,def.RegenPeriodTicks,ref timeSaturations);
                resourceSum=unchecked(resourceSum-RowLeaf(4,i));
                tasks[i]=sim.Scheduler.ScheduleFor(due,0,resourceOwners[i],resourceKeys[i],
                    new StableSimEvent(GameplayEvents.Regen,target:new EntityLink(resourceOwners[i])));
                hasTask[i]=true;resourceSum=unchecked(resourceSum+RowLeaf(4,i));
            }
            RefreshDiagnostics();
        }
        internal void ValidateBindings()
        {
            if(!Nonempty)return;
            for(int i=0;i<ownerCount;i++)
            {
                if(!sim.Entities.TryResolve(owners[i],out EntityRef entity)
                    || !sim.Entities.TryGet(binding,(EntityId)entity,out GameplayBinding bound) || bound.ArchetypeKey!=archetypes[i])
                    throw new SimBoundaryException("M0C_GAMEPLAY_BINDING_CHANGED");
            }
            foreach(EntityId entity in sim.Entities.CaptureStableEntities())
                if(sim.Entities.TryGet(binding,entity,out GameplayBinding bound) && OwnerIndex(sim.Entities.StableOf(entity))<0)
                    throw new SimBoundaryException("M0C_ORPHAN_BINDING");
        }
        internal void Retired(StableId id)
        {
            if(OwnerIndex(id)<0)return;
            if(retiredCount==retiredOwners.Length)throw new SimBoundaryException("M0C_RETIRE_CAPACITY");
            retiredOwners[retiredCount++]=id;
        }
        internal void Cleanup()
        {
            ValueOrder.Sort(retiredOwners.AsSpan(0,retiredCount));
            for(int at=0;at<retiredCount;at++)
            {
                int owner=OwnerIndex(retiredOwners[at]);if(owner<0)continue;
                StableId id=owners[owner];if(hasDownedTask[owner])sim.Scheduler.CancelStable(downedTasks[owner]); CleanupAbilities(id);CleanupStatuses(id); int start=~ResourceIndex(id,0);
                if(start<0)start=ResourceIndex(id,0);
                int end=start;
                while(end<resourceCount && resourceOwners[end].Equals(id))
                { if(hasTask[end])sim.Scheduler.CancelStable(tasks[end]);resourceSum=unchecked(resourceSum-RowLeaf(4,end));end++; }
                int removed=end-start;
                for(int i=end;i<resourceCount;i++)CopyResource(i,i-removed);
                resourceCount-=removed;ownerSum=unchecked(ownerSum-RowLeaf(3,owner));
                for(int i=owner+1;i<ownerCount;i++){owners[i-1]=owners[i];generations[i-1]=generations[i];archetypes[i-1]=archetypes[i];castNext[i-1]=castNext[i];CopyLife(i,i-1);}
                ownerCount--;
            }
            retiredCount=0;
        }
    }
}
