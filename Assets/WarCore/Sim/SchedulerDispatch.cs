using System;
using System.Collections.Generic;

namespace WarCore.Sim
{
    public sealed partial class SimScheduler
    {
        private struct DueBucket
        {
            internal int Head;
            internal int Tail;
        }

        private int[] dueNext;
        private int[] duePrevious;
        private int[] typeNext;
        private int[] typePrevious;
        private int dueTail;
        private Dictionary<int, DueBucket> dueTypes;

        // Diagnostics do not alter authoritative state. Queue persistence tests can explicitly disable them.
        public bool DiagnoseUnconsumedTasks { get; set; } = true;

        private void InitializeDue(int capacity)
        {
            dueNext = new int[capacity];
            duePrevious = new int[capacity];
            typeNext = new int[capacity];
            typePrevious = new int[capacity];
            dueTypes = new Dictionary<int, DueBucket>(capacity);
            dueHead = -1;
            dueTail = -1;
            dueCount = 0;
            dueHashV2 = 0;
        }

        private void GrowDue(int capacity)
        {
            Array.Resize(ref dueNext, capacity);
            Array.Resize(ref duePrevious, capacity);
            Array.Resize(ref typeNext, capacity);
            Array.Resize(ref typePrevious, capacity);
            var grown = new Dictionary<int, DueBucket>(capacity);
            foreach (KeyValuePair<int, DueBucket> bucket in dueTypes)
                grown.Add(bucket.Key, bucket.Value);
            dueTypes = grown;
        }

        private void EnqueueDue(int slot)
        {
            ScheduledTask task = tasks[slot];
            taskStates[slot] = 2;
            dueNext[slot] = -1;
            duePrevious[slot] = dueTail;
            if (dueTail < 0)
                dueHead = slot;
            else
                dueNext[dueTail] = slot;
            dueTail = slot;
            int type = task.Event.Type.Value;
            if (!dueTypes.TryGetValue(type, out DueBucket bucket))
                bucket = new DueBucket { Head = -1, Tail = -1 };
            typeNext[slot] = -1;
            typePrevious[slot] = bucket.Tail;
            if (bucket.Tail < 0)
                bucket.Head = slot;
            else
                typeNext[bucket.Tail] = slot;
            bucket.Tail = slot;
            dueTypes[type] = bucket;
            dueCount++;
            dueHashV2 = unchecked(dueHashV2 + TaskHashV2(task));
        }

        private ScheduledTask RemoveDue(int slot)
        {
            ScheduledTask task = tasks[slot];
            int previous = duePrevious[slot];
            int next = dueNext[slot];
            if (previous < 0)
                dueHead = next;
            else
                dueNext[previous] = next;
            if (next < 0)
                dueTail = previous;
            else
                duePrevious[next] = previous;
            int type = task.Event.Type.Value;
            DueBucket bucket = dueTypes[type];
            previous = typePrevious[slot];
            next = typeNext[slot];
            if (previous < 0)
                bucket.Head = next;
            else
                typeNext[previous] = next;
            if (next < 0)
                bucket.Tail = previous;
            else
                typePrevious[next] = previous;
            if (bucket.Head < 0)
                dueTypes.Remove(type);
            else
                dueTypes[type] = bucket;
            ReleaseTaskSlot(slot);
            dueCount--;
            dueHashV2 = unchecked(dueHashV2 - TaskHashV2(task));
            return task;
        }

        // Separate type links preserve both the global FIFO and each consumer's FIFO without compaction.
        public bool TryDequeue(EventTypeId type, out ScheduledTask task)
        {
            RequireLegacyTasks();
            if (!dueTypes.TryGetValue(type.Value, out DueBucket bucket))
            {
                task = default;
                return false;
            }
            task = RemoveDue(bucket.Head);
            return true;
        }

        internal void ValidateDueAtTickEnd()
        {
            // The oldest FIFO entry suffices because Advance appends consecutive ticks.
            if (DiagnoseUnconsumedTasks && dueHead >= 0 && advancedThrough > 1
                && tasks[dueHead].Event.Tick < advancedThrough - 1)
                throw new InvalidOperationException("Unconsumed scheduled task: type=" + tasks[dueHead].Event.Type.Value
                    + "; dueTick=" + tasks[dueHead].Event.Tick + "; currentTick=" + advancedThrough + ".");
        }
    }
}
