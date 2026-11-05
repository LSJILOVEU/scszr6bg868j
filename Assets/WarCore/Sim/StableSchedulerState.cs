using System;

namespace WarCore.Sim
{
    public sealed partial class SimScheduler
    {
        private readonly StateHash stableTaskHash = new StateHash();
        private SimStateWriter stableTaskWriter;
        private StateHash stableSchedulerHash;
        private SimStateWriter stableSchedulerWriter;
        private int[] stableOrder;
        private static void WriteStableTask(SimStateWriter writer, StableScheduledTask task)
        {
            writer.TaskKeyValue(task.Key);
            writer.StableEvent(task.Event);
        }
        private ulong StableTaskLeaf(int slot)
        {
            stableTaskWriter.ResetStable();
            WriteStableTask(stableTaskWriter,stableTasks[slot]);
            return StableEntityKey.Mix(stableTaskHash.Value);
        }
        public ulong StateHashStable()
        {
            RequireStableTasks();
            stableSchedulerWriter.ResetStable();
            WriteStableScheduler(stableSchedulerWriter);
            return StableEntityKey.Mix(stableSchedulerHash.Value);
        }
        internal void WriteStableScheduler(SimStateWriter writer)
        {
            RequireStableTasks();
            ulong pendingSum = 0;
            ulong dueSum = 0;
            for (int slot = 0; slot < tasks.Length; slot++)
            {
                if (taskStates[slot] == 1)
                    pendingSum = unchecked(pendingSum + StableTaskLeaf(slot));
                else if (taskStates[slot] == 2)
                    dueSum = unchecked(dueSum + StableTaskLeaf(slot));
            }
            writer.String("warcore.scheduler-stable-v3");
            writer.Int64(advancedThrough);
            writer.String("pending");
            writer.Int32(count);
            writer.UInt64(pendingSum);
            writer.String("due");
            writer.Int32(dueCount);
            writer.UInt64(dueSum);
            WriteTaskCursors(writer);
        }
        private static ulong CursorLeaf(ulong domain, StableId origin, ulong next)
        {
            ulong leaf = 14695981039346656037UL;
            StableEntityKey.Word(ref leaf,domain);
            StableEntityKey.Word(ref leaf,origin.Value);
            StableEntityKey.Word(ref leaf,next);
            return StableEntityKey.Mix(leaf);
        }
        private void WriteTaskCursors(SimStateWriter writer)
        {
            ulong sum = 0;
            int total = taskCursorCount;
            for (int i = 0; i < taskCursorCount; i++)
                sum = unchecked(sum + CursorLeaf(taskCursors[i].Domain,taskCursors[i].Origin,taskCursors[i].Next));
            if (StableEntities != null)
                for (int i = 0; i < StableEntities.RegisteredIdentityCount; i++)
                {
                    ulong next = StableEntities.TaskCounterAt(i);
                    if (next == 0)
                        continue;
                    total++;
                    sum = unchecked(sum + CursorLeaf(0,StableEntities.IdentityAt(i),next));
                }
            writer.Int32(total);
            writer.UInt64(sum);
        }
        private int SortStableTasks()
        {
            int length = 0;
            for (int slot = 0; slot < tasks.Length; slot++)
                if (taskStates[slot] != 0)
                    stableOrder[length++] = slot;
            for (int root = length / 2 - 1; root >= 0; root--)
                SiftStableOrder(root,length);
            for (int end = length-1; end > 0; end--)
            {
                SwapStableOrder(0,end);
                SiftStableOrder(0,end);
            }
            return length;
        }
        private void SwapStableOrder(int a, int b)
        {
            int value = stableOrder[a];
            stableOrder[a] = stableOrder[b];
            stableOrder[b] = value;
        }
        private bool StableOrderBefore(int a, int b) => stableTasks[stableOrder[a]].Key.CompareTo(stableTasks[stableOrder[b]].Key) < 0;
        private void SiftStableOrder(int root, int length)
        {
            while (root < length / 2)
            {
                int child = root*2+1;
                if (child+1 < length && StableOrderBefore(child,child+1))
                    child++;
                if (!StableOrderBefore(root,child))
                    return;
                SwapStableOrder(root,child);
                root = child;
            }
        }
        internal void SaveStableScheduler(SimStateWriter writer)
        {
            writer.Int64(advancedThrough);
            int length = SortStableTasks();
            writer.Int32(length);
            for (int i = 0; i < length; i++)
                WriteStableTask(writer,stableTasks[stableOrder[i]]);
            int owned = StableEntities == null ? 0 : StableEntities.TaskOriginCount;
            var order = new TaskCursor[taskCursorCount + owned];
            Array.Copy(taskCursors,order,taskCursorCount);
            int at = taskCursorCount;
            if (StableEntities != null)
                for (int i = 0; i < StableEntities.RegisteredIdentityCount; i++)
                    if (StableEntities.TaskCounterAt(i) != 0)
                        order[at++] = new TaskCursor { Origin=StableEntities.IdentityAt(i), Next=StableEntities.TaskCounterAt(i) };
            ValueOrder.Sort(order.AsSpan());
            writer.Int32(order.Length);
            foreach (TaskCursor cursor in order)
            {
                writer.UInt64(cursor.Domain);
                writer.UInt64(cursor.Origin.Value);
                writer.UInt64(cursor.Next);
            }
        }
        internal void LoadStableScheduler(SimStateReader reader, long tick)
        {
            SimStateReader.Require(reader.Int64() == tick,"Stable scheduler tick mismatch.");
            InitializeSlots(Capacity);
            InitializeDue(Capacity);
            Array.Clear(stableTasks,0,stableTasks.Length);
            stableTaskLookup.Clear();
            cursorLookup.Clear();
            Array.Clear(heap,0,heap.Length);
            count = 0;
            nextId = 1;
            advancedThrough = tick;
            int length = reader.Count(Capacity);
            TaskKey previous = default;
            for (int i = 0; i < length; i++)
            {
                TaskKey key = reader.ReadTaskKey();
                StableSimEvent item = reader.StableEvent();
                SimStateReader.Require((i == 0 || previous.CompareTo(key) < 0) && item.Tick == key.DueTick && item.Sequence == 0,
                    "Invalid stable task record.");
                int slot = AllocateTaskSlot();
                tasks[slot] = new ScheduledTask(nextId++,item.Payload);
                stableTasks[slot] = new StableScheduledTask(key,item);
                stableTaskLookup.Add(key,slot);
                AddTaskId(slot);
                if (key.DueTick <= tick)
                    EnqueueDue(slot);
                else
                {
                    taskStates[slot] = 1;
                    heap[count] = new SchedulerKey(key.DueTick,tasks[slot].Id,slot);
                    heapPositions[slot] = count;
                    SiftUp(count++);
                }
                previous = key;
            }
            int maximum = taskCursors.Length + (StableEntities == null ? 0 : StableEntities.IdentityCapacity);
            int cursors = reader.Count(maximum);
            taskCursorCount = 0;
            TaskCursor previousCursor = default;
            for (int i = 0; i < cursors; i++)
            {
                var cursor = new TaskCursor { Domain=reader.UInt64(), Origin=new StableId(reader.UInt64()), Next=reader.UInt64() };
                SimStateReader.Require(cursor.Domain <= 1 && cursor.Next > 0 && (i == 0 || previousCursor.CompareTo(cursor) < 0),"Invalid stable task cursor.");
                if (EntityCursor(cursor.Domain,cursor.Origin))
                    StableEntities.SetTaskCounter(cursor.Origin,cursor.Next);
                else
                {
                    SimStateReader.Require(StableEntities == null || !StableEntities.UsesRetainedProtocol || cursor.Domain == 1,
                        "Retained entity cursor must belong to a registered identity.");
                    SimStateReader.Require(taskCursorCount < taskCursors.Length,"Stable system cursor capacity exceeded.");
                    taskCursors[taskCursorCount] = cursor;
                    cursorLookup.Add(new OriginKey(cursor.Domain,cursor.Origin),taskCursorCount++);
                }
                previousCursor = cursor;
            }
            for (int slot = 0; slot < tasks.Length; slot++)
                if (taskStates[slot] != 0)
                {
                    TaskKey key = stableTasks[slot].Key;
                    int cursor = FindCursor(key.OriginDomain,key.Origin);
                    bool present = EntityCursor(key.OriginDomain,key.Origin)
                        ? StableEntities.TaskCounter(key.Origin,false) > key.Counter
                        : cursor >= 0 && taskCursors[cursor].Next > key.Counter;
                    bool historical = StableEntities != null && StableEntities.UsesRetainedProtocol && key.OriginDomain == 0
                        && !StableEntities.HasTaskOrigin(key.Origin);
                    SimStateReader.Require(present || historical,"Missing stable task cursor.");
                }
            RestorePendingHashV2();
        }
    }
}
