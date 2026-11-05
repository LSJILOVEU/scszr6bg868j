using System;
using System.Runtime.InteropServices;

namespace WarCore.Sim
{
    internal sealed partial class Components<T>
    {
        internal bool ProtectedWrites;
        private T[] fenceValues;
        private bool[] fencePresent;
        private bool[] borrowed;
        private int borrowedCount;

        internal void MarkBorrowed(int slot)
        {
            if (borrowed[slot])
                return;
            borrowed[slot] = true;
            borrowedCount++;
        }

        internal void ProtectWrites()
        {
            ProtectedWrites = true;
            ResizeWriteFence(Values.Length);
        }

        private void ResizeWriteFence(int size)
        {
            if (!ProtectedWrites)
                return;
            Array.Resize(ref fenceValues, size);
            Array.Resize(ref fencePresent, size);
        }

        public void CaptureWriteFence()
        {
            if (!ProtectedWrites)
                return;
            Array.Copy(Values, fenceValues, Values.Length);
            Array.Copy(Present, fencePresent, Present.Length);
        }

        public void ValidateWriteFence(bool[] alive, int allocated)
        {
            if (!ProtectedWrites || borrowedCount == 0)
                return;
            for (int slot = 0; slot < allocated; slot++)
            {
                // Death clears components immediately; it is legal after publication.
                if (!alive[slot] || !borrowed[slot])
                    continue;
                if (Present[slot] != fencePresent[slot] || (Present[slot]
                    && !MemoryMarshal.AsBytes(Values.AsSpan(slot, 1)).SequenceEqual(MemoryMarshal.AsBytes(fenceValues.AsSpan(slot, 1)))))
                    throw new SimBoundaryException("Position, Collider or Posture changed through a retained reference after spatial synchronization.");
            }
        }
    }

    public sealed partial class EntityStore
    {
        private bool[] pending;
        private int pendingCount;
        private bool spatialWritesLocked;
        internal int PendingDestroyCount => pendingCount;
        internal int Allocated => allocated;

        internal static int GrownCapacity(int capacity)
        {
            if (capacity < 1 || capacity > int.MaxValue / 2)
                throw new SimBoundaryException("Entity capacity exceeds the supported range.");
            return capacity * 2;
        }

        // The store owns only lifecycle state. Sim cleans derived indexes before calling this.
        internal void RecycleDestroyed()
        {
            if (pendingCount == 0)
                return;
            for (int slot = 0; slot < allocated; slot++)
            {
                if (!pending[slot])
                    continue;
                int index = freeCount++;
                while (index > 0 && free[(index - 1) / 2] > slot)
                {
                    free[index] = free[(index - 1) / 2];
                    index = (index - 1) / 2;
                }
                free[index] = slot;
                pending[slot] = false;
            }
            pendingCount = 0;
        }

        internal void ProtectWrites<T>(ComponentType<T> type) where T : unmanaged, IStateHash
            => Require(type).ProtectWrites();

        internal void SealSpatialWrites()
        {
            foreach (IComponents storage in storages)
                storage.CaptureWriteFence();
            spatialWritesLocked = true;
        }

        internal void OpenSpatialWrites() => spatialWritesLocked = false;

        internal void ValidateSpatialWrites()
        {
            if (!spatialWritesLocked)
                return;
            foreach (IComponents storage in storages)
                storage.ValidateWriteFence(alive, allocated);
        }

        private void RequireWritable<T>(Components<T> storage) where T : unmanaged, IStateHash
        {
            if (IdentityMode == IdentityMode.StableWorld && typeof(T) == typeof(SpawnSequence))
                throw new SimBoundaryException("SpawnSequence is owned by the stable spawn service.");
            if (spatialWritesLocked && storage.ProtectedWrites)
                throw new SimBoundaryException("Position, Collider and Posture writes require a movement phase before spatial synchronization; use Read for a value copy.");
        }

        public T Read<T>(ComponentType<T> type, EntityId entity) where T : unmanaged, IStateHash
        {
            RequireAlive(entity);
            Components<T> storage = Require(type);
            if (!storage.Present[entity.Id])
                throw new InvalidOperationException("Entity has no such component.");
            return storage.Values[entity.Id];
        }
    }
}
