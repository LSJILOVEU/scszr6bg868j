namespace WarCore.Sim
{
    public enum StableRestorePhase { Begin, IdentityMapped, ComponentsBound, ReferencesBound, SpatialRebuilt, Complete }

    // Cold diagnostics must observe only. They must not feed timings back into simulation decisions.
    public interface IStableRestoreObserver
    {
        void Reached(StableRestorePhase phase);
    }
}
