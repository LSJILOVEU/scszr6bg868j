using System;

namespace WarCore.Sim
{
    public readonly struct TaskKey : IEquatable<TaskKey>, IComparable<TaskKey>
    {
        public long DueTick { get; }
        public ulong OriginDomain { get; }
        public StableId Origin { get; }
        public ulong Counter { get; }
        public ulong Kind { get; }
        public TaskKey(long dueTick, ulong originDomain, StableId origin, ulong counter, ulong kind)
        {
            if (dueTick <= 0 || originDomain > 1)
                throw new SimBoundaryException("Invalid stable task key.");
            DueTick = dueTick;
            OriginDomain = originDomain;
            Origin = origin;
            Counter = counter;
            Kind = kind;
        }
        public int CompareTo(TaskKey other)
        {
            int c = DueTick.CompareTo(other.DueTick);
            if (c != 0)
                return c;
            c = OriginDomain.CompareTo(other.OriginDomain);
            if (c != 0)
                return c;
            c = Origin.CompareTo(other.Origin);
            if (c != 0)
                return c;
            c = Counter.CompareTo(other.Counter);
            return c != 0 ? c : Kind.CompareTo(other.Kind);
        }
        public bool Equals(TaskKey other) => CompareTo(other) == 0;
        public override bool Equals(object other) => other is TaskKey value && Equals(value);
        public override int GetHashCode()
        {
            ulong value = 14695981039346656037UL;
            StableEntityKey.Word(ref value,(ulong)DueTick);
            StableEntityKey.Word(ref value,OriginDomain);
            StableEntityKey.Word(ref value,Origin.Value);
            StableEntityKey.Word(ref value,Counter);
            StableEntityKey.Word(ref value,Kind);
            return SimInteger.Int32Bits(StableEntityKey.Mix(value));
        }
    }

    public readonly struct StableSimEvent
    {
        public SimEvent Payload { get; }
        public EntityLink Source { get; }
        public EntityLink Target { get; }
        public EventTypeId Type => Payload.Type;
        public long Tick => Payload.Tick;
        public long Sequence => Payload.Sequence;
        public int Tag => Payload.Tag;
        public StableSimEvent(EventTypeId type, EntityLink source = default, EntityLink target = default,
            SimVec3 position = default, SimVec3 aux = default, SimFix magnitude = default, SimFix radius = default, int tag = 0)
            : this(new SimEvent(type, position:position, aux:aux, magnitude:magnitude, radius:radius, tag:tag), source, target)
        {
        }
        internal StableSimEvent(SimEvent payload, EntityLink source, EntityLink target)
        {
            if (payload.Source.Generation != 0 || payload.Target.Generation != 0)
                throw new SimBoundaryException("Stable events cannot contain physical entity references.");
            Payload = payload;
            Source = source;
            Target = target;
        }
        internal StableSimEvent At(long tick, long sequence = 0) => new StableSimEvent(Payload.At(tick,sequence), Source, Target);
    }

    public readonly struct StableScheduledTask
    {
        public TaskKey Key { get; }
        public StableSimEvent Event { get; }
        internal StableScheduledTask(TaskKey key, StableSimEvent item)
        {
            Key = key;
            Event = item;
        }
    }

    public sealed partial class SimScheduler
    {
        private readonly IdentityMode identityMode;
        internal EntityStore StableEntities { get; set; }
        private StableScheduledTask[] stableTasks;
        private struct TaskCursor : IComparable<TaskCursor>
        {
            internal ulong Domain;
            internal StableId Origin;
            internal ulong Next;
            public int CompareTo(TaskCursor other)
            {
                int c = Domain.CompareTo(other.Domain);
                return c != 0 ? c : Origin.CompareTo(other.Origin);
            }
        }
        private readonly struct OriginKey : IEquatable<OriginKey>
        {
            internal readonly ulong Domain;
            internal readonly StableId Id;
            internal OriginKey(ulong domain, StableId id)
            {
                Domain = domain;
                Id = id;
            }
            public bool Equals(OriginKey other) => Domain == other.Domain && Id.Equals(other.Id);
            public override int GetHashCode() => SimInteger.Int32Bits(StableEntityKey.Mix(Id.Value) ^ Domain);
        }
        private TaskCursor[] taskCursors;
        private int taskCursorCount;
        private BoundedLookup<OriginKey> cursorLookup;
        private BoundedLookup<TaskKey> stableTaskLookup;
        private int FindCursor(ulong domain, StableId origin) => cursorLookup.Find(new OriginKey(domain,origin));
        private bool EntityCursor(ulong domain, StableId origin) => domain == 0 && StableEntities != null && StableEntities.HasTaskOrigin(origin);
        private void RequireStableTasks()
        {
            if (identityMode != IdentityMode.StableWorld)
                throw new SimBoundaryException("Stable task operations require StableWorld.");
        }
        private void RequireLegacyTasks()
        {
            if (identityMode != IdentityMode.Legacy)
                throw new SimBoundaryException("StableWorld requires a TaskKey and stable event references.");
        }
        public ScheduledTaskHandle ScheduleStable(TaskKey key, StableSimEvent template)
        {
            RequireStableTasks();
            StableEntities?.ValidateLink(template.Source,false);
            StableEntities?.ValidateLink(template.Target,false);
            if (key.DueTick <= advancedThrough || key.Counter == ulong.MaxValue)
                throw new SimBoundaryException("Stable task tick or counter exhausted.");
            if (FindStableTask(key) >= 0)
                throw new SimBoundaryException("Duplicate stable task key.");
            bool owned = EntityCursor(key.OriginDomain,key.Origin);
            if (StableEntities != null && StableEntities.UsesRetainedProtocol)
            {
                if (key.OriginDomain == 0 && !owned)
                    throw new SimBoundaryException("Scheduled origin must be an active registered entity.");
                if (count == tasks.Length - dueCount)
                    throw new SimBoundaryException("Retained scheduler task capacity exhausted.");
            }
            if (owned)
            {
                StableEntities.TaskCounter(key.Origin,true);
                StableEntities.RequirePlacedCursor(key.Origin,key.Counter,true);
            }
            int cursor = owned ? -1 : FindCursor(key.OriginDomain, key.Origin);
            if (!owned && cursor < 0 && taskCursorCount == taskCursors.Length)
                throw new SimBoundaryException("Stable task origin capacity exhausted.");
            var task = new StableScheduledTask(key, template.At(key.DueTick));
            ScheduledTaskHandle handle = ScheduleCore(key.DueTick, task.Event.Payload, task);
            if (owned)
            {
                if (StableEntities.TaskCounter(key.Origin,false) <= key.Counter)
                    StableEntities.SetTaskCounter(key.Origin,key.Counter+1);
                return handle;
            }
            if (cursor < 0)
            {
                cursor = taskCursorCount++;
                taskCursors[cursor] = new TaskCursor { Domain = key.OriginDomain, Origin = key.Origin };
                cursorLookup.Add(new OriginKey(key.OriginDomain,key.Origin),cursor);
            }
            if (taskCursors[cursor].Next <= key.Counter)
                taskCursors[cursor].Next = key.Counter + 1;
            return handle;
        }
        public TaskKey ScheduleFor(long tick, ulong originDomain, StableId origin, ulong kind, StableSimEvent template)
        {
            RequireStableTasks();
            int cursor = FindCursor(originDomain, origin);
            ulong counter = EntityCursor(originDomain,origin) ? StableEntities.TaskCounter(origin,true)
                : cursor < 0 ? 0 : taskCursors[cursor].Next;
            var key = new TaskKey(tick, originDomain, origin, counter, kind);
            ScheduleStable(key, template);
            return key;
        }
        private int FindStableTask(TaskKey key) => stableTaskLookup.Find(key);
        public bool TryGetPendingStable(TaskKey key, out StableScheduledTask task)
        {
            RequireStableTasks();
            int slot = FindStableTask(key);
            task = slot < 0 ? default : stableTasks[slot];
            return slot >= 0 && taskStates[slot] == 1;
        }
        public bool CancelStable(TaskKey key)
        {
            RequireStableTasks();
            int slot = FindStableTask(key);
            return slot >= 0 && Cancel(new ScheduledTaskHandle(slot, taskGenerations[slot]));
        }
        public bool TryDequeueStable(out StableScheduledTask task)
        {
            RequireStableTasks();
            if (dueHead < 0)
            {
                task = default;
                return false;
            }
            task = stableTasks[dueHead];
            RemoveDue(dueHead);
            return true;
        }
        public bool TryDequeueStable(EventTypeId type, out StableScheduledTask task)
        {
            RequireStableTasks();
            if (!dueTypes.TryGetValue(type.Value, out DueBucket bucket))
            {
                task = default;
                return false;
            }
            task = stableTasks[bucket.Head];
            RemoveDue(bucket.Head);
            return true;
        }
    }
}
