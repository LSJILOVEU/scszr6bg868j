using WarCore.Sim;

namespace WarCore.Battle
{
    // Engage "hold" (the default): a unit that sees an enemy stands and fires, as before parts existed.
    public sealed class HoldEngage : IAiEngage
    {
        public ulong Fold(ulong digest) => digest;
        public AiEngage Decide(in AiUnit unit, SimVec3 enemy, AiField field) => AiEngage.Hold;
    }
}
