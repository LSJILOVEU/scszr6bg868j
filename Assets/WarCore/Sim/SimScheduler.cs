using System;
using System.Collections.Generic;
using System.Text;

namespace WarCore.Sim
{
    public readonly struct ScheduledTask : IStateHash
    {
        public long Id
        {
            get;
        }

        public SimEvent Event
        {
            get;
        }

        internal ScheduledTask(long id, SimEvent item)
        {
            Id = id;
            Event = item;
        }

        public void WriteState(StateHash hash)
        {
            hash.Add(Id);
            Event.WriteState(hash);
        }
    }

    // Due tasks remain authoritative until consumed or canceled; presentation capacity cannot discard them.
    public sealed partial class SimScheduler : IStateHash
    {
        private SchedulerKey[] heap;
        private int[] hashHeap;
        private int count;
        private int dueHead;
        private int dueCount;
        private long nextId = 1;
        private long advancedThrough;
        private bool frozen;
        public int PendingCount => count;
        public int DueCount => dueCount;
        public int Capacity => heap.Length;
        public SimScheduler(int capacity = 1024, IdentityMode identityMode = IdentityMode.Legacy, int systemCapacity = 0)
        {
            if (capacity < 1)
                throw new ArgumentOutOfRangeException(nameof(capacity));
            this.identityMode = identityMode;
            if (identityMode == IdentityMode.StableWorld)
            {
                stableTasks = new StableScheduledTask[capacity];
                int origins = systemCapacity == 0 ? capacity : systemCapacity;
                cursorLookup = new BoundedLookup<OriginKey>(origins);
                taskCursors = new TaskCursor[origins];
                stableTaskLookup = new BoundedLookup<TaskKey>(capacity);
                stableOrder = new int[capacity];
                stableTaskWriter = new SimStateWriter(null,hash:stableTaskHash);
                stableSchedulerHash = new StateHash();
                stableSchedulerWriter = new SimStateWriter(null,hash:stableSchedulerHash);
            }
            IdTableCapacity(capacity);
            heap = new SchedulerKey[capacity];
            hashHeap = new int[capacity];
            InitializeSlots(capacity);
            InitializeDue(capacity);
        }

        public void Reserve(int capacity)
        {
            if (capacity <= heap.Length)
                return;
            if (frozen)
                throw new InvalidOperationException("Scheduler capacity exhausted after warmup.");
            IdTableCapacity(capacity);
            if (identityMode == IdentityMode.StableWorld)
            {
                Array.Resize(ref stableOrder, capacity);
                Array.Resize(ref stableTasks, capacity);
                stableTaskLookup = new BoundedLookup<TaskKey>(capacity);
                for (int i = 0; i < tasks.Length; i++)
                    if (taskStates[i] != 0)
                        stableTaskLookup.Add(stableTasks[i].Key,i);
            }
            int previous = heap.Length;
            Array.Resize(ref heap, capacity);
            Array.Resize(ref hashHeap, capacity);
            GrowSlots(previous, capacity);
            GrowDue(capacity);
        }

        public void CompleteWarmup()
        {
            frozen = true;
        }

        // Stable ids remain the persistence protocol; new callers can retain a direct slot handle.
        public long Schedule(long tick, SimEvent template)
        {
            ScheduledTaskHandle handle = ScheduleWithHandle(tick, template);
            return tasks[handle.Slot].Id;
        }

        public ScheduledTaskHandle ScheduleWithHandle(long tick, SimEvent template)
        {
            RequireLegacyTasks();
            return ScheduleCore(tick, template, default);
        }

        private ScheduledTaskHandle ScheduleCore(long tick, SimEvent template, StableScheduledTask stable)
        {
            if (tick <= advancedThrough)
                throw new ArgumentOutOfRangeException(nameof(tick), "Schedule a future tick.");
            // Validate the payload before consuming a slot, generation or stable task id.
            SimEvent item = template.At(tick);
            if (nextId == long.MaxValue)
                throw new SimBoundaryException("Scheduler task id exhausted.");
            if (count == heap.Length - dueCount)
            {
                if (frozen)
                    throw CapacityError(template.Type);
                Reserve(GrownCapacity(heap.Length));
            }
            int slot = AllocateTaskSlot();
            long id = nextId;
            nextId++;
            tasks[slot] = new ScheduledTask(id, item);
            if (stableTasks != null)
            {
                stableTasks[slot] = stable;
                stableTaskLookup.Add(stable.Key,slot);
            }
            taskStates[slot] = 1;
            AddTaskId(slot);
            heap[count] = new SchedulerKey(tick, id, slot);
            heapPositions[slot] = count;
            pendingHashV2 = unchecked(pendingHashV2 + TaskHashV2(tasks[slot]));
            SiftUp(count++);
            return new ScheduledTaskHandle(slot, taskGenerations[slot]);
        }

        internal static int GrownCapacity(int capacity)
        {
            if (capacity < 1 || capacity > int.MaxValue / 2)
                throw new SimBoundaryException("Scheduler capacity exceeds the supported range.");
            return capacity * 2;
        }

        private InvalidOperationException CapacityError(EventTypeId requestedType)
        {
            // Build diagnostics only on failure so successful warmed operations allocate nothing.
            var types = new SortedDictionary<int, int>();
            for (int slot = dueHead; slot >= 0; slot = dueNext[slot])
            {
                int type = tasks[slot].Event.Type.Value;
                types.TryGetValue(type, out int total);
                types[type] = total + 1;
            }

            var message = new StringBuilder("Scheduler capacity exhausted after warmup: requested task type=");
            message.Append(requestedType.Value);
            message.Append("; capacity=").Append(heap.Length);
            message.Append("; pending=").Append(count);
            message.Append("; due=").Append(dueCount);
            message.Append("; due task types=[");
            bool first = true;
            foreach (KeyValuePair<int, int> type in types)
            {
                if (!first)
                    message.Append(", ");
                message.Append(type.Key).Append(':').Append(type.Value);
                first = false;
            }

            message.Append("]. Consume or cancel tasks, or reserve more capacity before warmup completes.");
            return new InvalidOperationException(message.ToString());
        }

        private bool Before(SchedulerKey a, SchedulerKey b) => stableTasks == null
            ? a.Tick < b.Tick || (a.Tick == b.Tick && a.Id < b.Id)
            : stableTasks[a.Slot].Key.CompareTo(stableTasks[b.Slot].Key) < 0;
        private static bool Before(ScheduledTask a, ScheduledTask b) => a.Event.Tick < b.Event.Tick || (a.Event.Tick == b.Event.Tick && a.Id < b.Id);
        private void Swap(int a, int b)
        {
            SchedulerKey swap = heap[a];
            heap[a] = heap[b];
            heap[b] = swap;
            heapPositions[heap[a].Slot] = a;
            heapPositions[heap[b].Slot] = b;
        }

        private void SiftUp(int index)
        {
            while (index > 0 && Before(heap[index], heap[(index - 1) / 2]))
            {
                int parent = (index - 1) / 2;
                Swap(index, parent);
                index = parent;
            }
        }

        private void SiftDown(int index)
        {
            while (index < count / 2)
            {
                int child = index * 2 + 1;
                if (child + 1 < count && Before(heap[child + 1], heap[child]))
                    child++;
                if (!Before(heap[child], heap[index]))
                    return;
                Swap(child, index);
                index = child;
            }
        }

        private int RemovePending(int index)
        {
            int slot = heap[index].Slot;
            pendingHashV2 = unchecked(pendingHashV2 - TaskHashV2(tasks[slot]));
            heapPositions[slot] = -1;
            count--;
            if (index < count)
            {
                heap[index] = heap[count];
                heapPositions[heap[index].Slot] = index;
                if (index > 0 && Before(heap[index], heap[(index - 1) / 2]))
                    SiftUp(index);
                else
                    SiftDown(index);
            }

            heap[count] = default;
            return slot;
        }

        public bool TryGetPending(long id, out ScheduledTask task)
        {
            int slot = FindTaskSlot(id);
            if (slot >= 0 && taskStates[slot] == 1)
            {
                task = tasks[slot];
                return true;
            }
            task = default;
            return false;
        }

        public bool TryGetHandle(long id, out ScheduledTaskHandle handle)
        {
            int slot = FindTaskSlot(id);
            handle = slot < 0 ? default : new ScheduledTaskHandle(slot, taskGenerations[slot]);
            return slot >= 0;
        }

        // The legacy id lookup is bounded by a preallocated table; handle cancellation bypasses it.
        public bool Cancel(long id)
        {
            int slot = FindTaskSlot(id);
            return slot >= 0 && Cancel(new ScheduledTaskHandle(slot, taskGenerations[slot]));
        }

        public bool Cancel(ScheduledTaskHandle handle)
        {
            int slot = handle.Slot;
            if (slot < 0 || slot >= tasks.Length || taskStates[slot] == 0 || taskGenerations[slot] != handle.Generation)
                return false;
            if (taskStates[slot] == 1)
            {
                RemovePending(heapPositions[slot]);
                ReleaseTaskSlot(slot);
            }
            else
                RemoveDue(slot);
            return true;
        }

        public void Advance(long tick, SimEventBuffer mirrors = null)
        {
            if (advancedThrough == long.MaxValue || tick != advancedThrough + 1)
                throw new InvalidOperationException("Scheduler ticks must be consecutive.");
            advancedThrough = tick;
            while (count != 0 && heap[0].Tick == tick)
            {
                int slot = RemovePending(0);
                ScheduledTask task = tasks[slot];
                EnqueueDue(slot);
                if (mirrors != null)
                {
                    if (stableTasks == null)
                        mirrors.Publish(task.Event);
                    else
                        mirrors.PublishStable(stableTasks[slot].Event);
                }
            }
        }

        public bool TryDequeue(out ScheduledTask task)
        {
            RequireLegacyTasks();
            if (dueCount == 0)
            {
                task = default;
                return false;
            }

            task = RemoveDue(dueHead);
            return true;
        }

        private void WritePendingInOrder(StateHash hash)
        {
            // These indices inherit the live heap's (tick, id) order. Consuming only the
            // scratch heap keeps hashing independent of layout without changing cancellation indices.
            for (int i = 0; i < count; i++)
                hashHeap[i] = i;
            int remaining = count;
            while (remaining != 0)
            {
                tasks[heap[hashHeap[0]].Slot].WriteState(hash);
                remaining--;
                if (remaining == 0)
                    break;
                int replacement = hashHeap[remaining];
                int index = 0;
                while (index < remaining / 2)
                {
                    int child = index * 2 + 1;
                    if (child + 1 < remaining && Before(heap[hashHeap[child + 1]], heap[hashHeap[child]]))
                        child++;
                    if (!Before(heap[hashHeap[child]], heap[replacement]))
                        break;
                    hashHeap[index] = hashHeap[child];
                    index = child;
                }

                hashHeap[index] = replacement;
            }
        }

        public void WriteState(StateHash hash)
        {
            hash.Add(nextId);
            hash.Add(advancedThrough);
            hash.Add(count);
            WritePendingInOrder(hash);
            hash.Add(dueCount);
            for (int slot = dueHead; slot >= 0; slot = dueNext[slot])
                tasks[slot].WriteState(hash);
        }
    }
}
