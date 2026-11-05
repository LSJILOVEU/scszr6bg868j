using System;
using System.Collections.Generic;

namespace WarCore.Sim
{
    public readonly struct StableContentKey
    {
        public string Name { get; }
        public ulong Key { get; }
        public StableContentKey(string name, ulong key)
        {
            if (string.IsNullOrEmpty(name))
                throw new SimBoundaryException("Content keys require a stable name.");
            // Replacement fallback would let different malformed names share the same manifest bytes.
            for (int i = 0; i < name.Length; i++)
            {
                if (char.IsHighSurrogate(name[i]))
                {
                    if (i+1 >= name.Length || !char.IsLowSurrogate(name[i+1]))
                        throw new SimBoundaryException("Content names must be valid Unicode.");
                    i++;
                }
                else if (char.IsLowSurrogate(name[i]))
                    throw new SimBoundaryException("Content names must be valid Unicode.");
            }
            Name = name;
            Key = key;
        }
    }

    // Keys are allocated by the content build, never by runtime registration order.
    public sealed class StableContentManifest
    {
        private readonly StableContentKey[] entries;
        public ulong WorldId { get; }
        public ulong Digest { get; }
        private sealed class ByName : IComparer<StableContentKey>
        {
            public int Compare(StableContentKey a, StableContentKey b) => StringComparer.Ordinal.Compare(a.Name,b.Name);
        }
        public StableContentManifest(ulong worldId, params StableContentKey[] entries)
        {
            WorldId = worldId;
            if (entries == null)
                throw new ArgumentNullException(nameof(entries));
            this.entries = (StableContentKey[])entries.Clone();
            Array.Sort(this.entries,new ByName());
            var assigned = new Dictionary<ulong,string>();
            var hash = new StateHash();
            hash.Add("warcore.content-keys-v1");
            hash.Add(worldId);
            hash.Add(entries.Length);
            string previous = null;
            foreach (StableContentKey entry in this.entries)
            {
                if (entry.Name == null || entry.Name == previous || assigned.TryGetValue(entry.Key,out string conflict))
                    throw new SimBoundaryException("Duplicate content name or uint64 key in the frozen manifest.");
                assigned.Add(entry.Key,entry.Name);
                previous = entry.Name;
                hash.Add(entry.Name);
                hash.Add(entry.Key);
            }
            Digest = StableEntityKey.Mix(hash.Value);
        }
        public ulong Key(string name)
        {
            int low = 0;
            int high = entries.Length-1;
            while (low <= high)
            {
                int middle = low+(high-low)/2;
                int order = StringComparer.Ordinal.Compare(entries[middle].Name,name);
                if (order == 0)
                    return entries[middle].Key;
                if (order < 0)
                    low = middle+1;
                else
                    high = middle-1;
            }
            throw new SimBoundaryException("The requested content key is absent from the frozen manifest.");
        }
        internal void Write(SimStateWriter writer)
        {
            writer.UInt64(WorldId);
            writer.UInt64(Digest);
        }
    }
}
