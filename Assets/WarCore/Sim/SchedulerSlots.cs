using System;

namespace WarCore.Sim
{
    // Handles belong to one scheduler instance. Stable task ids, not these runtime slots, are serialized.
    public readonly struct ScheduledTaskHandle : IEquatable<ScheduledTaskHandle>
    {
        public int Slot { get; }
        public ulong Generation { get; }

        internal ScheduledTaskHandle(int slot, ulong generation)
        {
            Slot = slot;
            Generation = generation;
        }

        public bool Equals(ScheduledTaskHandle other) => Slot == other.Slot && Generation == other.Generation;
        public override bool Equals(object obj) => obj is ScheduledTaskHandle other && Equals(other);
        public override int GetHashCode() => Slot ^ Generation.GetHashCode();
    }

    internal readonly struct SchedulerKey
    {
        internal readonly long Tick;
        internal readonly long Id;
        internal readonly int Slot;

        internal SchedulerKey(long tick, long id, int slot)
        {
            Tick = tick;
            Id = id;
            Slot = slot;
        }
    }

    public sealed partial class SimScheduler
    {
        private ScheduledTask[] tasks;
        private ulong[] taskGenerations;
        private byte[] taskStates;
        private int[] heapPositions;
        private int[] freeSlots;
        private int freeSlotCount;
        private int[] idSlots;

        private static int IdTableCapacity(int capacity)
        {
            // At most half full, so every successful lookup and insertion has an empty terminator.
            if (capacity < 1 || capacity > 1 << 28)
                throw new SimBoundaryException("Scheduler slot capacity exceeds the supported range.");
            int size = 2;
            while (size / 2 < capacity)
                size *= 2;
            return size;
        }

        private void InitializeSlots(int capacity)
        {
            idSlots = new int[IdTableCapacity(capacity)];
            tasks = new ScheduledTask[capacity];
            taskStates = new byte[capacity];
            heapPositions = new int[capacity];
            freeSlots = new int[capacity];
            // Retain generation history even if a restored snapshot shrinks capacity. Later growth
            // must not resurrect a handle issued for a removed slot before the restore.
            if (taskGenerations == null || taskGenerations.Length < capacity)
                Array.Resize(ref taskGenerations, capacity);
            RebuildFreeSlots();
        }

        private void RebuildFreeSlots()
        {
            freeSlotCount = 0;
            for (int slot = tasks.Length - 1; slot >= 0; slot--)
                if (taskStates[slot] == 0)
                    freeSlots[freeSlotCount++] = slot;
        }

        private void GrowSlots(int previous, int capacity)
        {
            int tableSize = IdTableCapacity(capacity);
            Array.Resize(ref tasks, capacity);
            Array.Resize(ref taskStates, capacity);
            Array.Resize(ref heapPositions, capacity);
            Array.Resize(ref freeSlots, capacity);
            if (taskGenerations.Length < capacity)
                Array.Resize(ref taskGenerations, capacity);
            for (int slot = capacity - 1; slot >= previous; slot--)
                freeSlots[freeSlotCount++] = slot;
            idSlots = new int[tableSize];
            for (int slot = 0; slot < previous; slot++)
                if (taskStates[slot] != 0)
                    AddTaskId(slot);
        }

        private void NextTaskGeneration(int slot)
        {
            if (taskGenerations[slot] == ulong.MaxValue)
                throw new SimBoundaryException("Scheduler slot generation exhausted.");
            taskGenerations[slot]++;
        }

        private int AllocateTaskSlot()
        {
            int slot = freeSlots[freeSlotCount - 1];
            NextTaskGeneration(slot);
            freeSlotCount--;
            return slot;
        }

        private void ReleaseTaskSlot(int slot)
        {
            if (stableTasks != null)
                stableTaskLookup.Remove(stableTasks[slot].Key);
            RemoveTaskId(tasks[slot].Id);
            tasks[slot] = default;
            taskStates[slot] = 0;
            freeSlots[freeSlotCount++] = slot;
        }

        private int IdBucket(long id)
        {
            // This is an internal address hash, not a state hash. Unsigned mixing is intentionally modular.
            ulong bits = unchecked((ulong)id * 11400714819323198485UL);
            return (int)((bits ^ (bits >> 32)) & (ulong)(idSlots.Length - 1));
        }

        private int FindTaskSlot(long id)
        {
            if (id <= 0 || id >= nextId)
                return -1;
            int bucket = IdBucket(id);
            while (idSlots[bucket] != 0)
            {
                int slot = idSlots[bucket] - 1;
                if (tasks[slot].Id == id)
                    return slot;
                bucket = (bucket + 1) & (idSlots.Length - 1);
            }
            return -1;
        }

        private void AddTaskId(int slot)
        {
            int bucket = IdBucket(tasks[slot].Id);
            while (idSlots[bucket] != 0)
                bucket = (bucket + 1) & (idSlots.Length - 1);
            idSlots[bucket] = slot + 1;
        }

        private void RemoveTaskId(long id)
        {
            int mask = idSlots.Length - 1;
            int hole = IdBucket(id);
            while (tasks[idSlots[hole] - 1].Id != id)
                hole = (hole + 1) & mask;
            int next = (hole + 1) & mask;
            // Backshift deletion avoids accumulating tombstones after unbounded warmed reuse.
            while (idSlots[next] != 0)
            {
                int home = IdBucket(tasks[idSlots[next] - 1].Id);
                if (((next - home + idSlots.Length) & mask) >= ((next - hole + idSlots.Length) & mask))
                {
                    idSlots[hole] = idSlots[next];
                    hole = next;
                }
                next = (next + 1) & mask;
            }
            idSlots[hole] = 0;
        }
    }
}
