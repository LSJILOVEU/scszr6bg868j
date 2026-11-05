using WarCore.Sim;

namespace WarCore.Interim
{
    public interface ILocomotion
    {
        void Order(EntityId unit, SimVec3 target);
        void Stop(EntityId unit);
        bool IsArrived(EntityId unit);
    }
}
