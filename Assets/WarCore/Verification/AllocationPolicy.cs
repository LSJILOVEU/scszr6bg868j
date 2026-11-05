using System;

namespace WarCore.Verification.Fixtures
{
    internal static class AllocationPolicy
    {
        internal static void Check(AllocationSample sample)
        {
            // Standalone reports have no NUnit outcome; inconclusive evidence must remain visible.
            if (sample.IsInconclusive)
            {
                Console.WriteLine(sample.Describe());
                return;
            }
            if (!sample.MeetsZeroContract)
                throw new InvalidOperationException(sample.Describe());
        }

        internal static void CheckDirect(AllocationSample sample)
        {
            if (sample.IsDirect)
                Check(sample);
        }

        internal static void CheckObserved(AllocationSample sample)
        {
            if (sample.Basis != AllocationBasis.Indirect || sample.CollectionsChanged)
                Check(sample);
        }
    }
}
