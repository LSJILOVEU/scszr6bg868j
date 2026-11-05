namespace WarCore.Sim
{
    public sealed partial class EntityStore
    {
        // Backshift deletion preserves bounded probes without retaining a second history of tombstones.
        private void RemoveIdentityBucket(StableId id)
        {
            int mask = identityBuckets.Length - 1;
            int hole = IdentityBucket(id);
            while (!identities[identityBuckets[hole] - 1].Id.Equals(id))
                hole = (hole + 1) & mask;
            int next = (hole + 1) & mask;
            while (identityBuckets[next] != 0)
            {
                int home = IdentityBucket(identities[identityBuckets[next] - 1].Id);
                if (((next - home + identityBuckets.Length) & mask) >= ((next - hole + identityBuckets.Length) & mask))
                {
                    identityBuckets[hole] = identityBuckets[next];
                    hole = next;
                }
                next = (next + 1) & mask;
            }
            identityBuckets[hole] = 0;
        }

        internal void RecycleRetiredIdentities()
        {
            if (IdentityMode != IdentityMode.StableWorld || RetiredRetentionTicks == long.MaxValue)
                return;
            for (int i = 0; i < identityCount;)
            {
                IdentityRecord record = identities[i];
                if (record.Life != IdentityLife.Retired || IdentityTick < record.RetiredAt
                    || IdentityTick - record.RetiredAt < RetiredRetentionTicks)
                {
                    i++;
                    continue;
                }
                RemoveIdentityBucket(record.Id);
                int last = --identityCount;
                if (i != last)
                {
                    IdentityRecord moved = identities[last];
                    RemoveIdentityBucket(moved.Id);
                    identities[i] = moved;
                    if (moved.Life != IdentityLife.Retired)
                        identitySlots[moved.Address.Slot] = i;
                    AddIdentityBucket(i);
                }
                identities[last] = default;
                IdentityChanged();
            }
        }

        internal bool HasLogicalIdentity(StableId id)
        {
            int index = FindIdentity(id);
            return index >= 0 && identities[index].Life != IdentityLife.Retired;
        }
        internal bool HasTaskOrigin(StableId id) => FindIdentity(id) >= 0;
        internal ulong TaskCounter(StableId id, bool requireActive)
        {
            int index = FindIdentity(id);
            if (index < 0 || (requireActive && identities[index].Life != IdentityLife.Active))
                throw new SimBoundaryException("Scheduled origin must be an active registered entity.");
            return identities[index].NextTask;
        }
        internal void SetTaskCounter(StableId id, ulong next)
        {
            int index = FindIdentity(id);
            if (index < 0)
                throw new SimBoundaryException("Scheduled cursor has no identity record.");
            identities[index].NextTask = next;
        }
        internal int TaskOriginCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < identityCount; i++)
                    if (identities[i].NextTask != 0)
                        count++;
                return count;
            }
        }
        internal StableId IdentityAt(int index) => identities[index].Id;
        internal ulong TaskCounterAt(int index) => identities[index].NextTask;
    }
}
