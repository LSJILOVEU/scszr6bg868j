using WarCore.Verification.Fixtures;

namespace WarCore.Verification
{
    internal sealed class SemanticPlayerMeter : IAllocationMeter
    {
        private readonly AllocationMeter meter = new AllocationMeter();

        public AllocationSample Read() => meter.Read();
        public AllocationSample Difference(AllocationSample before) => meter.Difference(before);
    }
}
