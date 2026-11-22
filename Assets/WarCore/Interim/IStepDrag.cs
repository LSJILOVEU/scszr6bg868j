using WarCore.Sim;

namespace WarCore.Interim
{
    // SLOPE-1: one registered drag of the foot step (the "B" part of the speed line: wounds, load, stamina...). It reads
    // components that are already saved and stores nothing itself. The value is permille, 1..1000: a drag only slows.
    public interface IStepDrag
    {
        int Permille(EntityId unit);
    }

    // What ExplainStep returns: Blocked when the ground under the unit is blocked (surface factor 0, the step never reaches
    // the stat line), otherwise the stat line's receipt with the drags [surface, slope, registered drags...].
    public sealed class StepReceipt
    {
        public bool Blocked { get; }
        public StatReceipt Receipt { get; }

        internal StepReceipt(bool blocked, StatReceipt receipt)
        {
            Blocked = blocked;
            Receipt = receipt;
        }
    }
}
