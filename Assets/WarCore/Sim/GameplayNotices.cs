using System;
namespace WarCore.Sim
{
    internal readonly struct GameplayNotice : IComparable<GameplayNotice>
    {
        internal readonly int Phase, Kind;
        internal readonly StableId Target;
        internal readonly GameplayApplyKey Key;
        internal readonly StableSimEvent Event;
        internal GameplayNotice(int phase, StableId target, GameplayApplyKey key, int kind, StableSimEvent item)
        {
            Phase = phase;
            Target = target;
            Key = key;
            Kind = kind;
            Event = item;
        }
        public int CompareTo(GameplayNotice other)
        {
            int c = Phase.CompareTo(other.Phase);
            if (c != 0)
                return c;
            c = Target.CompareTo(other.Target);
            if (c != 0)
                return c;
            c = Key.CompareTo(other.Key);
            return c != 0 ? c : Kind.CompareTo(other.Kind);
        }
    }
    internal sealed partial class GameplayState
    {
        private GameplayNotice[] orderedNotices;
        private int s4NoticeCount;
        private long noticesDropped;
        private void InitNotices()
        {
            // At most one addition per command plus one deletion per initial/new
            // status; one final fact/resource, life/owner and failure/input or cast.
            orderedNotices = new GameplayNotice[schema.NoticeStagingCapacity];
        }
        private void QueueNotice(int phase, StableId target, GameplayApplyKey key, int kind, StableSimEvent item)
        {
            if (schema.LifeRuleCount == 0)
                return;
            if (s4NoticeCount == orderedNotices.Length)
                throw new SimBoundaryException("M0C_NOTICE_STAGING_BUDGET");
            orderedNotices[s4NoticeCount++] = new GameplayNotice(phase, target, key, kind, item);
        }
        private void QueueFact(int phase, StableId target, GameplayApplyKey key, int kind, StableSimEvent item)
        {
            if (schema.LifeRuleCount == 0)
                notices[noticeCount++] = item;
            else
                QueueNotice(phase, target, key, kind, item);
        }
        private void StatusNotice(GameplayStatusRow row, int phase, int outcome)
        {
            StatusNotice(row, phase, outcome, new GameplayApplyKey(row.Attribution.Source, row.Attribution.CastToken, 1, row.Target.Id, row.Key.Token, row.Attribution.ActionOrdinal, row.PulseOrdinal));
        }
        private void StatusNotice(GameplayStatusRow row, int phase, int outcome, GameplayApplyKey key)
        {
            QueueNotice(phase, row.Target.Id, key, 4,
                new StableSimEvent(GameplayEvents.StatusChanged, new EntityLink(row.Attribution.Source), new EntityLink(row.Target.Id),
                    magnitude: SimFix.FromRaw(row.Stacks), radius: SimFix.FromRaw(row.StrengthRaw), tag: outcome));
        }
        private void AbilityFailureNotice(GameplayStamp actor, uint ordinal, GameplayAbilityFailure failure, ulong token = 0)
        {
            if (failure == GameplayAbilityFailure.None)
                return;
            QueueNotice(10, actor.Id, new GameplayApplyKey(actor.Id, token, 0, actor.Id, token, ordinal), 5,
                new StableSimEvent(GameplayEvents.AbilityFailure, target: new EntityLink(actor.Id), tag: (int)failure));
        }
        private void FlushNotices()
        {
            if (schema.LifeRuleCount == 0)
            {
                for (int i = 0; i < noticeCount; i++)
                    sim.Context.Emit(notices[i]);
                noticeCount = 0;
                return;
            }
            ValueOrder.Sort(orderedNotices.AsSpan(0, s4NoticeCount));
            for (int i = 0; i < s4NoticeCount; i++)
                if (!sim.PublishGameplayNotice(orderedNotices[i].Event))
                    Count(ref noticesDropped);
            s4NoticeCount = 0;
            RefreshDiagnostics();
        }
    }
    public sealed partial class Sim
    {
        internal bool PublishGameplayNotice(StableSimEvent item)
        {
            Entities.ValidateLink(item.Source, false);
            Entities.ValidateLink(item.Target, false);
            return events.PublishStable(item);
        }
    }
}
