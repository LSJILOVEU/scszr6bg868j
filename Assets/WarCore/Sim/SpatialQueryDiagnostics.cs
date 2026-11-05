using System.Runtime.CompilerServices;

// The test-side timing replay calls the same numeric primitives without adding a product clock.
[assembly: InternalsVisibleTo("WarCore.Sim.Tests")]
[assembly: InternalsVisibleTo("WarCore.Verification")]

namespace WarCore.Sim
{
    // An instance opt-in keeps diagnostics out of simulation state and avoids shared mutable state.
    // Reset per query: counts are bounded by the grid's int-sized membership budget and tree depth.
    internal sealed class SpatialQueryDiagnostics
    {
        internal long WalkCalls;
        internal long DictionaryLookups;
        internal long LeafMembers;
        internal long DedupChecks;
        internal long DuplicateSkips;
        internal long EmptyLookups;
        internal long HashProbes;
        internal long MaskRejectedCells;
        internal long DirectorySlotsScanned;
        internal long RegionNodes;
        internal long StaticMembers;
        internal long UnitMembers;
        internal long SkippedPartitions;
        internal long VisitedPartitions;
        internal long TraversalStarts;
        internal long StaticLookups;
        internal long UnitLookups;
        internal long StaticRegionNodes;
        internal long UnitRegionNodes;

        internal void Reset()
        {
            WalkCalls = 0;
            DictionaryLookups = 0;
            LeafMembers = 0;
            DedupChecks = 0;
            DuplicateSkips = 0;
            EmptyLookups = 0;
            HashProbes = 0;
            MaskRejectedCells = 0;
            DirectorySlotsScanned = 0;
            RegionNodes = 0;
            StaticMembers = 0;
            UnitMembers = 0;
            SkippedPartitions = 0;
            VisitedPartitions = 0;
            TraversalStarts = 0;
            StaticLookups = 0;
            UnitLookups = 0;
            StaticRegionNodes = 0;
            UnitRegionNodes = 0;
        }
    }
}
