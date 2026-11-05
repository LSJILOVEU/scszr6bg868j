using System;
using System.Collections.Generic;

namespace WarCore.Sim
{
    // A retained handle identifies a stream; it never retains the mutable generator from an old snapshot.
    public readonly struct RandomStreamId
    {
        internal readonly string Name;
        public RandomStreamId(string name)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("A stream name is required.", nameof(name));
            Name = name;
        }
    }

    public sealed class SimRandom : IStateHash
    {
        private sealed class RandomStreamState : IStateHash
        {
            private ulong state;
            internal RandomStreamState(ulong seed)
            {
                state = seed;
            }

            public ulong NextUInt64()
            {
                // SplitMix64 specifies integer wrapping so checked builds produce the same sequence.
                unchecked
                {
                    state += 0x9E3779B97F4A7C15UL;
                    ulong z = state;
                    z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                    z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                    return z ^ (z >> 31);
                }
            }

            public int NextInt(int exclusiveMax)
            {
                if (exclusiveMax <= 0)
                    throw new ArgumentOutOfRangeException(nameof(exclusiveMax));
                ulong bound = (ulong)exclusiveMax;
                ulong threshold = unchecked(0UL - bound) % bound;
                ulong sample;
                do
                {
                    sample = NextUInt64();
                }
                while (sample < threshold);
                return (int)(sample % bound);
            }

            public void WriteState(StateHash hash)
            {
                hash.Add(state);
            }

            internal ulong SavedState => state;
        }

        private ulong seed;
        private SortedList<string, RandomStreamState> streams = new SortedList<string, RandomStreamState>(StringComparer.Ordinal);
        private IList<string> streamNames;
        private IList<RandomStreamState> streamStates;
        public SimRandom(ulong seed)
        {
            this.seed = seed;
            CacheStreamViews();
        }

        private void CacheStreamViews()
        {
            // Mono creates these wrappers lazily. Construct them only on the
            // initialization/restore path; they remain live as streams are added.
            streamNames = streams.Keys;
            streamStates = streams.Values;
        }

        public RandomStreamId Stream(string name)
        {
            var id = new RandomStreamId(name);
            Resolve(id);
            return id;
        }

        internal bool HasStream(RandomStreamId id) => id.Name != null && streams.ContainsKey(id.Name);
        public ulong NextUInt64(RandomStreamId id) => Resolve(id).NextUInt64();
        public int NextInt(RandomStreamId id, int exclusiveMax) => Resolve(id).NextInt(exclusiveMax);

        internal int NextRegisteredInt(RandomStreamId id,int exclusiveMax)
        {
            if(id.Name==null || !streams.TryGetValue(id.Name,out RandomStreamState stream))
                throw new SimBoundaryException("M0C_RANDOM_STREAM: Required stream was not registered during initialization.");
            return stream.NextInt(exclusiveMax);
        }

        private RandomStreamState Resolve(RandomStreamId id)
        {
            string name = id.Name;
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("A stream name is required.", nameof(name));
            if (!streams.TryGetValue(name, out RandomStreamState stream))
            {
                var hash = new StateHash();
                hash.Add(seed);
                hash.Add(name);
                stream = new RandomStreamState(hash.Value);
                streams.Add(name, stream);
            }

            return stream;
        }

        public void WriteState(StateHash hash)
        {
            hash.Add(seed);
            hash.Add(streams.Count);
            // Mono boxes SortedList's interface enumerator. Index cached views instead.
            for (int i = 0; i < streams.Count; i++)
            {
                hash.Add(streamNames[i]);
                streamStates[i].WriteState(hash);
            }
        }

        internal void SaveRandom(SimStateWriter writer)
        {
            writer.UInt64(seed);
            writer.Int32(streams.Count);
            for (int i = 0; i < streams.Count; i++)
            {
                writer.String(streamNames[i]);
                writer.UInt64(streamStates[i].SavedState);
            }
        }

        internal void LoadRandom(SimStateReader reader)
        {
            seed = reader.UInt64();
            int count = reader.Count(reader.Remaining / 12);
            var restored = new SortedList<string, RandomStreamState>(StringComparer.Ordinal);
            string previous = null;
            for (int i = 0; i < count; i++)
            {
                string name = reader.String();
                SimStateReader.Require(!string.IsNullOrEmpty(name) && (previous == null || StringComparer.Ordinal.Compare(previous, name) < 0), "Invalid RNG stream manifest.");
                ulong state = reader.UInt64();
                restored.Add(name, new RandomStreamState(state));
                previous = name;
            }

            // Missing names are absent, so an id first used after restore starts at its name-derived seed.
            streams = restored;
            CacheStreamViews();
        }
    }
}
