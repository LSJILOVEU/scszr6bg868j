using System;
using System.Collections.Generic;

namespace WarCore.Sim
{
    public enum ReferenceCleanup { None, Updated, Removed }
    public interface IStableReferenceCleanup<T> where T : unmanaged, IStateHash
    {
        ReferenceCleanup ClearRetiredReference(ref T value, EntityLink retired, out StableRequestKind kind);
    }
    public interface IEntityRetirementObserver
    {
        void ReferenceCleared(StableRequestKind kind, EntityLink owner, EntityLink retired);
    }
    internal sealed partial class Components<T>
    {
        // Derived state only: never serialized or hashed. Indices are dense positions + 1.
        private int[] referenceSlots, referenceIndices, orderedReferences, referenceScratch;
        private int[] referenceBuckets;
        private int referenceCount;
        private bool referenceOrderDirty;
        private ulong referenceRevision;

        internal void InitializeReferenceSlots()
        {
            if (!(Serializer is IStableReferenceCleanup<T>))
                return;
            referenceBuckets = new int[256];
            referenceSlots = new int[Values.Length];
            referenceIndices = new int[Values.Length];
            orderedReferences = new int[Values.Length];
            referenceScratch = new int[Values.Length];
            referenceCount = 0;
            for (int slot = 0; slot < Values.Length; slot++)
                if (Present[slot])
                    MarkReferenceValue(slot);
        }
        private void ResizeReferenceSlots(int size)
        {
            if (referenceSlots == null)
                return;
            Array.Resize(ref referenceSlots,size);
            Array.Resize(ref referenceIndices,size);
            Array.Resize(ref orderedReferences,size);
            Array.Resize(ref referenceScratch,size);
        }
        private void ResetReferenceSlots()
        {
            if (referenceSlots == null)
                return;
            Array.Clear(referenceIndices,0,referenceIndices.Length);
            referenceCount = 0;
            referenceOrderDirty = true;
            referenceRevision = unchecked(referenceRevision+1);
        }
        internal void MarkReferenceValue(int slot)
        {
            if (referenceSlots == null || referenceIndices[slot] != 0)
                return;
            referenceSlots[referenceCount] = slot;
            referenceIndices[slot] = ++referenceCount;
            referenceOrderDirty = true;
            referenceRevision = unchecked(referenceRevision+1);
        }
        private void UnmarkReferenceValue(int slot)
        {
            if (referenceSlots == null || referenceIndices[slot] == 0)
                return;
            int index = referenceIndices[slot]-1;
            int last = referenceSlots[--referenceCount];
            referenceSlots[index] = last;
            referenceIndices[last] = index+1;
            referenceIndices[slot] = 0;
            referenceOrderDirty = true;
            referenceRevision = unchecked(referenceRevision+1);
        }
        private void OrderReferenceSlots()
        {
            if (!referenceOrderDirty)
                return;
            Array.Copy(referenceSlots,orderedReferences,referenceCount);
            // Four fixed byte passes: O(present values), including non-comparison sorting.
            for (int shift = 0; shift < 32; shift += 8)
            {
                Array.Clear(referenceBuckets,0,referenceBuckets.Length);
                for (int i = 0; i < referenceCount; i++)
                    referenceBuckets[(orderedReferences[i] >> shift) & 255]++;
                int offset = 0;
                for (int i = 0; i < referenceBuckets.Length; i++)
                {
                    int count = referenceBuckets[i];
                    referenceBuckets[i] = offset;
                    offset += count;
                }
                for (int i = 0; i < referenceCount; i++)
                {
                    int slot = orderedReferences[i];
                    referenceScratch[referenceBuckets[(slot >> shift) & 255]++] = slot;
                }
                int[] swap = orderedReferences;
                orderedReferences = referenceScratch;
                referenceScratch = swap;
            }
            referenceOrderDirty = false;
        }
        public void ClearRetiredReferences(EntityStore store, EntityLink retired, int retiredSlot)
        {
            if (referenceCount == 0 || !(Serializer is IStableReferenceCleanup<T> cleaner))
                return;
            OrderReferenceSlots();
            int count = referenceCount;
            for (int i = 0; i < count; i++)
            {
                int slot = orderedReferences[i];
                if (!Present[slot] || slot == retiredSlot)
                    continue;
                ReferenceCleanup result = cleaner.ClearRetiredReference(ref Values[slot],retired,out StableRequestKind kind);
                if (result == ReferenceCleanup.None)
                    continue;
                if (result == ReferenceCleanup.Removed)
                    Clear(slot);
                ulong revision = referenceRevision;
                store.NotifyReferenceCleared(kind,new EntityLink(store.StableAt(slot)),retired);
                // Observers may change later slots. Resume strictly after this slot, as the old scan did.
                if (revision != referenceRevision)
                {
                    OrderReferenceSlots();
                    count = referenceCount;
                    int low = 0, high = count;
                    while (low < high)
                    {
                        int middle = low+(high-low)/2;
                        if (orderedReferences[middle] <= slot)
                            low = middle+1;
                        else
                            high = middle;
                    }
                    i = low-1;
                }
            }
        }
    }
    public sealed partial class EntityStore
    {
        private readonly List<IEntityRetirementObserver> retirementObservers = new List<IEntityRetirementObserver>();
        public void RegisterRetirementObserver(IEntityRetirementObserver observer)
        {
            if (frozen || observer == null || retirementObservers.Contains(observer))
                throw new SimBoundaryException("Register each retirement observer once before warmup.");
            retirementObservers.Add(observer);
        }
        internal void NotifyReferenceCleared(StableRequestKind kind, EntityLink owner, EntityLink retired)
        {
            for (int i = 0; i < retirementObservers.Count; i++)
                retirementObservers[i].ReferenceCleared(kind,owner,retired);
        }
        private void ClearRetiredReferences(EntityId entity)
        {
            if (IdentityMode != IdentityMode.StableWorld)
                return;
            var retired = new EntityLink(stableIds[entity.Id]);
            for (int i = 0; i < storages.Count; i++)
                storages[i].ClearRetiredReferences(this,retired,entity.Id);
        }
    }
}
