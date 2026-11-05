using System;

namespace WarCore.Sim
{
    // Readiness is transport state. The declared tick/chunk timeline is simulation authority.
    // Missing bytes block before the clock changes; elapsed wall time is never an input.
    public sealed class StableActivationTimeline
    {
        private struct Activation
        {
            internal long Tick;
            internal ulong Chunk;
            internal bool Ready;
        }
        private readonly Activation[] entries;
        private readonly ulong manifest;
        private readonly EntityStore entities;
        private int count;
        private int cursor;
        private long advancedThrough;
        internal StableActivationTimeline(int capacity, ulong manifest, EntityStore entities)
        {
            entries = new Activation[capacity];
            this.manifest = manifest;
            this.entities = entities;
        }
        public void Plan(long tick, ulong chunk)
        {
            if (tick <= advancedThrough)
                throw new SimBoundaryException("Chunk activation must target a future boundary.");
            if (count == entries.Length)
                throw new SimBoundaryException("Chunk activation capacity exhausted.");
            for (int i = 0; i < count; i++)
                if (entries[i].Tick == tick && entries[i].Chunk == chunk)
                    throw new SimBoundaryException("Duplicate chunk activation.");
            int at = count;
            while (at > cursor && (entries[at-1].Tick > tick || (entries[at-1].Tick == tick && entries[at-1].Chunk > chunk)))
            {
                entries[at] = entries[at-1];
                at--;
            }
            entries[at] = new Activation { Tick=tick, Chunk=chunk };
            count++;
        }
        public void Deliver(ulong chunk, ulong contentManifest)
        {
            if (contentManifest != manifest)
                throw new SimBoundaryException("Delivered chunk content manifest mismatch.");
            bool found = false;
            for (int i = cursor; i < count; i++)
                if (entries[i].Chunk == chunk)
                {
                    entries[i].Ready = true;
                    found = true;
                }
            if (!found)
                throw new SimBoundaryException("Delivered chunk has no activation plan.");
        }
        public bool ReadyFor(long tick)
        {
            if (tick <= 0)
                throw new SimBoundaryException("Activation boundary must be positive.");
            for (int i = cursor; i < count && entries[i].Tick <= tick; i++)
                if (entries[i].Tick != tick || !entries[i].Ready)
                    return false;
            return true;
        }
        internal void Advance(long tick)
        {
            if (!ReadyFor(tick))
                throw new SimBoundaryException("The activation boundary is missing declared chunk data.");
            while (cursor < count && entries[cursor].Tick == tick)
            {
                entities.ActivateColdChunk(entries[cursor].Chunk);
                cursor++;
            }
            advancedThrough = tick;
        }
        internal void Save(SimStateWriter writer)
        {
            writer.Int32(count);
            writer.Int32(cursor);
            for (int i = 0; i < count; i++)
            {
                writer.Int64(entries[i].Tick);
                writer.UInt64(entries[i].Chunk);
            }
        }
        internal void Load(SimStateReader reader, long tick)
        {
            advancedThrough = tick;
            count = reader.Count(entries.Length);
            cursor = reader.Count(count);
            for (int i = 0; i < count; i++)
            {
                entries[i] = new Activation { Tick=reader.Int64(), Chunk=reader.UInt64() };
                SimStateReader.Require(entries[i].Tick > 0 && ((i < cursor) == (entries[i].Tick <= tick))
                    && (i == 0 || entries[i-1].Tick < entries[i].Tick || (entries[i-1].Tick == entries[i].Tick
                        && entries[i-1].Chunk < entries[i].Chunk)),"Invalid chunk activation timeline.");
            }
        }
    }

    public sealed partial class Sim
    {
        public StableActivationTimeline Activations { get; private set; }
        public bool TryStepStable(StableInputFrame input)
        {
            Entities.RequireStableWorld();
            if (Clock.Tick == long.MaxValue)
                throw new SimBoundaryException("Simulation clock exhausted.");
            if (!Activations.ReadyFor(Clock.Tick+1))
                return false;
            StepStable(input);
            return true;
        }
        private void RequireActivationBoundary()
        {
            if (Clock.Tick == long.MaxValue)
                throw new SimBoundaryException("Simulation clock exhausted.");
            if (Activations != null && !Activations.ReadyFor(Clock.Tick+1))
                throw new SimBoundaryException("The activation boundary is missing declared chunk data.");
        }
    }
}
