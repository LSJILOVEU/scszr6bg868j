using System;

namespace WarCore.Sim
{
    public enum FactionRelation
    {
        Neutral,
        Friendly,
        Hostile
    }

    public sealed class FactionRelations : IStateHash
    {
        private FactionRelation[] values;
        public int Count { get; private set; }

        // Row is the observer, column is the target; copying keeps content mutation out of Sim.
        public FactionRelations(int count, FactionRelation[] rowMajorValues)
        {
            if (count <= 0)
                throw new ArgumentOutOfRangeException(nameof(count));
            if (count > int.MaxValue / count)
                throw new SimBoundaryException("Faction relation matrix exceeds the supported entry count.");
            if (rowMajorValues == null || rowMajorValues.Length != count * count)
                throw new ArgumentException("Faction data must contain exactly N * N relations.", nameof(rowMajorValues));
            foreach (FactionRelation relation in rowMajorValues)
                if (relation < FactionRelation.Neutral || relation > FactionRelation.Hostile)
                    throw new ArgumentOutOfRangeException(nameof(rowMajorValues));
            Count = count;
            values = (FactionRelation[])rowMajorValues.Clone();
        }

        public FactionRelation Get(int observer, int target)
        {
            if (observer < 0 || observer >= Count || target < 0 || target >= Count)
                throw new ArgumentOutOfRangeException(nameof(observer), "Faction index is outside the configured table.");
            return values[observer * Count + target];
        }

        public bool IsFriendly(int observer, int target) => Get(observer, target) == FactionRelation.Friendly;
        public bool IsHostile(int observer, int target) => Get(observer, target) == FactionRelation.Hostile;
        public bool IsNeutral(int observer, int target) => Get(observer, target) == FactionRelation.Neutral;
        public void WriteState(StateHash hash)
        {
            hash.Add(Count);
            foreach (FactionRelation value in values)
                hash.Add((int)value);
        }

        internal void SaveRelations(SimStateWriter writer)
        {
            writer.Int32(Count);
            foreach (FactionRelation value in values)
                writer.Int32((int)value);
        }

        internal FactionRelations Copy() => new FactionRelations(Count, values);

        internal void Clear()
        {
            // An absent optional table has no hash payload, but existing observers keep their identity.
            Count = 0;
            values = Array.Empty<FactionRelation>();
        }

        internal static FactionRelations LoadRelations(SimStateReader reader, FactionRelations existing)
        {
            int count = reader.Int32();
            SimStateReader.Require(count > 0 && count <= int.MaxValue / count, "Invalid faction matrix size.");
            int size = count * count;
            SimStateReader.Require(size <= reader.Remaining / 4, "Truncated faction matrix.");
            var values = new FactionRelation[size];
            for (int i = 0; i < size; i++)
                values[i] = (FactionRelation)reader.Int32();
            var restored = new FactionRelations(count, values);
            if (existing == null)
                return restored;
            existing.values = restored.values;
            existing.Count = restored.Count;
            return existing;
        }
    }
}
