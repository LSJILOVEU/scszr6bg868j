namespace WarCore.Sim
{
    internal sealed partial class GameplayState
    {
        private const int TableCount = 9;
        private readonly GameplaySchema schema;
        private ulong metaSum;
        private ulong inputSum, ownerSum, resourceSum, diagnosticSum;
        private long inputTick;
        private readonly StateHash leafHash = new StateHash();
        private readonly SimStateWriter leafWriter;
        private bool Nonempty => schema.Capacities.Owners != 0;
        internal GameplayState(GameplaySchema schema, Sim sim)
        {
            this.schema = schema;
            this.sim = sim;
            metaSum = Leaf(1, 0, 0);
            inputSum = Leaf(2, 0, 0);
            int o = schema.Capacities.Owners, r = schema.Capacities.Resources;
            retiredOwners = new StableId[o];
            owners = new StableId[o];
            generations = new ushort[o];
            archetypes = new ulong[o];
            resourceOwners = new StableId[r];
            resourceGenerations = new ushort[r];
            resourceKeys = new ulong[r];
            values = new long[r];
            tickStart = new long[r];
            hasTask = new bool[r];
            affected = new bool[r];
            tasks = new TaskKey[r];
            commands = new GameplayCommand[schema.Capacities.Commands];
            notices = new StableSimEvent[schema.Capacities.Notices];
            InitStatuses();
            InitAbilities();
            castNext = new ulong[o];
            InitLife();
            leafWriter = new SimStateWriter(sim.Entities, hash: leafHash);
            if (Nonempty)
            {
                binding = sim.Entities.Register<GameplayBinding>("warcore.gameplay-binding", new GameplayBindingSerializer());
                criticalStream = sim.Rng.Stream(schema.CriticalStream);
                RefreshDiagnostics();
                sim.Entities.Gameplay = this;
            }
        }
        private static ulong Leaf(ulong domain, ulong first, ulong second)
        {
            ulong hash = 14695981039346656037UL;
            StableEntityKey.Word(ref hash, domain);
            StableEntityKey.Word(ref hash, first);
            StableEntityKey.Word(ref hash, second);
            return StableEntityKey.Mix(hash);
        }
        internal void CaptureInput(long tick)
        {
            if (tick < 0 || commandCount != 0 || s4NoticeCount != 0 || noticeCount != 0)
                throw new SimBoundaryException("M0C_GAMEPLAY_TICK");
            inputTick = tick;
            inputSum = schema.AbilityCount == 0 ? Leaf(2, (ulong)inputTick, 0) : RowLeaf(2, 0);
        }
        private void Header(SimStateWriter w)
        {
            w.UInt64(1);
            w.UInt64(schema.RulesDigest);
            w.UInt64(schema.CapacityDigest);
            w.Int32(TableCount);
        }
        private int CountFor(int tag) => tag <= 2 ? 1 : tag == 3 ? ownerCount : tag == 4 ? resourceCount : tag == 5 ? cooldownCount : tag == 6 ? abilityCastCount : tag == 7 ? statusCount : tag == 9 && Nonempty ? 1 : 0;
        private ulong SumFor(int tag) => tag == 1 ? metaSum : tag == 2 ? inputSum : tag == 3 ? ownerSum : tag == 4 ? resourceSum : tag == 5 ? cooldownSum : tag == 6 ? abilityCastSum : tag == 7 ? statusSum : tag == 9 ? diagnosticSum : 0;
        private static void Stamp(SimStateWriter w, StableId id, ushort gen)
        {
            w.Bool(true);
            w.UInt64(id.Value);
            w.UInt64(gen);
        }
        private void Row(SimStateWriter w, int tag, int index)
        {
            if (tag == 1)
            {
                w.UInt64(effectNext);
                w.UInt64(0);
            }
            else if (tag == 2)
                WriteAbilityInput(w);
            else if (tag == 3)
            {
                Stamp(w, owners[index], generations[index]);
                w.UInt64(archetypes[index]);
                w.UInt64(schema.AbilityCount == 0 ? (ulong)generations[index] << 48 : castNext[index]);
                WriteLife(w, index);
            }
            else if (tag == 4)
            {
                Stamp(w, resourceOwners[index], resourceGenerations[index]);
                w.UInt64(resourceKeys[index]);
                w.Int64(values[index]);
                w.Bool(hasTask[index]);
                if (hasTask[index])
                    w.TaskKeyValue(tasks[index]);
            }
            else if (tag == 5)
                WriteAbilityCooldown(w, index);
            else if (tag == 6)
                WriteAbilityCast(w, index);
            else if (tag == 7)
                WriteStatusRow(w, index);
            else if (tag == 9)
            {
                w.Int64(numericSaturations);
                w.Int64(rejectedResources);
                w.Int64(rejectedCapacity);
                w.Int64(timeSaturations);
                if (schema.StatusCount != 0)
                {
                    w.Int64(immuneRejections);
                    w.Int64(statusCapacityRejections);
                }
                if (schema.AbilityCount != 0)
                    for (int i = 0; i < abilityFailures.Length; i++)
                        w.Int64(abilityFailures[i]);
                if (schema.LifeRuleCount != 0)
                {
                    w.Int64(noticesDropped);
                    w.Int32(s4NoticeCount);
                }
            }
        }
        private ulong RowLeaf(int tag, int index)
        {
            leafWriter.ResetStable();
            leafWriter.Int32(tag);
            Row(leafWriter, tag, index);
            return StableEntityKey.Mix(leafHash.Value);
        }
        private void RefreshDiagnostics()
        {
            if (Nonempty)
                diagnosticSum = RowLeaf(9, 0);
        }
        internal void WriteHash(SimStateWriter w)
        {
            if (retiredCount != 0)
                throw new SimBoundaryException("M0C_GAMEPLAY_BOUNDARY: Complete pending retirement first.");
            w.UInt64(248);
            Header(w);
            for (int tag = 1; tag <= TableCount; tag++)
            {
                w.Int32(tag);
                w.Int32(CountFor(tag));
                w.UInt64(SumFor(tag));
            }
        }
        private long RowLength(int tag, int i) => tag == 1 ? 16 : tag == 2 ? AbilityInputLength() : tag == 3 ? 58 + (hasDownedTask[i] ? 40 : 0) :
            tag == 4 ? 34 + (hasTask[i] ? 40 : 0) : tag == 5 ? 34 + (abilityCooldowns[i].HasTask ? 40 : 0) : tag == 6 ? AbilityCastLength(i) :
            tag == 7 ? StatusRowLength(i) : (schema.StatusCount != 0 ? 48 : 32) + (schema.AbilityCount != 0 ? 8L * abilityFailures.Length : 0) + (schema.LifeRuleCount != 0 ? 16 : 0);
        internal void Save(SimStateWriter w)
        {
            if (commandCount != 0 || s4NoticeCount != 0 || noticeCount != 0 || retiredCount != 0)
                throw new SimBoundaryException("M0C_GAMEPLAY_BOUNDARY");
            long length = 32 + 9 * 32;
            for (int tag = 1; tag <= TableCount; tag++)
                for (int i = 0; i < CountFor(tag); i++)
                    length += 8 + RowLength(tag, i);
            w.UInt64((ulong)length);
            Header(w);
            for (int tag = 1; tag <= TableCount; tag++)
            {
                int count = CountFor(tag);
                w.Int32(tag);
                w.Int32(count);
                for (int i = 0; i < count; i++)
                {
                    w.UInt64((ulong)RowLength(tag, i));
                    Row(w, tag, i);
                }
                w.Int32(count);
                w.UInt64(SumFor(tag));
            }
        }
        private GameplayStamp ReadStamp(SimStateReader row)
        {
            SimStateReader.Require(row.Bool(), "M0C_STAMP_REQUIRED");
            StableId id = new StableId(row.UInt64());
            ulong gen = row.UInt64();
            SimStateReader.Require(gen <= ushort.MaxValue && gen == Generation(id) && sim.Entities.TryResolve(id, out _), "M0C_STAMP_LIFE");
            return new GameplayStamp(id, (ushort)gen);
        }
        internal void Load(SimStateReader reader, long tick)
        {
            SimStateReader section = reader.Section();
            SimStateReader.Require(section.UInt64() == 1, "M0C_GAMEPLAY_SCHEMA");
            SimStateReader.Require(section.UInt64() == schema.RulesDigest, "M0C_GAMEPLAY_RULES");
            SimStateReader.Require(section.UInt64() == schema.CapacityDigest, "M0C_GAMEPLAY_CAPACITY");
            SimStateReader.Require(section.Int32() == TableCount, "M0C_GAMEPLAY_TABLES");
            ownerCount = resourceCount = commandCount = noticeCount = retiredCount = statusCount = 0;
            ownerSum = resourceSum = statusSum = 0;
            effectNext = 0;
            inputTick = tick;
            immuneRejections = statusCapacityRejections = 0;
            ResetAbilityProtocol();
            for (int tag = 1; tag <= TableCount; tag++)
            {
                SimStateReader.Require(section.Int32() == tag, "M0C_GAMEPLAY_TAG");
                int cap = tag <= 2 ? 1 : tag == 3 ? owners.Length : tag == 4 ? values.Length : tag == 5 ? abilityCooldowns.Length : tag == 6 ? abilityCasts.Length : tag == 7 ? statuses.Length : tag == 9 && Nonempty ? 1 : 0;
                int count = section.Count(cap);
                ulong sum = 0;
                if (tag <= 2 || tag == 9 && Nonempty)
                    SimStateReader.Require(count == 1, "M0C_GAMEPLAY_COUNT");
                for (int i = 0; i < count; i++)
                {
                    SimStateReader row = section.Section();
                    if (tag == 1)
                    {
                        ulong first = row.UInt64(), second = row.UInt64();
                        SimStateReader.Require(second == 0 && (tag == 1 ? (schema.StatusCount != 0 || schema.LifeRuleCount != 0 || first == 0) : first == (ulong)tick), "M0C_GAMEPLAY_ROW");
                        if (tag == 1)
                        {
                            effectNext = first;
                            RefreshStatusMeta();
                        }
                    }
                    else if (tag == 2)
                        ReadAbilityInput(row, tick);
                    else if (tag == 3)
                    {
                        GameplayStamp stamp = ReadStamp(row);
                        ulong archetype = row.UInt64();
                        SimStateReader.Require(schema.FindArchetype(archetype) >= 0 && (i == 0 || owners[i - 1].CompareTo(stamp.Id) < 0), "M0C_OWNER_ORDER");
                        ulong next = row.UInt64();
                        SimStateReader.Require(schema.AbilityCount == 0 ? next == ((ulong)stamp.Generation << 48) : ValidCastNext(next, stamp), "M0C_OWNER_STATE");
                        castNext[i] = next;
                        owners[i] = stamp.Id;
                        generations[i] = stamp.Generation;
                        archetypes[i] = archetype;
                        ownerCount++;
                        ReadLife(row, i, tick);
                        SimStateReader.Require(sim.Entities.TryGet(binding, sim.Entities.Resolve(new EntityLink(stamp.Id)), out GameplayBinding b)
                            && b.ArchetypeKey == archetype, "M0C_OWNER_BINDING");
                    }
                    else if (tag == 4)
                    {
                        GameplayStamp stamp = ReadStamp(row);
                        ulong key = row.UInt64();
                        long value = row.Int64();
                        bool regen = row.Bool();
                        int defIndex = schema.FindResource(key), owner = OwnerIndex(stamp.Id);
                        SimStateReader.Require(defIndex >= 0 && owner >= 0 && (i == 0 || resourceOwners[i - 1].CompareTo(stamp.Id) < 0
                            || resourceOwners[i - 1].Equals(stamp.Id) && resourceKeys[i - 1] < key), "M0C_RESOURCE_ORDER");
                        GameplayResourceDef def = schema.ResourceAt(defIndex);
                        SimStateReader.Require(value >= def.LowerRaw && value <= def.UpperRaw && (!IsDead(owner) || !regen) && (!regen || def.RegenPeriodTicks > 0)
                            && (tick == 0 || regen == (def.RegenPeriodTicks > 0 && !IsDead(owner))), "M0C_RESOURCE_RANGE");
                        resourceOwners[i] = stamp.Id;
                        resourceGenerations[i] = stamp.Generation;
                        resourceKeys[i] = key;
                        values[i] = value;
                        hasTask[i] = regen;
                        tasks[i] = regen ? row.ReadTaskKey() : default;
                        resourceCount++;
                        if (regen)
                            SimStateReader.Require(tasks[i].DueTick > tick && tasks[i].DueTick <= StatusProtocolFuture(tick, (ulong)def.RegenPeriodTicks)
                            && tasks[i].OriginDomain == 0 && tasks[i].Origin.Equals(stamp.Id)
                            && tasks[i].Kind == key && sim.Scheduler.TryGetPendingStable(tasks[i], out StableScheduledTask task)
                            && task.Event.Type.Equals(GameplayEvents.Regen) && task.Event.Target.HasValue && task.Event.Target.Id.Equals(stamp.Id)
                            && !task.Event.Source.HasValue && task.Event.Tag == 0 && task.Event.Payload.Magnitude.Raw == 0 && task.Event.Payload.Radius.Raw == 0
                            && task.Event.Payload.Position.Equals(default(SimVec3)) && task.Event.Payload.Aux.Equals(default(SimVec3)), "M0C_REGEN_RESTORE");
                    }
                    else if (tag == 5)
                        ReadAbilityCooldown(row, i, tick);
                    else if (tag == 6)
                        ReadAbilityCast(row, i, tick);
                    else if (tag == 7)
                        ReadStatusRow(row, i, tick);
                    else if (tag == 9)
                    {
                        numericSaturations = row.Int64();
                        rejectedResources = row.Int64();
                        rejectedCapacity = row.Int64();
                        timeSaturations = row.Int64();
                        if (schema.StatusCount != 0)
                        {
                            immuneRejections = row.Int64();
                            statusCapacityRejections = row.Int64();
                        }
                        if (schema.AbilityCount != 0)
                            for (int f = 0; f < abilityFailures.Length; f++)
                            {
                                abilityFailures[f] = row.Int64();
                                SimStateReader.Require(abilityFailures[f] >= 0, "M0C_ABILITY_DIAGNOSTICS");
                            }
                        if (schema.LifeRuleCount != 0)
                        {
                            noticesDropped = row.Int64();
                            SimStateReader.Require(noticesDropped >= 0 && row.Int32() == 0, "M0C_NOTICE_BOUNDARY");
                        }
                        SimStateReader.Require(numericSaturations >= 0 && rejectedResources >= 0 && rejectedCapacity >= 0 && timeSaturations >= 0
                            && immuneRejections >= 0 && statusCapacityRejections >= 0, "M0C_DIAGNOSTICS");
                    }
                    row.End();
                    sum = unchecked(sum + RowLeaf(tag, i));
                }
                SimStateReader.Require(section.Int32() == count && section.UInt64() == sum, "M0C_GAMEPLAY_SUM");
                if (tag == 2)
                    inputSum = sum;
                if (tag == 3)
                    ownerSum = sum;
                if (tag == 4)
                    resourceSum = sum;
                if (tag == 5)
                    cooldownSum = sum;
                if (tag == 6)
                    abilityCastSum = sum;
                if (tag == 7)
                    statusSum = sum;
                if (tag == 9)
                    diagnosticSum = sum;
            }
            section.End();
            int expected = 0;
            for (int o = 0; o < ownerCount; o++)
            {
                GameplayArchetypeDef a = schema.ArchetypeAt(schema.FindArchetype(archetypes[o]));
                SimStateReader.Require(a.ResourceCount <= resourceCount - expected, "M0C_RESOURCE_BINDING_COUNT");
                expected += a.ResourceCount;
                for (int r = 0; r < a.ResourceCount; r++)
                    SimStateReader.Require(ResourceIndex(owners[o], a.ResourceKeyAt(r)) >= 0, "M0C_RESOURCE_BINDING");
            }
            SimStateReader.Require(expected == resourceCount, "M0C_RESOURCE_BINDING_COUNT");
            if (Nonempty)
                foreach (EntityId entity in sim.Entities.CaptureStableEntities())
                    if (sim.Entities.TryGet(binding, entity, out GameplayBinding bound))
                    {
                        int owner = OwnerIndex(sim.Entities.StableOf(entity));
                        SimStateReader.Require(owner >= 0 && archetypes[owner] == bound.ArchetypeKey, "M0C_ORPHAN_BINDING");
                    }
            sim.Scheduler.ValidateGameplayRegeneration(tasks, hasTask, resourceCount);
            ValidateRestoredStatuses(tick);
            ValidateRestoredAbilities(tick);
            ValidateRestoredLife(tick);
            if (Nonempty)
                SimStateReader.Require(sim.Rng.HasStream(criticalStream), "M0C_GAMEPLAY_RNG");
        }
    }
    public sealed partial class Sim
    {
        public const int GameplayHashVersion = 6;
        public const int GameplaySnapshotVersion = 8;
        private GameplayState gameplay;
    }
}
