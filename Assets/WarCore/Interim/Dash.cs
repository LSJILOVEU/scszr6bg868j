using System;
using WarCore.Sim;

namespace WarCore.Interim
{
    // A unit's dash: one dash covers Distance in Ticks steps; a new dash is accepted Cooldown ticks after the tick of the last
    // accepted request. It exists only in worlds that give a unit a dash, so every other world keeps its exact snapshots and hashes.
    public readonly struct DashSpec : IStateHash
    {
        public const string ComponentId = "interim.dash-spec";
        // At most 300 ticks, 100 m, 10 m per tick (the movers' sweep bound) and a cooldown of 18 000 ticks. These bounds keep
        // every product the dash step forms (|total| * k, below 2^23 * 2^9) inside 32 bits.
        public const int MaxTicks = 300, MaxCooldown = 18000;
        public const long MaxDistanceRaw = 100 * SimFix.Scale, MaxStepRaw = 10 * SimFix.Scale;
        public SimFix Distance { get; }
        public int Ticks { get; }
        public int Cooldown { get; }
        public DashSpec(SimFix distance, int ticks, int cooldown)
        {
            if (ticks < 1 || ticks > MaxTicks) throw new ArgumentOutOfRangeException(nameof(ticks), "A dash lasts 1 to 300 ticks.");
            if (distance.Raw <= 0 || distance.Raw > MaxDistanceRaw || distance.Raw > MaxStepRaw * ticks)
                throw new ArgumentOutOfRangeException(nameof(distance), "A dash covers more than zero, at most 100 m and at most 10 m per tick.");
            if (cooldown < ticks || cooldown > MaxCooldown)
                throw new ArgumentOutOfRangeException(nameof(cooldown), "A dash cooldown is at least the dash's own ticks and at most 18000.");
            Distance = distance; Ticks = ticks; Cooldown = cooldown;
        }
        public void WriteState(StateHash hash) { hash.Add(Distance.Raw); hash.Add(Ticks); hash.Add(Cooldown); }
    }

    public sealed class DashSpecSerializer : IStableComponentSerializer<DashSpec>
    {
        public int TypeId => 2008;
        public int Version => 1;
        public void Write(SimStateWriter writer, in DashSpec value)
        {
            writer.Int64(value.Distance.Raw); writer.Int32(value.Ticks); writer.Int32(value.Cooldown);
        }
        public DashSpec Read(SimStateReader reader)
        {
            long distance = reader.Int64(); int ticks = reader.Int32(), cooldown = reader.Int32();
            SimStateReader.Require(ticks >= 1 && ticks <= DashSpec.MaxTicks && distance > 0 && distance <= DashSpec.MaxDistanceRaw
                && distance <= DashSpec.MaxStepRaw * ticks && cooldown >= ticks && cooldown <= DashSpec.MaxCooldown, "Invalid dash spec.");
            return new DashSpec(SimFix.FromRaw(distance), ticks, cooldown);
        }
    }

    // The unit's last accepted dash: its whole horizontal displacement (TotalX, TotalZ), the tick of the request, how many
    // steps it takes, and the first tick a new request is accepted. Its steps run on ticks Start + 1 .. Start + Ticks; a dash
    // that is blocked ends early by lowering Ticks. A unit starts with no dash and ready: Start -1, Ticks 0, Ready 0.
    public readonly struct DashState : IStateHash
    {
        public const string ComponentId = "interim.dash-state";
        public static DashState Initial => new DashState(default, default, -1, 0, 0);
        public SimFix TotalX { get; }
        public SimFix TotalZ { get; }
        public long Start { get; }
        public int Ticks { get; }
        public long Ready { get; }
        public DashState(SimFix totalX, SimFix totalZ, long start, int ticks, long ready)
        {
            if (ticks < 0 || ticks > DashSpec.MaxTicks) throw new ArgumentOutOfRangeException(nameof(ticks));
            TotalX = totalX; TotalZ = totalZ; Start = start; Ticks = ticks; Ready = ready;
        }
        // The step number (1 .. Ticks) the dash takes on this tick, or 0 when it takes none.
        public int StepAt(long tick) => tick > Start && tick - Start <= Ticks ? (int)(tick - Start) : 0;
        public DashState EndedAfter(int steps) => new DashState(TotalX, TotalZ, Start, steps, Ready);
        public void WriteState(StateHash hash)
        {
            hash.Add(TotalX.Raw); hash.Add(TotalZ.Raw); hash.Add(Start); hash.Add(Ticks); hash.Add(Ready);
        }
    }

    public sealed class DashStateSerializer : IStableComponentSerializer<DashState>
    {
        public int TypeId => 2009;
        public int Version => 1;
        public void Write(SimStateWriter writer, in DashState value)
        {
            writer.Int64(value.TotalX.Raw); writer.Int64(value.TotalZ.Raw); writer.Int64(value.Start); writer.Int32(value.Ticks); writer.Int64(value.Ready);
        }
        public DashState Read(SimStateReader reader)
        {
            long x = reader.Int64(), z = reader.Int64(), start = reader.Int64();
            int ticks = reader.Int32();
            long ready = reader.Int64();
            SimStateReader.Require(x >= -DashSpec.MaxDistanceRaw && x <= DashSpec.MaxDistanceRaw && z >= -DashSpec.MaxDistanceRaw
                && z <= DashSpec.MaxDistanceRaw && start >= -1 && ticks >= 0 && ticks <= DashSpec.MaxTicks && ready >= 0, "Invalid dash state.");
            return new DashState(SimFix.FromRaw(x), SimFix.FromRaw(z), start, ticks, ready);
        }
    }
}
