namespace WarCore.Sim
{
    public sealed partial class SimScheduler
    {
        private readonly StateHash taskHashSink = new StateHash();
        private ulong pendingHashV2;
        private ulong dueHashV2;

        private ulong TaskHashV2(ScheduledTask task)
        {
            taskHashSink.Reset();
            taskHashSink.Add("warcore.scheduler-task-v2");
            task.WriteState(taskHashSink);
            return taskHashSink.Value;
        }

        private void RestorePendingHashV2()
        {
            pendingHashV2 = 0;
            for (int i = 0; i < count; i++)
                pendingHashV2 = unchecked(pendingHashV2 + TaskHashV2(tasks[heap[i].Slot]));
        }

        // Unique task ids and validated (tick, id) order make each queue's order derivable from its set.
        // Addition and removal deliberately use modulo 2^64 arithmetic; overflow is part of this protocol.
        public void WriteStateV2(StateHash hash)
        {
            hash.Add("warcore.scheduler-v2");
            hash.Add(nextId);
            hash.Add(advancedThrough);
            hash.Add(count);
            hash.Add(pendingHashV2);
            hash.Add(dueCount);
            hash.Add(dueHashV2);
        }
    }
}
