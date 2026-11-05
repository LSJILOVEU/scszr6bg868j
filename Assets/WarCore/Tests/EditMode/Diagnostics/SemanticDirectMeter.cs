namespace WarCore.Tests.Diagnostics
{
    // The historical name is retained for call-site continuity; the basis is selected at runtime.
    internal sealed class SemanticDirectMeter : IAllocationMeter
    {
        private readonly AllocationMeter meter = new AllocationMeter();

        public AllocationSample Read() => meter.Read();
        public AllocationSample Difference(AllocationSample before) => meter.Difference(before);
    }
}
