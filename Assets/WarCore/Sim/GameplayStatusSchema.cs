namespace WarCore.Sim
{
    public sealed partial class GameplaySchema
    {
        public int FindStatus(ulong key)
        {int lo=0,hi=statuses.Length-1;while(lo<=hi){int m=lo+(hi-lo)/2;ulong k=statuses[m].Key;if(k==key)return m;if(k<key)lo=m+1;else hi=m-1;}return -1;}
        private void ValidateStatuses()
        {
            if(statuses.Length==0 && Capacities.Statuses!=0 || statuses.Length!=0 && Capacities.Statuses==0)
                throw new SimBoundaryException("M0C_STATUS_CAPACITY");
            for(int i=0;i<statuses.Length;i++)
            {
                GameplayStatusDef d=statuses[i];ValidateName(d.Name);GameplayMath.RequireMagnitude(d.StrengthRaw);
                if(i>0 && statuses[i-1].Key==d.Key || (int)d.DurationKind<0 || (int)d.DurationKind>2 || (int)d.StackPolicy<0 || (int)d.StackPolicy>3 || (int)d.StackScope<0 || (int)d.StackScope>1
                    || d.MaxStacks<1 || d.MaxStacks>GameplayMath.MaxModifiers || d.MaxInstances<1 || d.MaxInstances>GameplayMath.MaxPackets
                    || d.DurationKind==GameplayStatusDuration.Timed && d.DurationTicks<0
                    || d.DurationKind!=GameplayStatusDuration.Timed && d.DurationTicks!=0 || d.PeriodTicks<0 || d.FirstPulseDelayTicks<0
                    || d.PeriodTicks==0 && (d.FirstPulseDelayTicks!=0 || d.PeriodicActionCount!=0)
                    || d.PeriodTicks>0 && (d.PeriodicActionCount==0)
                    || d.DurationKind==GameplayStatusDuration.Instant && (d.PeriodTicks!=0 || d.ModifierCount!=0 || d.ImmunityTags!=0 || d.HasAbsorbResource)
                    || d.StackPolicy!=GameplayStackPolicy.Stack && d.MaxStacks!=1
                    || d.StackPolicy!=GameplayStackPolicy.Independent && d.MaxInstances!=1
                    || d.HasAbsorbResource && FindResource(d.AbsorbResourceKey)<0)
                    throw new SimBoundaryException("M0C_STATUS_DEFINITION");
                for(int j=0;j<i;j++)if(statuses[j].Name==d.Name)throw new SimBoundaryException("M0C_STATUS_NAME");
                // At the declared maximum all active modifiers must fit the S1 bounded list.
                ulong modifiers=(ulong)d.ModifierCount*(ulong)Capacities.Statuses;
                if(modifiers>(ulong)GameplayMath.MaxModifiers)throw new SimBoundaryException("M0C_STATUS_MODIFIER_CAPACITY");
                for(int j=0;j<d.ModifierCount;j++)
                {
                    var m=d.ModifierAt(j);if(FindDamageType(m.DamageType)<0 || j>0 && d.ModifierAt(j-1).Key==m.Key)
                        throw new SimBoundaryException("M0C_STATUS_MODIFIER");
                    switch(m.Stage){case GameplayModifierStage.PreFlat:case GameplayModifierStage.PostFlat:GameplayMath.RequireSignedMagnitude(m.MagnitudeRaw);break;
                        case GameplayModifierStage.Rate:GameplayMath.RequireRate(m.MagnitudeRaw);break;case GameplayModifierStage.Product:GameplayMath.RequireFactor(m.MagnitudeRaw);break;
                        default:throw new SimBoundaryException("M0C_STATUS_MODIFIER_STAGE");}
                }
                for(int j=0;j<d.PeriodicActionCount;j++)
                {
                    var a=d.PeriodicActionAt(j);if(j>0 && d.PeriodicActionAt(j-1).Ordinal==a.Ordinal)throw new SimBoundaryException("M0C_STATUS_ACTION_ORDINAL");
                    if(a.IsDamage){GameplayMath.RequireMagnitude(a.AmountRaw);if(FindDamageType(a.Definition)<0)throw new SimBoundaryException("M0C_STATUS_ACTION_DEFINITION");}
                    else {GameplayMath.RequireSignedMagnitude(a.AmountRaw);if(FindResource(a.Definition)<0)throw new SimBoundaryException("M0C_STATUS_ACTION_DEFINITION");}
                }
            }
            int largest=0;for(int i=0;i<statuses.Length;i++){int n=statuses[i].ModifierCount;if(n>largest)largest=n;}
            int pipeline=0;for(int i=0;i<pipelines.Length;i++)if(pipelines[i].ModifierCount>pipeline)pipeline=pipelines[i].ModifierCount;
            if((ulong)largest*(ulong)Capacities.Statuses+(ulong)pipeline>(ulong)GameplayMath.MaxModifiers)
                throw new SimBoundaryException("M0C_STATUS_MODIFIER_CAPACITY");
        }
        private void HashStatuses(StateHash h)
        {
            h.Add(statuses.Length);
            for(int i=0;i<statuses.Length;i++)
            {
                var d=statuses[i];h.Add(d.Key);h.Add(d.Name);h.Add((int)d.DurationKind);h.Add(d.DurationTicks);h.Add(d.PeriodTicks);h.Add(d.FirstPulseDelayTicks);
                h.Add((int)d.StackPolicy);h.Add((int)d.StackScope);h.Add(d.MaxStacks);h.Add(d.MaxInstances);h.Add(d.StrengthRaw);
                h.Add(d.Tags);h.Add(d.DispelTags);h.Add(d.ImmunityTags);h.Add(d.Priority);h.Add(d.HasAbsorbResource);h.Add(d.AbsorbResourceKey);h.Add(d.ModifierCount);
                for(int j=0;j<d.ModifierCount;j++){var m=d.ModifierAt(j);h.Add(m.Key);h.Add(m.DamageType);h.Add((int)m.Stage);h.Add(m.MagnitudeRaw);}
                h.Add(d.PeriodicActionCount);for(int j=0;j<d.PeriodicActionCount;j++){var a=d.PeriodicActionAt(j);h.Add((ulong)a.Ordinal);h.Add(a.IsDamage);h.Add(a.Definition);h.Add(a.AmountRaw);}
            }
        }
    }
}
