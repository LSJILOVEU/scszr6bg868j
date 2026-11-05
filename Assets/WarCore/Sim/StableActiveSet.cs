using System;

namespace WarCore.Sim
{
    public sealed partial class EntityStore
    {
        private struct ActiveMember : IComparable<ActiveMember>
        {
            internal StableId Key;
            internal EntityId Entity;
            public int CompareTo(ActiveMember other) => Key.CompareTo(other.Key);
        }
        private EntityId[] orderedActive;
        private EntityId[] mergedActive;
        private ActiveMember[] activeAdditions;
        private int[] additionIndex;
        private int additionCount;
        private int activeCount;
        private bool activePublished;
        private bool activeDirty;
        public ulong ActiveSetPublication { get; private set; }
        /// <summary>Count at the last stable-set publication, not the current live count.
        /// Pending births are absent; deaths can remain counted until synchronization.</summary>
        public int ActiveEntityCount => activeCount;
        private void InitializeActiveSet(int capacity)
        {
            orderedActive = new EntityId[capacity];
            mergedActive = new EntityId[capacity];
            activeAdditions = new ActiveMember[capacity];
            additionIndex = new int[capacity];
        }
        private void QueueActive(EntityId entity)
        {
            int at = additionIndex[entity.Id] - 1;
            if (at < 0)
            {
                at = additionCount++;
                additionIndex[entity.Id] = at + 1;
            }
            activeAdditions[at] = new ActiveMember { Key=stableIds[entity.Id], Entity=entity };
            activeDirty = true;
        }
        // Views expire at this explicit boundary. Capturing a view never sorts or publishes later edits.
        public void SynchronizeStableEntities()
        {
            if (IdentityMode != IdentityMode.StableWorld || (activePublished && !activeDirty))
                return;
            int added = 0;
            for (int i = 0; i < additionCount; i++)
            {
                ActiveMember item = activeAdditions[i];
                additionIndex[item.Entity.Id] = 0;
                if (IsAlive(item.Entity))
                    activeAdditions[added++] = item;
            }
            ValueOrder.Sort(activeAdditions.AsSpan(0,added));
            int old = 0;
            int next = 0;
            int length = 0;
            while (old < activeCount || next < added)
            {
                if (old < activeCount && !IsAlive(orderedActive[old]))
                {
                    old++;
                    continue;
                }
                if (old == activeCount || (next < added && activeAdditions[next].Key.CompareTo(StableAt(orderedActive[old].Id)) < 0))
                    mergedActive[length++] = activeAdditions[next++].Entity;
                else
                    mergedActive[length++] = orderedActive[old++];
            }
            EntityId[] swap = orderedActive;
            orderedActive = mergedActive;
            mergedActive = swap;
            activeCount = length;
            additionCount = 0;
            activeDirty = false;
            activePublished = true;
            ActiveSetPublication = unchecked(ActiveSetPublication + 1);
        }
        internal EntityStage.View CaptureStableEntities()
        {
            // Standalone stores have one assembly publication; Sim publishes explicitly at phase boundaries.
            if (!activePublished)
                SynchronizeStableEntities();
            return new EntityStage.View(this,orderedActive,activeCount);
        }
        private void ResetActiveSet()
        {
            Array.Clear(additionIndex,0,additionIndex.Length);
            activeCount = 0;
            additionCount = 0;
            activeDirty = true;
            activePublished = false;
            for (int slot = 0; slot < allocated; slot++)
                if (alive[slot])
                    QueueActive(new EntityId(slot,generations[slot]));
            SynchronizeStableEntities();
        }
    }
}
