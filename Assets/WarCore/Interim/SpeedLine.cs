using System;
using WarCore.Sim;

namespace WarCore.Interim
{
    // One gear of a unit's speed (the "A" part of the stat line): a factor in permille, 1 to 4000, read every step.
    public interface ISpeedGear
    {
        int Permille(EntityId unit);
    }

    // STANCE-1: the speed line. It wraps the motion model the locomotion had (instant or inertia) and hands it the unit's speed
    // scaled by every gear (StatLine.Scale: all factors multiplied, then divided once), so the gears act before inertia. A unit
    // with a speed above zero never drops to zero (at least 1 raw). With no gear, or only gears at 1000, the wanted speed is the
    // unit's own speed exactly. The player's step asks Wanted directly. Nothing is saved: the gears read saved components.
    public sealed class SpeedLine : IMotionModel
    {
        private readonly IMotionModel inner;
        private readonly ISpeedGear[] gears = new ISpeedGear[StatLine.MaxFactors];
        private readonly int[] factors = new int[StatLine.MaxFactors];
        private int count;

        public SpeedLine(IMotionModel inner)
        {
            this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public IMotionModel Inner => inner;
        public int GearCount => count;

        public void AddGear(ISpeedGear gear)
        {
            if (gear == null) throw new ArgumentNullException(nameof(gear));
            for (int i = 0; i < count; i++)
                if (ReferenceEquals(gears[i], gear)) throw new ArgumentException("This gear is already on the line.", nameof(gear));
            if (count == gears.Length) throw new ArgumentException("A speed line takes at most " + StatLine.MaxFactors + " gears.", nameof(gear));
            gears[count++] = gear;
        }

        // The unit's own speed through every gear. A positive speed stays at least 1 raw.
        public SimFix Wanted(EntityId unit, SimFix own)
        {
            if (count == 0) return own;
            for (int i = 0; i < count; i++) factors[i] = gears[i].Permille(unit);
            long scaled = StatLine.Scale(own.Raw, new ReadOnlySpan<int>(factors, 0, count));
            return SimFix.FromRaw(own.Raw > 0 && scaled < 1 ? 1 : scaled);
        }

        public SimFix StepSpeed(EntityId unit, SimFix wanted) => inner.StepSpeed(unit, Wanted(unit, wanted));
    }
}
