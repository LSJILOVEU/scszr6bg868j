using WarCore.Sim;

namespace WarCore.Battle
{
    // Default placement ("nearest-slot"): the unit takes the slot nearest to where it stands.
    public sealed class NearestSlotPlacement : IAiPlacement
    {
        public ulong Fold(ulong digest) => digest;
        public SimVec3 Anchor(in AiUnit unit, int point, AiField field) => unit.Here;
    }
}
