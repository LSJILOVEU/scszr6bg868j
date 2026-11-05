using WarCore.Sim;

namespace WarCore.Interim
{
    // A desired displacement only. Crowd owns collision checks and the single Movement submission.
    public interface IControlledStep
    {
        bool TryGetStep(SimContext context, out EntityId unit, out SimVec3 displacement);
    }
}
