using WarCore.Sim;

namespace WarCore.Interim
{
    // DOWN-1: a rule that keeps a unit from walking by itself this step (a stunned unit, for one). A held unit takes no ordinary
    // step: its move intent, waypoint and stall count stay as they are, and nobody else moves it. A dash already under way is
    // not held. It reads only saved state, so a restored world answers the same.
    public interface IUnitHold
    {
        bool Holds(EntityId unit);
    }
}
