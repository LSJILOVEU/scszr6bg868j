using System;
namespace WarCore.Sim
{
    internal sealed partial class GameplayState
    {
        private GameplayStatusRow[] statuses, frozenStatuses;
        private int statusCount, frozenStatusCount;
        private ulong statusSum, effectNext;
        private long immuneRejections, statusCapacityRejections;
        private GameplayModifier[] statusModifiers;
        private ulong[] frozenImmunity;
        private int[] statusOrder;
        private void InitStatuses()
        {
            statuses = new GameplayStatusRow[schema.Capacities.Statuses];
            frozenStatuses = new GameplayStatusRow[statuses.Length];
            frozenImmunity = new ulong[schema.Capacities.Owners];
            statusOrder = new int[statuses.Length];
            int modifiers = 0;
            for (int i = 0; i < schema.PipelineCount; i++)
                if (schema.PipelineAt(i).ModifierCount > modifiers)
                    modifiers = schema.PipelineAt(i).ModifierCount;
            int per = 0;
            for (int i = 0; i < schema.StatusCount; i++)
                if (schema.StatusAt(i).ModifierCount > per)
                    per = schema.StatusAt(i).ModifierCount;
            statusModifiers = new GameplayModifier[modifiers + per * statuses.Length];
        }
        internal int StatusCount => statusCount;
        internal ulong StatusTags(StableId target)
        {
            int lo = 0, hi = statusCount;
            while (lo < hi)
            {
                int m = lo + (hi - lo) / 2;
                if (statuses[m].Target.Id.CompareTo(target) < 0)
                    lo = m + 1;
                else
                    hi = m;
            }
            ulong tags = 0;
            for (int i = lo; i < statusCount && statuses[i].Target.Id.Equals(target); i++)
                tags |= schema.StatusAt(schema.FindStatus(statuses[i].Key.Definition)).Tags;
            return tags;
        }
        internal GameplayStatusView StatusAt(int index)
        {
            if (index < 0 || index >= statusCount)
                throw new SimBoundaryException("M0C_STATUS_INDEX");
            return new GameplayStatusView(statuses[index]);
        }
        private int FindStatusToken(ulong token)
        {
            for (int i = 0; i < statusCount; i++)
                if (statuses[i].Key.Token == token)
                    return i;
            return -1;
        }
        private void WriteStatus(int index, GameplayStatusRow row)
        {
            statusSum = unchecked(statusSum - RowLeaf(7, index));
            statuses[index] = row;
            statusSum = unchecked(statusSum + RowLeaf(7, index));
        }
        private void DeleteStatus(int at, int noticePhase = -1)
        {
            GameplayStatusRow r = statuses[at];
            if (noticePhase >= 0)
                StatusNotice(r, noticePhase, 0);
            if (r.HasExpiryTask)
                sim.Scheduler.CancelStable(r.ExpiryTask);
            if (r.HasPulseTask)
                sim.Scheduler.CancelStable(r.PulseTask);
            statusSum = unchecked(statusSum - RowLeaf(7, at));
            for (int i = at + 1; i < statusCount; i++)
                statuses[i - 1] = statuses[i];
            statuses[--statusCount] = default;
        }
        private void CleanupStatuses(StableId target, int noticePhase = -1)
        {
            // The canonical owner range avoids a separate scan for each owned instance.
            int lo = 0, hi = statusCount;
            while (lo < hi)
            {
                int m = lo + (hi - lo) / 2;
                if (statuses[m].Target.Id.CompareTo(target) < 0)
                    lo = m + 1;
                else
                    hi = m;
            }
            while (lo < statusCount && statuses[lo].Target.Id.Equals(target))
                DeleteStatus(lo, noticePhase);
        }
        private long StatusFuture(int duration) => GameplayMath.FutureTick(sim.Clock.Tick, duration, ref timeSaturations);
        private void ScheduleStatus(ref GameplayStatusRow r)
        {
            if (r.ExpiryTick != 0 && !r.HasExpiryTask)
            {
                r.ExpiryTask = sim.Scheduler.ScheduleFor(r.ExpiryTick, 0, r.Target.Id, r.Key.Token, new StableSimEvent(GameplayEvents.StatusExpiry, target: new EntityLink(r.Target.Id)));
                r.HasExpiryTask = true;
            }
            if (r.NextPulseTick != 0 && !r.HasPulseTask && (r.ExpiryTick == 0 || r.NextPulseTick < r.ExpiryTick))
            {
                r.PulseTask = sim.Scheduler.ScheduleFor(r.NextPulseTick, 0, r.Target.Id, r.Key.Token, new StableSimEvent(GameplayEvents.StatusPulse, target: new EntityLink(r.Target.Id)));
                r.HasPulseTask = true;
            }
        }
        private void BeginStatuses()
        {
            while (sim.Scheduler.TryDequeueStable(GameplayEvents.StatusExpiry, out StableScheduledTask due))
            {
                int i = FindStatusToken(due.Key.Kind);
                if (i < 0 || !statuses[i].HasExpiryTask || !statuses[i].ExpiryTask.Equals(due.Key))
                    throw new SimBoundaryException("M0C_STATUS_EXPIRY_TASK");
                if (statuses[i].ExpiryTick <= sim.Clock.Tick)
                    DeleteStatus(i, 0); // S2_MUTATION:expiry_boundary
            }
            while (sim.Scheduler.TryDequeueStable(GameplayEvents.StatusPulse, out StableScheduledTask due))
            {
                int i = FindStatusToken(due.Key.Kind);
                if (i < 0 || !statuses[i].HasPulseTask || !statuses[i].PulseTask.Equals(due.Key))
                    throw new SimBoundaryException("M0C_STATUS_PULSE_TASK");
                GameplayStatusRow r = statuses[i];
                GameplayStatusDef d = schema.StatusAt(schema.FindStatus(r.Key.Definition));
                if (r.ExpiryTick != 0 && r.ExpiryTick <= sim.Clock.Tick)
                    throw new SimBoundaryException("M0C_STATUS_EXPIRED_PULSE");
                if (r.PulseOrdinal == uint.MaxValue)
                    throw new SimBoundaryException("M0C_STATUS_PULSE_EXHAUSTED");
                for (int a = 0; a < d.PeriodicActionCount; a++)
                {
                    GameplayPeriodicAction action = d.PeriodicActionAt(a);
                    var key = new GameplayApplyKey(r.Attribution.Source, r.Attribution.CastToken, 1, r.Target.Id, r.Key.Token, action.Ordinal, r.PulseOrdinal);
                    long amount = ScaleStackAmount(action.AmountRaw, r.Stacks, GameplayMath.MaxMagnitude);
                    GameplayFailure f = Enqueue(action.IsDamage, r.Target, key, action.Definition, amount);
                    if (f != GameplayFailure.None)
                        throw new SimBoundaryException("M0C_STATUS_PULSE_TARGET");
                }
                r.PulseOrdinal++;
                r.HasPulseTask = false;
                r.PulseTask = default;
                r.NextPulseTick = StatusFuture(d.PeriodTicks); // S2_MUTATION:pulse_successor
                WriteStatus(i, r);
            }
            // Allocate successor task counters only after all due callbacks, in instance-key order.
            for (int i = 0; i < statusCount; i++)
            {
                var r = statuses[i];
                bool had = r.HasPulseTask;
                ScheduleStatus(ref r);
                if (had != r.HasPulseTask)
                    WriteStatus(i, r);
            }
        }
        private static int StatusCommandOrder(GameplayCommand a, GameplayCommand b)
        {
            int c = (a.Kind == 3 ? 0 : 1).CompareTo(b.Kind == 3 ? 0 : 1);
            if (c != 0)
                return c;
            // EffectNext allocation follows full ApplyKey, whose last member is TargetStableId.
            c = a.Key.CompareTo(b.Key);
            return c != 0 ? c : a.Target.Id.CompareTo(b.Target.Id);
        }
        private void StatusCommandSift(int root, int length)
        {
            while (root < length / 2)
            {
                int child = root * 2 + 1;
                if (child + 1 < length && StatusCommandOrder(commands[child], commands[child + 1]) < 0)
                    child++;
                if (StatusCommandOrder(commands[root], commands[child]) >= 0)
                    return;
                GameplayCommand swap = commands[root];
                commands[root] = commands[child];
                commands[child] = swap;
                root = child;
            }
        }
        private void SortStatusCommands()
        {
            for (int root = commandCount / 2; root > 0;)
            {
                root--;
                StatusCommandSift(root, commandCount);
            }
            for (int end = commandCount - 1; end > 0; end--)
            {
                GameplayCommand swap = commands[0];
                commands[0] = commands[end];
                commands[end] = swap;
                StatusCommandSift(0, end);
            }
        }
        private void SettleStatuses()
        {
            if (schema.StatusCount == 0)
                return;
            SortStatusCommands();
            for (int c = 0; c < commandCount; c++)
                if (commands[c].Kind == 3 && ValidateTarget(commands[c].Target) == GameplayFailure.None)
                    ApplyDispel(commands[c]);
            for (int o = 0; o < ownerCount; o++)
                frozenImmunity[o] = 0;
            for (int i = 0; i < statusCount; i++)
                frozenImmunity[OwnerIndex(statuses[i].Target.Id)] |= schema.StatusAt(schema.FindStatus(statuses[i].Key.Definition)).ImmunityTags;
            for (int c = 0; c < commandCount; c++)
            {
                GameplayCommand item = commands[c];
                if ((item.Kind != 2 && item.Kind != 4) || ValidateTarget(item.Target) != GameplayFailure.None)
                    continue;
                GameplayStatusDef d = schema.StatusAt(schema.FindStatus(item.Definition));
                if ((frozenImmunity[OwnerIndex(item.Target.Id)] & d.Tags) != 0)
                {
                    Count(ref immuneRejections);
                    QueueNotice(21, item.Target.Id, item.Key, 5, new StableSimEvent(GameplayEvents.StatusChanged, new EntityLink(item.Key.Source), new EntityLink(item.Target.Id), tag: -1));
                    continue;
                } // S2_MUTATION:immunity_freeze
                ApplyStatus(item, d);
            }
            frozenStatusCount = statusCount;
            for (int i = 0; i < statusCount; i++)
                frozenStatuses[i] = statuses[i]; // S2_MUTATION:defense_freeze
        }
        private void ApplyDispel(GameplayCommand item)
        {
            for (int n = 0; n < item.Maximum; n++)
            {
                int chosen = -1;
                for (int i = 0; i < statusCount; i++)
                {
                    GameplayStatusRow r = statuses[i];
                    GameplayStatusDef d = schema.StatusAt(schema.FindStatus(r.Key.Definition));
                    if (!r.Target.Id.Equals(item.Target.Id) || (d.DispelTags & item.Definition) == 0)
                        continue;
                    if (chosen < 0 || d.Priority < schema.StatusAt(schema.FindStatus(statuses[chosen].Key.Definition)).Priority
                        || d.Priority == schema.StatusAt(schema.FindStatus(statuses[chosen].Key.Definition)).Priority && r.Key.CompareTo(statuses[chosen].Key) < 0)
                        chosen = i; // S2_MUTATION:dispel_order
                }
                if (chosen < 0)
                    break;
                DeleteStatus(chosen, 20);
            }
        }
        private void ApplyStatus(GameplayCommand item, GameplayStatusDef d)
        {
            if (d.DurationKind == GameplayStatusDuration.Instant || d.DurationKind == GameplayStatusDuration.Timed && d.DurationTicks == 0)
                return;
            long strength = item.Kind == 2 && item.Amount >= 0 ? item.Amount : d.StrengthRaw;
            int at = -1;
            for (int i = 0; i < statusCount; i++)
                if (SameGroup(statuses[i], item, d))
                {
                    at = i;
                    break;
                }
            if (d.StackPolicy == GameplayStackPolicy.Independent)
                at = -1;
            bool replace = d.StackPolicy == GameplayStackPolicy.Strongest;
            if (at >= 0)
            {
                GameplayStatusRow r = statuses[at];
                if (replace && (strength < r.StrengthRaw || strength == r.StrengthRaw && item.Key.CompareTo(r.Attribution) >= 0))
                    return; // S2_MUTATION:strongest_winner
                RequireSaturatedStatusCapture(item, d, replace ? sim.Clock.Tick : r.StartTick);
                long expiry = d.DurationKind == GameplayStatusDuration.Infinite ? 0 : StatusFuture(d.DurationTicks);
                if (replace)
                {
                    if (r.HasExpiryTask)
                        sim.Scheduler.CancelStable(r.ExpiryTask);
                    if (r.HasPulseTask)
                        sim.Scheduler.CancelStable(r.PulseTask);
                    r.HasExpiryTask = r.HasPulseTask = false;
                    r.ExpiryTask = r.PulseTask = default;
                    r.ExpiryTick = expiry;
                    r.StrengthRaw = strength;
                    r.StartTick = sim.Clock.Tick;
                    r.NextPulseTick = d.PeriodTicks == 0 ? 0 : StatusFuture(d.FirstPulseDelayTicks == 0 ? 1 : d.FirstPulseDelayTicks);
                    r.PulseOrdinal = 0;
                }
                else
                {
                    if (d.StackPolicy == GameplayStackPolicy.Stack && r.Stacks < d.MaxStacks)
                        r.Stacks++; // S2_MUTATION:stack_cap
                    else if (d.StackPolicy == GameplayStackPolicy.Stack)
                        Count(ref numericSaturations);
                    if (r.ExpiryTick != 0 && expiry > r.ExpiryTick)
                    {
                        if (r.HasExpiryTask)
                            sim.Scheduler.CancelStable(r.ExpiryTask);
                        r.HasExpiryTask = false;
                        r.ExpiryTask = default;
                        r.ExpiryTick = expiry;
                    } // S2_MUTATION:refresh_expiry
                }
                if (replace || r.AttributionTick != sim.Clock.Tick || item.Key.CompareTo(r.Attribution) < 0)
                {
                    r.Attribution = item.Key;
                    r.SourceSnapshot = item.Snapshot;
                    r.AttributionTick = sim.Clock.Tick;
                } // S2_MUTATION:refresh_attribution
                if (item.Kind == 4)
                    r.AbsorbRemainingRaw = GameplayMath.SaturatingAdd(replace ? 0 : r.AbsorbRemainingRaw, item.Amount, ref numericSaturations);
                ScheduleStatus(ref r);
                WriteStatus(at, r);
                StatusNotice(r, 21, 2, item.Key);
                return;
            }
            // A preceding dispel can turn a validated refresh into a new instance.
            RequireSaturatedStatusCapture(item, d, sim.Clock.Tick);
            if (statusCount == statuses.Length)
                throw new SimBoundaryException("M0C_STATUS_UNRESERVED_CAPACITY");
            if (effectNext == ulong.MaxValue)
                throw new SimBoundaryException("M0C_STATUS_TOKEN_EXHAUSTED");
            bool grouped = d.StackScope == GameplayStackScope.TargetAndSource;
            GameplayStatusRow created = new GameplayStatusRow
            {
                Target = item.Target,
                Key = new GameplayStatusInstanceKey(item.Target.Id, d.Key, grouped, grouped ? item.Snapshot.Source : default, effectNext),
                Attribution = item.Key,
                SourceSnapshot = item.Snapshot,
                AttributionTick = sim.Clock.Tick,
                Stacks = 1,
                StrengthRaw = strength,
                StartTick = sim.Clock.Tick,
                ExpiryTick = d.DurationKind == GameplayStatusDuration.Infinite ? 0 : StatusFuture(d.DurationTicks),
                NextPulseTick = d.PeriodTicks == 0 ? 0 : StatusFuture(d.FirstPulseDelayTicks == 0 ? 1 : d.FirstPulseDelayTicks),
                AbsorbRemainingRaw = item.Kind == 4 ? item.Amount : 0
            };
            effectNext++;
            RefreshStatusMeta();
            ScheduleStatus(ref created);
            int insert = 0;
            while (insert < statusCount && statuses[insert].Key.CompareTo(created.Key) < 0)
                insert++;
            for (int i = statusCount; i > insert; i--)
                statuses[i] = statuses[i - 1];
            statuses[insert] = created;
            statusCount++;
            statusSum = unchecked(statusSum + RowLeaf(7, insert));
            StatusNotice(created, 21, 1, item.Key);
        }
        private long ScaleStackAmount(long raw, int stacks, long limit) => ScaleStatusModifier(raw, stacks, limit);
    }
}
