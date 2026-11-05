using System;

namespace WarCore.Sim
{
    public readonly struct StableProducedEvent : IComparable<StableProducedEvent>
    {
        public int CompareTo(StableProducedEvent other) => Key.CompareTo(other.Key);
        public StableRequestKey Key { get; }
        public StableSimEvent Event { get; }
        public StableProducedEvent(StableRequestKey key, StableSimEvent item)
        {
            Key = key;
            Event = item;
        }
    }

    public sealed partial class SimContext
    {
        // One caller-owned batch represents one system/phase boundary. Cross-system order remains registration order.
        // Sort before publication so both sequence assignment and overflow retention use the same authority.
        public void EmitStableBatch(Span<StableProducedEvent> batch)
        {
            RequireActive();
            Entities.RequireStableWorld();
            for (int i = 0; i < batch.Length; i++)
            {
                Entities.ValidateLink(batch[i].Event.Source,false);
                Entities.ValidateLink(batch[i].Event.Target,false);
            }
            ValueOrder.Sort(batch);
            for (int i = 1; i < batch.Length; i++)
                if (batch[i-1].Key.Equals(batch[i].Key))
                    throw new SimBoundaryException("Duplicate stable event production key.");
            for (int i = 0; i < batch.Length; i++)
                events.PublishStable(batch[i].Event);
        }
    }
}
