using WarCore.Sim;

namespace WarCore.Interim
{
    // Says whether a unit is busy with something that a dash may not interrupt or start during (DASH-T; the crossing of
    // TRV-4 binds one). Read only: answering must not change any state.
    public interface IUnitBusy
    {
        bool IsBusy(EntityId unit);
    }
}
