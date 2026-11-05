using System.Collections.Generic;

namespace WarCore.Sim
{
    public sealed partial class SimScheduler
    {
        internal long AdvancedThrough => advancedThrough;

        internal void SaveScheduler(SimStateWriter writer)
        {
            writer.Int32(heap.Length);
            writer.Bool(frozen);
            writer.Int64(nextId);
            writer.Int64(advancedThrough);
            writer.Int32(count);
            for (int i = 0; i < count; i++)
                SaveTask(writer, tasks[heap[i].Slot]);
            writer.Int32(dueHead < 0 ? 0 : dueHead);
            writer.Int32(dueCount);
            for (int slot = dueHead; slot >= 0; slot = dueNext[slot])
                SaveTask(writer, tasks[slot]);
        }

        private static void SaveTask(SimStateWriter writer, ScheduledTask task)
        {
            writer.Int64(task.Id);
            SnapshotValues.Event(writer, task.Event);
        }

        private static ScheduledTask LoadTask(SimStateReader reader, long limit, HashSet<long> seen)
        {
            long id = reader.Int64();
            SimStateReader.Require(id > 0 && id < limit && seen.Add(id), "Invalid or duplicate scheduled task id.");
            return new ScheduledTask(id, SnapshotValues.Event(reader));
        }

        private void RestoreTaskSlot(int slot, ScheduledTask task, byte state)
        {
            NextTaskGeneration(slot);
            tasks[slot] = task;
            taskStates[slot] = state;
            AddTaskId(slot);
        }

        internal void LoadScheduler(SimStateReader reader, long tick)
        {
            int capacity = reader.Int32();
            bool savedFrozen = reader.Bool();
            long savedNextId = reader.Int64();
            long savedThrough = reader.Int64();
            SimStateReader.Require(capacity > 0 && capacity <= 1 << 28 && savedNextId > 0 && savedThrough == tick, "Invalid scheduler snapshot header.");
            int pending = reader.Count(capacity);
            var savedHeap = new ScheduledTask[capacity];
            var savedDue = new ScheduledTask[capacity];
            var seen = new HashSet<long>();
            for (int i = 0; i < pending; i++)
            {
                ScheduledTask task = LoadTask(reader, savedNextId, seen);
                SimStateReader.Require(task.Event.Tick > tick, "Pending task is not in the future.");
                SimStateReader.Require(i == 0 || !Before(task, savedHeap[(i - 1) / 2]), "Invalid scheduler heap order.");
                savedHeap[i] = task;
            }

            int head = reader.Int32();
            int ready = reader.Count(capacity - pending);
            SimStateReader.Require(head >= 0 && head < capacity, "Invalid due queue head.");
            for (int i = 0; i < ready; i++)
            {
                ScheduledTask task = LoadTask(reader, savedNextId, seen);
                SimStateReader.Require(task.Event.Tick > 0 && task.Event.Tick <= tick, "Due task is outside the elapsed ticks.");
                SimStateReader.Require(i == 0 || Before(savedDue[i - 1], task), "Invalid due task order.");
                savedDue[i] = task;
            }

            // Storage slots are runtime handles, not snapshot identity. Keep the legacy payload
            // order and due-head header, but advance local generations to invalidate pre-restore handles.
            heap = new SchedulerKey[capacity];
            hashHeap = new int[capacity];
            InitializeSlots(capacity);
            InitializeDue(capacity);
            int cursor = head;
            for (int i = 0; i < ready; i++)
            {
                RestoreTaskSlot(cursor, savedDue[i], 2);
                EnqueueDue(cursor);
                cursor = cursor == capacity - 1 ? 0 : cursor + 1;
            }
            for (int i = 0; i < pending; i++)
            {
                RestoreTaskSlot(cursor, savedHeap[i], 1);
                heap[i] = new SchedulerKey(savedHeap[i].Event.Tick, savedHeap[i].Id, cursor);
                heapPositions[cursor] = i;
                cursor = cursor == capacity - 1 ? 0 : cursor + 1;
            }
            RebuildFreeSlots();
            count = pending;
            RestorePendingHashV2();

            nextId = savedNextId;
            advancedThrough = savedThrough;
            frozen = savedFrozen;
        }
    }

    public sealed partial class SimEventBuffer
    {
        internal bool IsActive => active;
        internal long CurrentTick => currentTick;

        internal void SaveEvents(SimStateWriter writer)
        {
            writer.Int32(slots.Length);
            writer.Int32(hardLimit);
            writer.Int64(currentTick);
            writer.Int64(NextSequence);
            writer.Int64(EventsDroppedThisTick);
            writer.Int64(EventsDroppedTotal);
            writer.Bool(frozen);
            for (int i = 0; i < slots.Length; i++)
            {
                writer.Int32(slots[i].Length);
                writer.Int64(ticks[i]);
                writer.Int32(counts[i]);
                for (int j = 0; j < counts[i]; j++)
                    SnapshotValues.Event(writer, slots[i][j]);
            }
        }

        internal void LoadEvents(SimStateReader reader, long tick)
        {
            SimStateReader.Require(reader.Int32() == slots.Length && reader.Int32() == hardLimit, "Event buffer configuration mismatch.");
            long savedTick = reader.Int64();
            long sequence = reader.Int64();
            long dropped = reader.Int64();
            long total = reader.Int64();
            bool savedFrozen = reader.Bool();
            SimStateReader.Require(savedTick == tick && sequence > 0 && dropped >= 0 && total >= dropped, "Invalid event snapshot counters.");
            for (int i = 0; i < slots.Length; i++)
            {
                int size = reader.Count(hardLimit + 1);
                SimStateReader.Require(size >= 2 && (!savedFrozen || size == hardLimit + 1), "Invalid event slot capacity.");
                long slotTick = reader.Int64();
                int count = reader.Count(size);
                SimStateReader.Require(slotTick >= 0 && slotTick <= tick && (slotTick != 0 || count == 0), "Invalid event slot tick.");
                SimStateReader.Require(slotTick == 0 || (slotTick % slots.Length == i && tick - slotTick < slots.Length), "Event slot lies outside its retained window.");
                var items = new SimEvent[size];
                long previous = 0;
                for (int j = 0; j < count; j++)
                {
                    SimEvent item = SnapshotValues.Event(reader);
                    SimStateReader.Require(item.Tick == slotTick && item.Sequence > previous && item.Sequence < sequence, "Invalid retained event order.");
                    items[j] = item;
                    previous = item.Sequence;
                }

                // No Publish: gaps caused by dropped events and original sequence numbers survive.
                slots[i] = items;
                ticks[i] = slotTick;
                counts[i] = count;
            }

            currentTick = savedTick;
            NextSequence = sequence;
            EventsDroppedThisTick = dropped;
            EventsDroppedTotal = total;
            frozen = savedFrozen;
            active = false;
        }
    }
}
