using System;

namespace WarCore.Sim
{
    public enum StableRequestKind
    {
        Position = 1, AttachTarget, AttachHost, Detach, ColliderStatic, FollowHost,
        SeatCarrier, SeatOccupant, OccupantSeat, InputTarget
    }

    public sealed class RequestDiagnostics
    {
        public const int EventType = 2147483646;
        private struct Notice : IComparable<Notice>
        {
            internal StableRequestKind Kind;
            internal EntityLink Subject;
            internal EntityLink Related;
            internal long Amount;
            public int CompareTo(Notice other)
            {
                int c = ((int)Kind).CompareTo((int)other.Kind);
                if (c != 0)
                    return c;
                c = Subject.Id.CompareTo(other.Subject.Id);
                return c != 0 ? c : Related.Id.CompareTo(other.Related.Id);
            }
        }
        private readonly EntityStore entities;
        private readonly Notice[] pending;
        private readonly Notice[] cleanups;
        private readonly Notice[] cleanupOrder;
        private readonly long[] counts = new long[11];
        private int count;
        private bool hasCleanups;
        private long suppressedPending;
        private long total;
        public long Total => SimChecked.Add(total,CleanupCount(0));
        public long SuppressedTotal { get; private set; }
        public long Count(StableRequestKind kind)
        {
            if (kind < StableRequestKind.Position || kind > StableRequestKind.InputTarget)
                throw new SimBoundaryException("Unknown request diagnostic kind.");
            return SimChecked.Add(counts[(int)kind],CleanupCount(kind));
        }
        internal RequestDiagnostics(int capacity, EntityStore entities)
        {
            pending = new Notice[capacity];
            // Strong-reference notices belong to their owner, so dying owners cannot consume another queue's capacity.
            cleanups = new Notice[entities.Capacity*4];
            cleanupOrder = new Notice[cleanups.Length];
            this.entities = entities;
        }
        private static bool CleanupKind(StableRequestKind kind) => kind >= StableRequestKind.FollowHost && kind <= StableRequestKind.OccupantSeat;
        private bool LiveCleanup(Notice notice) => notice.Amount != 0 && entities.HasLogicalIdentity(notice.Subject.Id);
        private long CleanupCount(StableRequestKind kind)
        {
            long sum = 0;
            if (hasCleanups)
                for (int i = 0; i < cleanups.Length; i++)
                    if ((kind == 0 || cleanups[i].Kind == kind) && LiveCleanup(cleanups[i]))
                        sum = SimChecked.Add(sum,cleanups[i].Amount);
            return sum;
        }
        internal void RecordCleanup(StableRequestKind kind, EntityLink subject, EntityLink related)
        {
            EntityId owner = entities.ResolveLogical(subject);
            int at = owner.Id*4+(int)kind-(int)StableRequestKind.FollowHost;
            SimChecked.Add(Total,1);
            Notice previous = cleanups[at];
            long amount = previous.Subject.Equals(subject) ? SimChecked.Add(previous.Amount,1) : 1;
            if (previous.Subject.Equals(subject) && previous.Related.Id.CompareTo(related.Id) < 0)
                related = previous.Related;
            cleanups[at] = new Notice { Kind=kind, Subject=subject, Related=related, Amount=amount };
            hasCleanups = true;
        }
        internal void Record(StableRequestKind kind, EntityLink subject, EntityLink related = default)
        {
            total = SimChecked.Add(total,1);
            counts[(int)kind] = SimChecked.Add(counts[(int)kind],1);
            if (count == pending.Length)
            {
                SuppressedTotal = SimChecked.Add(SuppressedTotal,1);
                suppressedPending = SimChecked.Add(suppressedPending,1);
                return;
            }
            pending[count++] = new Notice { Kind=kind, Subject=subject, Related=related, Amount=1 };
        }
        private int CollectCleanups()
        {
            int length = 0;
            if (hasCleanups)
                for (int i = 0; i < cleanups.Length; i++)
                    if (LiveCleanup(cleanups[i]))
                        cleanupOrder[length++] = cleanups[i];
            ValueOrder.Sort(cleanupOrder.AsSpan(0,length));
            return length;
        }
        private Notice NextNotice(ref int request, ref int cleanup, int length)
        {
            if (request == count || (cleanup < length && cleanupOrder[cleanup].CompareTo(pending[request]) < 0))
                return cleanupOrder[cleanup++];
            return pending[request++];
        }
        internal void Flush(SimEventBuffer events)
        {
            ValueOrder.Sort(pending.AsSpan(0,count));
            int length = CollectCleanups();
            int request = 0;
            int cleanup = 0;
            while (request < count || cleanup < length)
            {
                int before = cleanup;
                Notice item = NextNotice(ref request,ref cleanup,length);
                if (cleanup != before)
                {
                    total = SimChecked.Add(total,item.Amount);
                    counts[(int)item.Kind] = SimChecked.Add(counts[(int)item.Kind],item.Amount);
                }
                events.PublishStable(new StableSimEvent(new EventTypeId(EventType),item.Subject,item.Related,
                    magnitude:SimFix.FromRaw(item.Amount),tag:(int)item.Kind));
            }
            if (suppressedPending != 0)
                events.PublishStable(new StableSimEvent(new EventTypeId(EventType),magnitude:SimFix.FromRaw(suppressedPending),tag:0));
            count = 0;
            suppressedPending = 0;
            if (hasCleanups)
                Array.Clear(cleanups,0,cleanups.Length);
            hasCleanups = false;
        }
        internal void Save(SimStateWriter writer)
        {
            writer.Int64(Total);
            writer.Int64(SuppressedTotal);
            writer.Int64(suppressedPending);
            for (int i = 1; i < counts.Length; i++)
                writer.Int64(Count((StableRequestKind)i));
            ValueOrder.Sort(pending.AsSpan(0,count));
            int length = CollectCleanups();
            writer.Int32(count+length);
            int request = 0;
            int cleanup = 0;
            while (request < count || cleanup < length)
            {
                Notice item = NextNotice(ref request,ref cleanup,length);
                writer.Int32((int)item.Kind);
                writer.Link(item.Subject);
                writer.Link(item.Related);
                writer.Int64(item.Amount);
            }
        }
        internal void Load(SimStateReader reader)
        {
            Reset();
            total = reader.Int64();
            SuppressedTotal = reader.Int64();
            suppressedPending = reader.Int64();
            SimStateReader.Require(total >= 0 && SuppressedTotal >= 0 && SuppressedTotal <= total
                && suppressedPending >= 0 && suppressedPending <= SuppressedTotal,"Invalid request diagnostic counts.");
            long sum = 0;
            for (int i = 1; i < counts.Length; i++)
            {
                counts[i] = reader.Int64();
                SimStateReader.Require(counts[i] >= 0 && counts[i] <= total-sum,"Invalid diagnostic kind count.");
                sum += counts[i];
            }
            SimStateReader.Require(sum == total,"Incomplete diagnostic kind counts.");
            int length = reader.Count(pending.Length+cleanups.Length);
            Notice previous = default;
            for (int i = 0; i < length; i++)
            {
                var item = new Notice { Kind=(StableRequestKind)reader.Int32(), Subject=reader.Link(), Related=reader.Link(), Amount=reader.Int64() };
                SimStateReader.Require(item.Kind >= StableRequestKind.Position && item.Kind <= StableRequestKind.InputTarget
                    && item.Subject.HasValue && item.Amount > 0 && (i == 0 || previous.CompareTo(item) <= 0),"Invalid request diagnostic record.");
                if (CleanupKind(item.Kind))
                {
                    EntityId owner = entities.ResolveLogical(item.Subject);
                    int at = owner.Id*4+(int)item.Kind-(int)StableRequestKind.FollowHost;
                    SimStateReader.Require(cleanups[at].Amount == 0 && item.Amount <= counts[(int)item.Kind],"Invalid owned cleanup diagnostic.");
                    cleanups[at] = item;
                    total -= item.Amount;
                    counts[(int)item.Kind] -= item.Amount;
                    hasCleanups = true;
                }
                else
                {
                    SimStateReader.Require(count < pending.Length && item.Amount == 1,"Request diagnostic capacity exceeded.");
                    pending[count++] = item;
                }
                previous = item;
            }
        }
        internal void Reset()
        {
            Array.Clear(counts,0,counts.Length);
            Array.Clear(cleanups,0,cleanups.Length);
            hasCleanups = false;
            total = 0;
            SuppressedTotal = 0;
            suppressedPending = 0;
            count = 0;
        }
    }

    public sealed partial class Sim : IEntityRetirementObserver
    {
        public RequestDiagnostics RequestDiagnostics { get; private set; }
        void IEntityRetirementObserver.ReferenceCleared(StableRequestKind kind, EntityLink owner, EntityLink retired)
            => RequestDiagnostics.RecordCleanup(kind,owner,retired);
    }
}
