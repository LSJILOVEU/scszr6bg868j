using WarCore.Sim;

namespace WarCore.Battle
{
    // Engage "hold" (the default): a unit that sees an enemy stands and fires, as before parts existed. It never looks at
    // the unit's nerve, so a battle with the pressure system on behaves as one without it.
    public sealed class HoldEngage : IAiEngage
    {
        public ulong Fold(ulong digest) => digest;
        public AiEngage Decide(in AiUnit unit, SimVec3 enemy, AiField field) => AiEngage.Hold;
        public AiEngage Unseen(in AiUnit unit, AiField field) => AiEngage.Advance;
        public AiEngage Melee(in AiUnit unit, SimVec3 enemy, AiField field) => AiEngage.Advance;
    }
}
