using System;

namespace WarCore.Sim
{
    // Scratch belongs to the caller's query session, never to index membership or snapshot state.
    // A context is sequential: concurrent callers must use separate query sessions.
    public sealed class GridQueryContext
    {
        private readonly uint[] seen;
        internal readonly ulong[] SortKeys;
        private uint stamp;
        internal RayCellTraversal Traversal;
        public int Capacity => seen.Length;
        public int VisitedCells { get; internal set; }
        public int CandidateCount { get; internal set; }
        public int NarrowPhaseCount { get; internal set; }
        public int TruncatedHits { get; internal set; }
        internal SpatialQueryDiagnostics Diagnostics { get; set; }

        public GridQueryContext(int capacity)
        {
            if (capacity < 1)
                throw new ArgumentOutOfRangeException(nameof(capacity));
            seen = new uint[capacity];
            SortKeys = new ulong[capacity];
        }

        internal void Begin()
        {
            Diagnostics?.Reset();
            VisitedCells = 0;
            CandidateCount = 0;
            NarrowPhaseCount = 0;
            TruncatedHits = 0;
            stamp = unchecked(stamp + 1);
            if (stamp == 0)
            {
                Array.Clear(seen, 0, seen.Length);
                stamp = 1;
            }
        }

        internal bool Visit(int id)
        {
            if (seen[id] == stamp)
                return false;
            seen[id] = stamp;
            return true;
        }
    }
}
