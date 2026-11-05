namespace WarCore.Sim
{
    public sealed class PlacedDestructionState
    {
        public const int CursorBits = 48;
        public const ulong CursorMask = (1UL << CursorBits)-1;
        private readonly byte[] bits;
        private readonly int[] destroyed;
        private readonly ushort[] generations;
        private readonly int[] nonzero;
        private int destroyedCount;
        private int generationCount;
        private ulong destroyedSum;
        private ulong generationSum;
        public PlacedCatalog Catalog { get; }
        public int DestroyedCount => destroyedCount;
        public int GenerationCount => generationCount;
        public ulong DestroyedDigest => destroyedSum;
        public ulong GenerationDigest => generationSum;
        // Per-call diagnostics, excluded from authority. A visit is an encoded sparse entry.
        internal int LastHashWords { get; private set; }
        internal int LastSnapshotVisits { get; private set; }
        internal PlacedDestructionState(PlacedCatalog catalog, InitialPlacedState initial)
        {
            Catalog = catalog;
            bits = new byte[(catalog.Count+7)/8];
            destroyed = new int[catalog.Count];
            generations = new ushort[catalog.RecoverableCount];
            nonzero = new int[catalog.RecoverableCount];
            if (initial == null)
                return;
            if (initial.CatalogDigest != catalog.Digest)
                throw new SimBoundaryException("M0C_PLACED_INITIAL: Catalog digest mismatch.");
            int previous = -1;
            foreach (int index in initial.Destroyed)
            {
                if (index <= previous || index >= catalog.Count)
                    throw new SimBoundaryException("M0C_PLACED_INITIAL: Invalid sparse index order or range.");
                MarkDestroyed(index);
                previous = index;
            }
            previous = -1;
            foreach (PlacedGeneration item in initial.Generations)
            {
                if (item.Index <= previous || item.Index >= catalog.Count || item.Generation == 0
                    || catalog.Entry(item.Index).Policy != 1)
                    throw new SimBoundaryException("M0C_PLACED_INITIAL: Invalid generation table.");
                SetGeneration(item.Index,item.Generation);
                previous = item.Index;
            }
        }
        public bool IsDestroyed(StableEntityKey key) => IsDestroyedIndex(Catalog.Index(key));
        public ushort Generation(StableEntityKey key) => GenerationAt(Catalog.Index(key));
        internal bool IsDestroyedIndex(int index) => (bits[index >> 3] & (1 << (index & 7))) != 0;
        internal ushort GenerationAt(int index)
        {
            int slot = Catalog.GenerationSlot(index);
            return slot < 0 ? (ushort)0 : generations[slot];
        }
        private static ulong Leaf(int index, ulong value, ulong domain)
        {
            ulong hash = 14695981039346656037UL;
            StableEntityKey.Word(ref hash,domain);
            StableEntityKey.Word(ref hash,(ulong)index);
            StableEntityKey.Word(ref hash,value);
            return StableEntityKey.Mix(hash);
        }
        private static void Insert(int[] values, ref int count, int index)
        {
            int at = count;
            while (at > 0 && values[at-1] > index)
            {
                values[at] = values[at-1];
                at--;
            }
            values[at] = index;
            count++;
        }
        internal void MarkDestroyed(int index)
        {
            if (IsDestroyedIndex(index))
                return;
            bits[index >> 3] |= (byte)(1 << (index & 7));
            Insert(destroyed,ref destroyedCount,index);
            destroyedSum = unchecked(destroyedSum + Leaf(index,1,1));
        }
        private void SetGeneration(int index, ushort next)
        {
            int slot = Catalog.GenerationSlot(index);
            ushort old = generations[slot];
            if (old != 0)
                generationSum = unchecked(generationSum - Leaf(index,old,2));
            else
                Insert(nonzero,ref generationCount,slot);
            generations[slot] = next;
            generationSum = unchecked(generationSum + Leaf(index,next,2));
        }
        internal void Restore(int index)
        {
            if (Catalog.Entry(index).Policy != 1)
                throw new SimBoundaryException("M0C_RESTORE_POLICY: Permanent placement.");
            if (!IsDestroyedIndex(index))
                throw new SimBoundaryException("M0C_RESTORE_NOT_DESTROYED: Placement is not destroyed.");
            ushort old = GenerationAt(index);
            if (old == ushort.MaxValue)
                throw new SimBoundaryException("M0C_RESTORE_GEN_EXHAUSTED: Placement generation exhausted.");
            SetGeneration(index,(ushort)(old+1));
            bits[index >> 3] &= (byte)(255 ^ (1 << (index & 7)));
            int at = 0;
            while (destroyed[at] != index)
                at++;
            destroyedCount--;
            for (; at < destroyedCount; at++)
                destroyed[at] = destroyed[at+1];
            destroyedSum = unchecked(destroyedSum - Leaf(index,1,1));
        }
        internal ulong CursorStart(int index) => (ulong)GenerationAt(index) << CursorBits;
        internal bool CursorValid(int index, ulong value) => (value >> CursorBits) == GenerationAt(index);
        internal void RequireCursor(int index, ulong value, string error)
        {
            if (!CursorValid(index,value) || (value & CursorMask) == CursorMask)
                throw new SimBoundaryException(error);
        }
        internal void WriteHash(SimStateWriter writer)
        {
            LastHashWords = 8;
            writer.UInt64(1);
            writer.UInt64(Catalog.WorldId);
            writer.UInt64(Catalog.Digest);
            writer.Int32(Catalog.Count);
            writer.Int32(destroyedCount);
            writer.UInt64(destroyedSum);
            writer.Int32(generationCount);
            writer.UInt64(generationSum);
        }
        internal void Save(SimStateWriter writer)
        {
            LastSnapshotVisits = 0;
            writer.UInt64(1);
            writer.UInt64(Catalog.WorldId);
            writer.UInt64(Catalog.Digest);
            writer.Int32(Catalog.Count);
            writer.UInt64(destroyedSum);
            writer.UInt64(generationSum);
            writer.Int32(destroyedCount);
            for (int i = 0; i < destroyedCount; i++)
            {
                writer.Int32(destroyed[i]);
                LastSnapshotVisits++;
            }
            writer.Int32(generationCount);
            for (int i = 0; i < generationCount; i++)
            {
                writer.Int32(Catalog.GenerationIndex(nonzero[i]));
                writer.UInt64(generations[nonzero[i]]);
                LastSnapshotVisits++;
            }
        }
        private void Clear()
        {
            for (int i = 0; i < destroyedCount; i++)
                bits[destroyed[i] >> 3] = 0;
            for (int i = 0; i < generationCount; i++)
                generations[nonzero[i]] = 0;
            destroyedCount = 0;
            generationCount = 0;
            destroyedSum = 0;
            generationSum = 0;
        }
        internal void Load(SimStateReader reader)
        {
            SimStateReader.Require(reader.UInt64() == 1 && reader.UInt64() == Catalog.WorldId
                && reader.UInt64() == Catalog.Digest && reader.Int32() == Catalog.Count,"M0C_PLACED_CATALOG: Snapshot catalog mismatch.");
            ulong savedDestroyed = reader.UInt64(), savedGenerations = reader.UInt64();
            Clear();
            int count = reader.Count(Catalog.Count);
            int previous = -1;
            for (int i = 0; i < count; i++)
            {
                int index = reader.Int32();
                SimStateReader.Require(index > previous && index < Catalog.Count,"M0C_PLACED_INDEX: Invalid sparse index order or range.");
                MarkDestroyed(index);
                previous = index;
            }
            count = reader.Count(generations.Length);
            previous = -1;
            for (int i = 0; i < count; i++)
            {
                int index = reader.Int32();
                ulong gen = reader.UInt64();
                SimStateReader.Require(index > previous && index < Catalog.Count && gen > 0 && gen <= ushort.MaxValue,
                    "M0C_PLACED_GEN: Invalid generation index or value.");
                SimStateReader.Require(Catalog.Entry(index).Policy == 1,"M0C_PLACED_GEN: Permanent placement has a generation.");
                SetGeneration(index,(ushort)gen);
                previous = index;
            }
            SimStateReader.Require(destroyedSum == savedDestroyed && generationSum == savedGenerations,"M0C_PLACED_DIGEST: Sparse state digest mismatch.");
        }
    }
}
