using WarCore.Sim;

namespace WarCore.Interim
{
    // How far a moving unit travels in one tick. The movers ask this once per unit per tick, before terrain scaling and
    // before any wall or crowd check, and use the answer wherever they used the unit's own speed. The only model is the
    // instant one: the unit always travels its full speed, which is what the movers did before this seam existed.
    public interface IMotionModel
    {
        SimFix StepSpeed(EntityId unit, SimFix wanted);
    }

    public sealed class InstantMotion : IMotionModel
    {
        public SimFix StepSpeed(EntityId unit, SimFix wanted) => wanted;
    }

    // Where a unit's motion limits live, in worlds whose recipe opens them (other worlds keep their exact snapshots and hashes).
    // Each field is per tick (per tick squared for the two rates); zero means "no limit". Nothing reads them yet: the instant
    // model ignores them, and a world starts with MaxSpeed equal to the unit's own speed and the other three at zero.
    // The unit of MaxTurn is not fixed yet; it is settled by the model that first uses it.
    public readonly struct MotionLimits : IStateHash
    {
        public const string ComponentId = "interim.motion-limits";
        public SimFix MaxSpeed { get; }
        public SimFix Accel { get; }
        public SimFix Decel { get; }
        public SimFix MaxTurn { get; }
        public MotionLimits(SimFix maxSpeed, SimFix accel, SimFix decel, SimFix maxTurn)
        {
            if (maxSpeed.Raw < 0 || accel.Raw < 0 || decel.Raw < 0 || maxTurn.Raw < 0)
                throw new System.ArgumentOutOfRangeException(nameof(maxSpeed), "Motion limits cannot be negative.");
            MaxSpeed = maxSpeed; Accel = accel; Decel = decel; MaxTurn = maxTurn;
        }
        public void WriteState(StateHash hash) { hash.Add(MaxSpeed.Raw); hash.Add(Accel.Raw); hash.Add(Decel.Raw); hash.Add(MaxTurn.Raw); }
    }

    public sealed class MotionLimitsSerializer : IStableComponentSerializer<MotionLimits>
    {
        public int TypeId => 2005;
        public int Version => 1;
        public void Write(SimStateWriter writer, in MotionLimits value)
        {
            writer.Int64(value.MaxSpeed.Raw); writer.Int64(value.Accel.Raw); writer.Int64(value.Decel.Raw); writer.Int64(value.MaxTurn.Raw);
        }
        public MotionLimits Read(SimStateReader reader) => new MotionLimits(
            SimFix.FromRaw(reader.Int64()), SimFix.FromRaw(reader.Int64()), SimFix.FromRaw(reader.Int64()), SimFix.FromRaw(reader.Int64()));
    }
}
