using System;
using WarCore.Sim;

namespace WarCore.Interim
{
    public interface ILocomotion
    {
        void Order(EntityId unit, SimVec3 target);
        // An order whose target stands on a layer (0 = the ground, the two-argument order). Only a world with floor plates
        // accepts another layer.
        void Order(EntityId unit, SimVec3 target, int layer)
        {
            if (layer != 0) throw new NotSupportedException("This locomotion has no layers.");
            Order(unit, target);
        }
        void Stop(EntityId unit);
        bool IsArrived(EntityId unit);
    }
}
