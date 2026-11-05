using System;

namespace WarCore.Sim
{
    // Elapsed integration is distinct from infrequent decisions such as an AI thought.
    public interface ISimElapsedEntitySystem : ISimEntitySystem
    {
        void StepEntity(SimContext context, EntityId entity, long elapsedTicks);
    }

    internal interface ISimWarmupSystem
    {
        void PrepareWarmup();
    }

    internal sealed partial class TieredTimeline : IStateHash
    {
        private struct Entry
        {
            internal ulong Generation;
            internal StableId Identity;
            internal bool HasIdentity;
            internal long LastSimulatedTick;
            internal SimTierLevel PreviousLevel;
        }

        private readonly EntityStore entities;
        private Entry[] entries;
        internal TieredTimeline(EntityStore entities)
        {
            this.entities = entities;
            PrepareWarmup();
        }

        internal void PrepareWarmup()
        {
            if (entries == null || entries.Length != entities.Capacity)
                Array.Resize(ref entries, entities.Capacity);
        }

        internal void Advance(SimContext context, EntityId entity, SimTierLevel level, int period, ISimElapsedEntitySystem system)
        {
            long tick = context.Clock.Tick;
            if (tick <= 0 || entity.Id < 0 || entity.Id >= entries.Length)
                throw new InvalidOperationException("Elapsed integration requires an active tick and reserved entity slot.");
            ref Entry entry = ref entries[entity.Id];
            if (entities.IdentityMode == IdentityMode.StableWorld
                ? !entry.HasIdentity || !entry.Identity.Equals(entities.StableAt(entity.Id)) : entry.Generation != entity.Generation)
            {
                // A new or reused slot starts now; it must not inherit a predecessor's elapsed interval.
                entry = new Entry { Generation = entity.Generation, LastSimulatedTick = tick - 1, PreviousLevel = level,
                    Identity = entities.StableAt(entity.Id), HasIdentity = entities.IdentityMode == IdentityMode.StableWorld };
            }
            bool waking = entry.PreviousLevel == SimTierLevel.Dormant && level != SimTierLevel.Dormant;
            entry.PreviousLevel = level;
            bool due = level == SimTierLevel.Full || waking || (level == SimTierLevel.Reduced
                && ((ulong)tick % (ulong)period == entities.StableOf(entity).Value % (ulong)period || tick % period == 0));
            if (!due)
                return;
            if (entry.LastSimulatedTick < 0 || entry.LastSimulatedTick >= tick)
                throw new InvalidOperationException("Elapsed integration timeline is not behind the current tick.");
            // Both timestamps are nonnegative and ordered, so subtraction cannot overflow.
            system.StepEntity(context, entity, tick - entry.LastSimulatedTick);
            entry.LastSimulatedTick = tick;
        }

        private bool Deferred(EntityId entity) => entity.Id < entries.Length
            && entries[entity.Id].Generation == entity.Generation && entries[entity.Id].PreviousLevel != SimTierLevel.Full;

        private int DeferredCount()
        {
            int count = 0;
            foreach (EntityId entity in entities)
                if (Deferred(entity))
                    count++;
            return count;
        }

        public void WriteState(StateHash hash)
        {
            int count = DeferredCount();
            // Full entities have a derivable timestamp (the completed tick); preserve their legacy encoding.
            if (count == 0)
                return;
            hash.Add("warcore.elapsed-tier-v2");
            hash.Add(count);
            foreach (EntityId entity in entities)
            {
                if (!Deferred(entity))
                    continue;
                Entry entry = entries[entity.Id];
                entity.WriteState(hash);
                hash.Add(entry.LastSimulatedTick);
                hash.Add((int)entry.PreviousLevel);
            }
        }

        internal void Save(SimStateWriter writer)
        {
            if (writer.IsStable)
            {
                SaveStable(writer);
                return;
            }
            writer.Int32(DeferredCount());
            foreach (EntityId entity in entities)
            {
                if (!Deferred(entity))
                    continue;
                Entry entry = entries[entity.Id];
                SnapshotValues.Entity(writer, entity);
                writer.Int64(entry.LastSimulatedTick);
                writer.Int32((int)entry.PreviousLevel);
            }
        }

        internal void Load(SimStateReader reader)
        {
            if (reader.IsStable)
            {
                LoadStable(reader);
                return;
            }
            int count = reader.Count(entities.Capacity);
            entries = new Entry[entities.Capacity];
            int previous = -1;
            for (int i = 0; i < count; i++)
            {
                EntityId entity = SnapshotValues.Entity(reader);
                long tick = reader.Int64();
                var level = (SimTierLevel)reader.Int32();
                SimStateReader.Require(entity.Id > previous && entities.IsAlive(entity) && tick >= 0
                    && (level == SimTierLevel.Reduced || level == SimTierLevel.Dormant), "Invalid elapsed tier timeline.");
                entries[entity.Id] = new Entry { Generation = entity.Generation, LastSimulatedTick = tick, PreviousLevel = level };
                previous = entity.Id;
            }
        }

        internal void Validate(long tick)
        {
            foreach (EntityId entity in entities)
                if (Deferred(entity))
                    SimStateReader.Require(entries[entity.Id].LastSimulatedTick <= tick, "Elapsed tier timeline lies in the future.");
        }
    }
}
