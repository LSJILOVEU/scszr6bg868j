using System;
namespace WarCore.Sim
{
    internal sealed partial class GameplayState
    {
        private GameplayFailure ValidateStatusCommand(GameplayCommand item)
        {
            // Status and life producer identities are reserved for internal emissions.
            if(item.Key.ProducerKind!=0)throw new SimBoundaryException("M0C_STATUS_PRODUCER_ADMISSION");
            GameplayFailure f=ValidateTarget(item.Target);if(f!=GameplayFailure.None)return f;
            if(item.Kind==3){if(item.Maximum<0 || item.Maximum>GameplayMath.MaxPackets)throw new SimBoundaryException("M0C_DISPEL_COUNT");return GameplayFailure.None;}
            int di=schema.FindStatus(item.Definition);if(di<0)throw new SimBoundaryException("M0C_STATUS_DEFINITION");
            GameplayStatusDef d=schema.StatusAt(di);
            if(item.Kind==2){if(item.Amount!=-1)GameplayMath.RequireMagnitude(item.Amount);}
            else if(item.Kind==4){GameplayMath.RequireMagnitude(item.Amount);if(!d.HasAbsorbResource)throw new SimBoundaryException("M0C_ABSORB_DEFINITION");}
            else throw new SimBoundaryException("M0C_STATUS_ACTION_KIND");
            if(d.HasAbsorbResource && ResourceIndex(item.Target.Id,d.AbsorbResourceKey)<0)throw new SimBoundaryException("M0C_ABSORB_RESOURCE");
            for(int i=0;i<d.PeriodicActionCount;i++)
            {var a=d.PeriodicActionAt(i);ulong resource=a.IsDamage?schema.DamageTypeAt(schema.FindDamageType(a.Definition)).ResourceKey:a.Definition;
                if(ResourceIndex(item.Target.Id,resource)<0)throw new SimBoundaryException("M0C_STATUS_ACTION_RESOURCE");}
            if(item.Snapshot.CapturedTick!=0 && (item.Snapshot.CapturedTick<0 || item.Snapshot.CapturedTick>sim.Clock.Tick || !item.Snapshot.Source.Id.Equals(item.Key.Source)
                || item.Snapshot.Source.Generation!=Generation(item.Key.Source)))
                throw new SimBoundaryException("M0C_SOURCE_SNAPSHOT");
            // A delayed capture can be unrepresentable once expiry reaches the time limit.
            // Strongest is checked only after choosing its actual winner in ApplyStatus.
            if(d.StackPolicy!=GameplayStackPolicy.Strongest && item.Snapshot.CapturedTick!=0)
            {
                long start=sim.Clock.Tick;
                if(d.StackPolicy!=GameplayStackPolicy.Independent)
                    for(int i=0;i<statusCount;i++)if(SameGroup(statuses[i],item,d)){start=statuses[i].StartTick;break;}
                RequireSaturatedStatusCapture(item,d,start);
            }
            return GameplayFailure.None;
        }
        private void RequireSaturatedStatusCapture(GameplayCommand item,GameplayStatusDef definition,long start)
        {
            if(definition.DurationKind==GameplayStatusDuration.Timed && definition.DurationTicks>0
                && definition.DurationTicks>=long.MaxValue-1-sim.Clock.Tick && item.Snapshot.CapturedTick<start)
                throw new SimBoundaryException("M0C_STATUS_SATURATED_CAPTURE");
        }
        private GameplayCommand CaptureStatus(GameplayCommand item)
        {
            if(item.Kind!=2 && item.Kind!=4)return item;
            GameplaySourceSnapshot snapshot=item.Snapshot.CapturedTick==0
                ?new GameplaySourceSnapshot(new GameplayStamp(item.Key.Source,Generation(item.Key.Source)),sim.Clock.Tick):item.Snapshot;
            return new GameplayCommand(item.Kind,item.Target,item.Key,item.Definition,item.Amount,item.Maximum,snapshot);
        }
        private bool SameGroup(GameplayStatusRow row,GameplayCommand item,GameplayStatusDef d)
        {return row.Target.Id.Equals(item.Target.Id) && row.Key.Definition==d.Key &&
            (d.StackScope==GameplayStackScope.Target || row.Key.GroupSource.Id.Equals(item.Key.Source) && row.Key.GroupSource.Generation==CapturedSource(item).Generation);}
        private bool SameGroup(GameplayCommand a,GameplayCommand b,GameplayStatusDef d)
        {return a.Target.Id.Equals(b.Target.Id) && a.Definition==b.Definition &&
            (d.StackScope==GameplayStackScope.Target || a.Key.Source.Equals(b.Key.Source) && CapturedSource(a).Generation==CapturedSource(b).Generation);}
        private GameplayStamp CapturedSource(GameplayCommand item)=>item.Snapshot.CapturedTick==0
            ?new GameplayStamp(item.Key.Source,Generation(item.Key.Source)):item.Snapshot.Source;
        private int reservedStatusCreates,reservedStatusTimers;
        private long reservedStatusPulses;
        private int candidateStatusCreates,candidateStatusTimers;
        private long candidateStatusPulses;
        private void ResetStatusReservations(){reservedStatusCreates=reservedStatusTimers=0;reservedStatusPulses=0;}
        private void CommitStatusReservations()
        {reservedStatusCreates=candidateStatusCreates;reservedStatusTimers=candidateStatusTimers;reservedStatusPulses=candidateStatusPulses;}
        private GameplayFailure ReserveStatuses(ReadOnlySpan<GameplayAction> actions)
        {
            int creates=reservedStatusCreates,timers=reservedStatusTimers;long pulseAdded=reservedStatusPulses;
            long pulses=0;for(int i=0;i<statusCount;i++)pulses+=schema.StatusAt(schema.FindStatus(statuses[i].Key.Definition)).PeriodicActionCount;
            for(int i=0;i<actions.Length;i++)
            {
                GameplayCommand item=actions[i].Command;if(item.Kind!=2 && item.Kind!=4)continue;
                GameplayStatusDef d=schema.StatusAt(schema.FindStatus(item.Definition));
                if(d.DurationKind==GameplayStatusDuration.Instant || d.DurationKind==GameplayStatusDuration.Timed && d.DurationTicks==0)continue;
                int group=0;for(int j=0;j<statusCount;j++)if(SameGroup(statuses[j],item,d))group++;
                for(int j=0;j<commandCount;j++){GameplayCommand prior=commands[j];if((prior.Kind==2 || prior.Kind==4) && SameGroup(prior,item,d))group++;}
                for(int j=0;j<i;j++){GameplayCommand prior=actions[j].Command;if((prior.Kind==2 || prior.Kind==4) && SameGroup(prior,item,d))group++;}
                bool createsRow=d.StackPolicy==GameplayStackPolicy.Independent || group==0;
                if(d.StackPolicy==GameplayStackPolicy.Independent && group>=d.MaxInstances)return GameplayFailure.StatusCapacity; // S2_MUTATION:independent_cap
                if(createsRow){creates++;timers+=(d.DurationKind==GameplayStatusDuration.Timed?1:0)+(d.PeriodTicks>0?1:0);pulseAdded+=d.PeriodicActionCount;}
            }
            if((ulong)creates>ulong.MaxValue-effectNext)throw new SimBoundaryException("M0C_STATUS_TOKEN_EXHAUSTED");
            if(creates>statuses.Length-statusCount-heldAbilityStatuses || pulses+pulseAdded+heldAbilityCommands>commands.Length || pulses+pulseAdded+heldAbilityCommands>notices.Length-resourceCount
                || timers+MissingStatusTimers()+heldAbilityTimers+MissingLifeTimers()>sim.Scheduler.Capacity-sim.Scheduler.PendingCount-sim.Scheduler.DueCount)return GameplayFailure.StatusCapacity;
            candidateStatusCreates=creates;candidateStatusTimers=timers;candidateStatusPulses=pulseAdded;
            return GameplayFailure.None;
        }
        private int MissingStatusTimers()
        {
            int missing=0;
            for(int i=0;i<statusCount;i++)
            {
                GameplayStatusRow r=statuses[i];GameplayStatusDef d=schema.StatusAt(schema.FindStatus(r.Key.Definition));
                missing+=(d.DurationKind==GameplayStatusDuration.Timed?1:0)+(d.PeriodTicks>0?1:0)-(r.HasExpiryTask?1:0)-(r.HasPulseTask?1:0);
            }
            return missing;
        }
        private void ValidateStatusReservations(long tick)
        {
            long pulses=0;for(int i=0;i<statusCount;i++)pulses+=schema.StatusAt(schema.FindStatus(statuses[i].Key.Definition)).PeriodicActionCount;
            SimStateReader.Require(pulses<=commands.Length && pulses<=notices.Length-resourceCount
                && MissingStatusTimers()+MissingLifeTimers()<=sim.Scheduler.Capacity-sim.Scheduler.PendingCount-sim.Scheduler.DueCount,"M0C_STATUS_FUTURE_CAPACITY");
        }
        internal GameplayFailure AdmitStatus(GameplayCommand item,uint ordinal)
        {
            GameplayStamp source=new GameplayStamp(item.Key.Source,Generation(item.Key.Source));AdmissionOrder(source,ordinal);
            GameplayFailure f=ValidateTarget(source);if(f!=GameplayFailure.None)return f;
            return EnqueueStatus(item);
        }
        private GameplayFailure EnqueueStatus(GameplayCommand item)
        {
            GameplayFailure f=ValidateStatusCommand(item);if(f!=GameplayFailure.None)return f;
            for(int i=0;i<commandCount;i++)if(commands[i].SameApplication(item))throw new SimBoundaryException("M0C_DUPLICATE_APPLY_KEY");
            if(commandCount>=commands.Length-heldAbilityCommands || commandCount>=notices.Length-resourceCount-heldAbilityCommands)throw new SimBoundaryException("M0C_RELEASE_CAPACITY");
            Span<GameplayAction> one=stackalloc GameplayAction[1];one[0]=GameplayAction.FromCommand(item);
            f=ReserveStatuses(one);if(f!=GameplayFailure.None){Count(ref statusCapacityRejections);RefreshDiagnostics();return f;}
            CommitStatusReservations();commands[commandCount++]=CaptureStatus(item);return GameplayFailure.None;
        }
    }
}
