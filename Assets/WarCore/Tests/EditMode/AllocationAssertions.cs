using System;
using NUnit.Framework;

namespace WarCore.Tests
{
    internal static class AllocationAssertions
    {
        // Success performs no NUnit calls or formatting: observers run inside measured ticks.
        internal static void Check(AllocationSample sample)
        {
            if (sample.IsInconclusive)
                Assert.Inconclusive(sample.Describe());
            if (!sample.MeetsZeroContract)
                throw new InvalidOperationException(sample.Describe());
        }

        // Nested observers cannot retry a query inside Sim.Step. The owning full window checks indirect evidence.
        internal static void CheckDirect(AllocationSample sample)
        {
            if (sample.IsDirect)
                Check(sample);
        }

        // Memory deltas inside a tick are diagnostics; the complete window owns the exact memory check.
        internal static void CheckObserved(AllocationSample sample)
        {
            if (sample.Basis != AllocationBasis.Indirect || sample.CollectionsChanged)
                Check(sample);
        }

        internal static void Zero(AllocationSample sample)
        {
            TestContext.WriteLine(sample.Describe());
            Check(sample);
        }
    }
}
