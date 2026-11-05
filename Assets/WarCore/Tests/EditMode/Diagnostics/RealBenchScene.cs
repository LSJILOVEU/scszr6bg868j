using System;
using System.Diagnostics;
using WarCore.Sim;

namespace WarCore.Tests.Diagnostics
{
    internal struct QueryObservation
    {
        internal long Elapsed;
        internal AllocationSample Allocation;
        internal int Hits;
        internal int Candidates;
        internal int Precise;
        internal int Visited;
        internal long Walks;
        internal long Lookups;
        internal long Members;
        internal long Dedup;
        internal long Duplicates;
    }

    // Both the Editor diagnostics and G23 use RealBenchWorkload's unchanged inputs and oracle.
    internal sealed class RealBenchScene : RealBenchWorkload, ISimSystem
    {
        internal readonly ProfiledGridSpatialQuery Replay;
        internal readonly SpatialQueryDiagnostics Counters = new SpatialQueryDiagnostics();
        internal QueryObservation[] Observations;
        internal bool Observe;
        internal int ObservationOffset;
        private readonly AllocationMeter meter = new AllocationMeter();
        public int Order => 1;

        internal RealBenchScene(bool dense, bool peak) : base(dense, peak)
        {
            Replay = new ProfiledGridSpatialQuery(Sim.Entities, Sim.Entities.Resolve<Position>(),
                Sim.Entities.Resolve<Collider>(), SimFix.FromInt(3), EntityCount, EntityCount * 16 + 128, readLiveLayers: false);
            Replay.Rebuild();
            Replay.Diagnostics = new SpatialQueryDiagnostics();
            Sim.Register("real-bench-queries", this, SimSystemPhase.Queries);
        }

        public void Step(SimContext context)
        {
            LastAffected = 0;
            LastRays = 0;
            int cover = 0;
            for (int i = 0; i < Queries.Length; i++)
            {
                RealQuery query = Queries[i].Role == QueryRole.Cover ? Cover(Entities[cover++]) : Queries[i];
                AllocationSample before = Observe ? meter.Read() : default;
                long start = Observe ? Stopwatch.GetTimestamp() : 0;
                int found = query.Query.Run(Grid, Entities, Rays);
                long elapsed = Observe ? Stopwatch.GetTimestamp() - start : 0;
                AllocationSample allocation = Observe ? meter.Difference(before) : default;
                if (Observe)
                {
                    Observations[ObservationOffset++] = new QueryObservation
                    {
                        Elapsed = elapsed,
                        Allocation = allocation,
                        Hits = found,
                        Candidates = Grid.LastCandidateCount,
                        Precise = Grid.LastNarrowPhaseCount,
                        Visited = Grid.LastVisitedCells,
                        Walks = Counters.WalkCalls,
                        Lookups = Counters.DictionaryLookups,
                        Members = Counters.LeafMembers,
                        Dedup = Counters.DedupChecks,
                        Duplicates = Counters.DuplicateSkips
                    };
                }

                ulong signature = Signature(query.Query.Kind, found);
                if (found != ExpectedCounts[i] || signature != ExpectedSignatures[i])
                    throw new InvalidOperationException("Real benchmark output changed.");
                if (query.Role == QueryRole.Explosion)
                    LastAffected = found;
                if (query.Query.Kind == 0)
                    LastRays++;
                Calls++;
                TotalHits += found;
                Checksum = unchecked(Checksum * 1099511628211UL ^ signature);
            }

            if (LastAffected != Affected || LastRays != RayCount || cover != Affected)
                throw new InvalidOperationException("Peak work or affected targets disappeared.");
            context.Emit(new SimEvent(new EventTypeId(902), tag: Queries.Length));
        }

        public void WriteState(StateHash hash)
        {
            hash.Add(Calls);
            hash.Add(Checksum);
        }
    }
}
