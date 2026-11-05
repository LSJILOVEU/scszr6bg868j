using System;

namespace WarCore.Sim
{
    public sealed partial class SimEventBuffer
    {
        private struct EventLinks
        {
            internal EntityLink Source;
            internal EntityLink Target;
        }
        private EventLinks[][] stableLinks;
        private bool publishingStable;
        internal void EnableStableEvents()
        {
            stableLinks = new EventLinks[slots.Length][];
            for (int i = 0; i < slots.Length; i++)
                stableLinks[i] = new EventLinks[hardLimit+1];
        }
        public bool PublishStable(StableSimEvent item)
        {
            if (stableLinks == null)
                throw new SimBoundaryException("Stable events require StableWorld.");
            int index = (int)(currentTick % slots.Length);
            int at = counts[index];
            bool accepted;
            publishingStable = true;
            try
            {
                accepted = Publish(item.Payload);
            }
            finally
            {
                publishingStable = false;
            }
            if (accepted)
                stableLinks[index][at] = new EventLinks { Source=item.Source, Target=item.Target };
            return accepted;
        }
        public StableSimEvent ReadStable(long tick, int index)
        {
            ReadOnlySpan<SimEvent> values = Read(tick);
            if (stableLinks == null || index < 0 || index >= values.Length)
                throw new SimBoundaryException("Stable event index is outside its retained window.");
            EventLinks links = stableLinks[(int)(tick % slots.Length)][index];
            return new StableSimEvent(values[index],links.Source,links.Target);
        }
        internal void SaveStableEvents(SimStateWriter writer)
        {
            writer.Int64(currentTick);
            writer.Int64(NextSequence);
            writer.Int64(EventsDroppedThisTick);
            writer.Int64(EventsDroppedTotal);
            int retained = (int)SimLimits.Min(currentTick,slots.Length);
            writer.Int32(retained);
            long first = currentTick-retained+1;
            for (int offset = 0; offset < retained; offset++)
            {
                long tick = first+offset;
                int count = Read(tick).Length;
                writer.Int64(tick);
                writer.Int32(count);
                for (int i = 0; i < count; i++)
                    writer.StableEvent(ReadStable(tick,i));
            }
        }
        internal void LoadStableEvents(SimStateReader reader, long tick)
        {
            long savedTick = reader.Int64();
            long next = reader.Int64();
            long dropped = reader.Int64();
            long total = reader.Int64();
            int retained = reader.Count(slots.Length);
            SimStateReader.Require(savedTick == tick && next > 0 && dropped >= 0 && total >= dropped
                && retained == SimLimits.Min(tick,slots.Length),"Invalid stable event window.");
            Array.Clear(counts,0,counts.Length);
            Array.Clear(ticks,0,ticks.Length);
            long previous = 0;
            for (int offset = 0; offset < retained; offset++)
            {
                long expectedTick = tick-retained+1+offset;
                SimStateReader.Require(reader.Int64() == expectedTick,"Invalid stable event tick.");
                int index = (int)(expectedTick % slots.Length);
                int count = reader.Count(hardLimit+1);
                ticks[index] = expectedTick;
                counts[index] = count;
                for (int i = 0; i < count; i++)
                {
                    StableSimEvent item = reader.StableEvent();
                    SimStateReader.Require(item.Tick == expectedTick && item.Sequence > previous && item.Sequence < next,"Invalid stable event sequence.");
                    slots[index][i] = item.Payload;
                    stableLinks[index][i] = new EventLinks { Source=item.Source, Target=item.Target };
                    previous = item.Sequence;
                }
            }
            currentTick = tick;
            NextSequence = next;
            EventsDroppedThisTick = dropped;
            EventsDroppedTotal = total;
            active = false;
        }
    }
}
