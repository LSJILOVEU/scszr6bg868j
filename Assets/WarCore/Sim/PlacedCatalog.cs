using System;

namespace WarCore.Sim
{
    public readonly struct PlacedCatalogEntry : IComparable<PlacedCatalogEntry>
    {
        public ulong Chunk { get; }
        public ulong Placement { get; }
        public byte Policy { get; }
        public StableId Id { get; }
        public StableEntityKey Key => StableEntityKey.Placed(Chunk,Placement);
        public PlacedCatalogEntry(ulong chunk, ulong placement, byte policy = 0)
        {
            if (policy > 1)
                throw new SimBoundaryException("M0C_PLACED_POLICY: Expected 0 or 1.");
            Chunk = chunk;
            Placement = placement;
            Policy = policy;
            Id = StableEntityKey.Placed(chunk,placement).Id;
        }
        public int CompareTo(PlacedCatalogEntry other) => Id.CompareTo(other.Id);
    }

    public sealed class PlacedCatalog
    {
        public const int MaximumCount = 1 << 24;
        private readonly PlacedCatalogEntry[] entries;
        private readonly int[] recoverable;
        public int Count => entries.Length;
        public ulong WorldId { get; }
        public ulong ContentDigest { get; }
        public ulong Digest { get; }
        internal int RecoverableCount => recoverable.Length;
        public PlacedCatalog(StableContentManifest content, params PlacedCatalogEntry[] entries)
        {
            if (content == null || entries == null || entries.Length > MaximumCount)
                throw new SimBoundaryException("M0C_PLACED_CATALOG: Missing or oversized catalog.");
            WorldId = content.WorldId;
            ContentDigest = content.Digest;
            this.entries = (PlacedCatalogEntry[])entries.Clone();
            Array.Sort(this.entries);
            ulong digest = 14695981039346656037UL;
            StableEntityKey.Word(ref digest,1);
            StableEntityKey.Word(ref digest,WorldId);
            StableEntityKey.Word(ref digest,ContentDigest);
            StableEntityKey.Word(ref digest,(ulong)Count);
            int restored = 0;
            for (int i = 0; i < Count; i++)
            {
                PlacedCatalogEntry entry = this.entries[i];
                if (!entry.Id.Equals(entry.Key.Id) || entry.Policy > 1 || (i > 0 && this.entries[i-1].Id.Equals(entry.Id)))
                    throw new SimBoundaryException("M0C_PLACED_CATALOG: Invalid, duplicate or colliding key.");
                StableEntityKey.Word(ref digest,entry.Id.Value);
                StableEntityKey.Word(ref digest,entry.Chunk);
                StableEntityKey.Word(ref digest,entry.Placement);
                // Policy is one byte, unlike the stable integer codecs.
                digest = unchecked((digest ^ entry.Policy) * 1099511628211UL);
                if (entry.Policy == 1)
                    restored++;
            }
            Digest = StableEntityKey.Mix(digest);
            recoverable = new int[restored];
            int at = 0;
            for (int i = 0; i < Count; i++)
                if (this.entries[i].Policy == 1)
                    recoverable[at++] = i;
        }
        public PlacedCatalogEntry Entry(int index)
        {
            if (index < 0 || index >= Count)
                throw new SimBoundaryException("M0C_PLACED_INDEX: Index outside catalog.");
            return entries[index];
        }
        internal int Find(StableId id)
        {
            int low = 0, high = Count-1;
            while (low <= high)
            {
                int middle = low+(high-low)/2;
                int comparison = entries[middle].Id.CompareTo(id);
                if (comparison == 0)
                    return middle;
                if (comparison < 0)
                    low = middle+1;
                else
                    high = middle-1;
            }
            return -1;
        }
        public int Index(StableEntityKey key)
        {
            int index = key.Domain == 1 ? Find(key.Id) : -1;
            if (index < 0 || !entries[index].Key.Equals(key))
                throw new SimBoundaryException("M0C_PLACED_UNKNOWN: Full key absent from catalog.");
            return index;
        }
        internal int GenerationIndex(int slot) => recoverable[slot];
        internal int GenerationSlot(int index)
        {
            int low = 0, high = recoverable.Length-1;
            while (low <= high)
            {
                int middle = low+(high-low)/2;
                if (recoverable[middle] == index)
                    return middle;
                if (recoverable[middle] < index)
                    low = middle+1;
                else
                    high = middle-1;
            }
            return -1;
        }
    }

    public readonly struct PlacedGeneration
    {
        public int Index { get; }
        public ushort Generation { get; }
        public PlacedGeneration(int index, ushort generation) { Index = index; Generation = generation; }
    }
    public sealed class InitialPlacedState
    {
        internal readonly ulong CatalogDigest;
        internal readonly int[] Destroyed;
        internal readonly PlacedGeneration[] Generations;
        public InitialPlacedState(ulong catalogDigest, int[] destroyed, PlacedGeneration[] generations = null)
        {
            if (destroyed == null)
                throw new SimBoundaryException("M0C_PLACED_INITIAL: Missing sparse indices.");
            CatalogDigest = catalogDigest;
            Destroyed = (int[])destroyed.Clone();
            Generations = generations == null ? new PlacedGeneration[0] : (PlacedGeneration[])generations.Clone();
        }
    }
}
