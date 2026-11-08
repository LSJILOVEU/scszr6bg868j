using System;
namespace WarCore.Sim
{
    internal sealed partial class GameplayState
    {
        private readonly GameplayCommand[] commands;
        private readonly StableSimEvent[] notices;
        private int commandCount, noticeCount;
        internal GameplayFailure Enqueue(bool damage, GameplayStamp target, GameplayApplyKey key, ulong definition, long amount)
        {
            if (damage)
                GameplayMath.RequireMagnitude(amount);
            else
                GameplayMath.RequireSignedMagnitude(amount);
            int def = damage ? schema.FindDamageType(definition) : schema.FindResource(definition);
            if (def < 0)
                throw new SimBoundaryException("M0C_ACTION_DEFINITION");
            GameplayFailure failure = ValidateTarget(target);
            if (failure != GameplayFailure.None)
                return failure;
            ulong resource = damage ? schema.DamageTypeAt(def).ResourceKey : definition;
            if (ResourceIndex(target.Id, resource) < 0)
                throw new SimBoundaryException("M0C_ACTION_RESOURCE");
            var item = new GameplayCommand(damage, target, key, definition, amount);
            // Complete duplicates are protocol errors; discovery precedes any Q2 resource/RNG writes.
            for (int i = 0; i < commandCount; i++)
                if (commands[i].SameApplication(item))
                    throw new SimBoundaryException("M0C_DUPLICATE_APPLY_KEY");
            if (commandCount >= commands.Length - heldAbilityCommands || commandCount >= notices.Length - resourceCount - heldAbilityCommands)
                throw new SimBoundaryException("M0C_RELEASE_CAPACITY: Released effects exceeded their reserved capacity.");
            commands[commandCount++] = item;
            return GameplayFailure.None;
        }
        internal GameplayFailure Submit(GameplayStamp owner, ReadOnlySpan<GameplayCost> costs, ReadOnlySpan<GameplayAction> actions, uint ordinal)
        {
            AdmissionOrder(owner, ordinal);
            if (actions.Length > GameplayMath.MaxPackets)
                throw new SimBoundaryException("M0C_ACTION_COUNT");
            // S1 released batches reserve their complete Q2 fan-out before any fee is written.
            GameplayFailure ownerFailure = ValidateTarget(owner);
            if (ownerFailure != GameplayFailure.None)
                return ownerFailure;
            for (int i = 0; i < actions.Length; i++)
            {
                GameplayCommand item = actions[i].Command;
                if (item.Kind > 1)
                {
                    GameplayFailure statusFailure = ValidateStatusCommand(item);
                    if (statusFailure != GameplayFailure.None)
                        return statusFailure;
                    if (!item.Key.Source.Equals(owner.Id))
                        throw new SimBoundaryException("M0C_STATUS_ADMISSION_SOURCE");
                }
                else
                {
                    if (item.Damage)
                        GameplayMath.RequireMagnitude(item.Amount);
                    else
                        GameplayMath.RequireSignedMagnitude(item.Amount);
                    int def = item.Damage ? schema.FindDamageType(item.Definition) : schema.FindResource(item.Definition);
                    if (def < 0)
                        throw new SimBoundaryException("M0C_ACTION_DEFINITION");
                    GameplayFailure targetFailure = ValidateTarget(item.Target);
                    if (targetFailure != GameplayFailure.None)
                        return targetFailure;
                    ulong resource = item.Damage ? schema.DamageTypeAt(def).ResourceKey : item.Definition;
                    if (ResourceIndex(item.Target.Id, resource) < 0)
                        throw new SimBoundaryException("M0C_ACTION_RESOURCE");
                }
                for (int j = 0; j < i; j++)
                    if (actions[j].Command.SameApplication(item))
                        throw new SimBoundaryException("M0C_DUPLICATE_APPLY_KEY");
                for (int j = 0; j < commandCount; j++)
                    if (commands[j].SameApplication(item))
                        throw new SimBoundaryException("M0C_DUPLICATE_APPLY_KEY");
            }
            GameplayFailure affordability = SpendCore(owner, costs, false);
            if (affordability != GameplayFailure.None)
                return affordability;
            if (actions.Length > commands.Length - commandCount - heldAbilityCommands || actions.Length > notices.Length - resourceCount - commandCount - heldAbilityCommands)
            {
                Count(ref rejectedCapacity);
                RefreshDiagnostics();
                return GameplayFailure.CommandCapacity;
            }
            GameplayFailure reserved = ReserveStatuses(actions);
            if (reserved != GameplayFailure.None)
            {
                Count(ref statusCapacityRejections);
                RefreshDiagnostics();
                return reserved;
            }
            GameplayFailure paid = SpendCore(owner, costs);
            if (paid != GameplayFailure.None)
                return paid;
            CommitStatusReservations();
            for (int i = 0; i < actions.Length; i++)
                commands[commandCount++] = CaptureStatus(actions[i].Command);
            return GameplayFailure.None;
        }
        internal void Settle()
        {
            SettleStatuses();
            InterruptAbilities();
            ValueOrder.Sort(commands.AsSpan(0, commandCount));
            noticeCount = 0;
            for (int i = 0; i < commandCount; i++)
            {
                GameplayCommand item = commands[i];
                if (item.Kind > 1)
                    continue;
                if (ValidateTarget(item.Target) != GameplayFailure.None)
                    continue;
                ulong resource = item.Damage ? schema.DamageTypeAt(schema.FindDamageType(item.Definition)).ResourceKey : item.Definition;
                int row = ResourceIndex(item.Target.Id, resource);
                GameplayResourceDef def = schema.ResourceAt(schema.FindResource(resource));
                long amount = item.Amount;
                bool critical = false;
                if (item.Damage)
                {
                    GameplayDamageTypeDef type = schema.DamageTypeAt(schema.FindDamageType(item.Definition));
                    GameplayPipelineDef pipeline = schema.PipelineAt(schema.FindPipeline(type.PipelineKey));
                    long roll = (pipeline.Spec.EnabledStages & GameplayDamageStages.Critical) != 0
                        ? sim.Rng.NextRegisteredInt(criticalStream, (int)GameplayMath.Scale) : 0;
                    GameplayArchetypeDef targetArchetype = schema.ArchetypeAt(schema.FindArchetype(archetypes[OwnerIndex(item.Target.Id)]));
                    int defense = targetArchetype.FindDefense(item.Definition);
                    long resistance = defense < 0 ? pipeline.Spec.ResistanceRaw : targetArchetype.DefenseAt(defense).ResistanceRaw;
                    GameplayDamageResult result = EvaluateStatusDamage(item, pipeline, amount, roll, resistance);
                    amount = AbsorbDamage(item.Target.Id, resource, result.AmountRaw);
                    critical = result.WasCritical;
                }
                long next = GameplayMath.ChangeResource(values[row], item.Damage ? -amount : amount, def.LowerRaw, def.UpperRaw, ref numericSaturations);
                WriteValue(row, next);
                QueueFact(item.Damage ? 30 : 31, item.Target.Id, item.Key, item.Damage ? 0 : 1, new StableSimEvent(item.Damage ? GameplayEvents.Damage : GameplayEvents.Resource,
                    new EntityLink(item.Key.Source), new EntityLink(item.Target.Id), magnitude: SimFix.FromRaw(amount),
                    radius: SimFix.FromRaw(next), tag: critical ? 1 : 0));
            }
            commandCount = 0;
            // This is Q3's future read boundary. S1 emits facts only and never changes life state.
            for (int i = 0; i < resourceCount; i++)
            {
                GameplayResourceDef def = schema.ResourceAt(schema.FindResource(resourceKeys[i]));
                if (!affected[i] || !def.IsLife)
                    continue;
                int crossing = tickStart[i] > def.LifeThresholdRaw && values[i] <= def.LifeThresholdRaw ? 1 :
                    tickStart[i] <= def.LifeThresholdRaw && values[i] > def.LifeThresholdRaw ? 2 : 0;
                QueueFact(40, resourceOwners[i], default, 2, new StableSimEvent(GameplayEvents.FinalLife, target: new EntityLink(resourceOwners[i]),
                    magnitude: SimFix.FromRaw(values[i]), radius: SimFix.FromRaw(def.LifeThresholdRaw), tag: crossing));
            }
            ResolveLife();
            RefreshDiagnostics();
            FlushNotices();
        }
    }
}
